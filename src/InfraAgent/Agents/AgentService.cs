using System.Diagnostics;
using System.Text.Json;
using InfraAgent.Abstractions;
using InfraAgent.Knowledge;
using InfraAgent.Auditing;
using InfraAgent.Models;
using InfraAgent.Settings;
using Microsoft.Extensions.Options;

namespace InfraAgent.Agents;

/// <summary>
/// Ejecuta un agente especialista: arma instrucciones (repo), llama a Foundry mediante <see cref="IAgentRunner"/>
/// y, cuando se espera JSON, reintenta una vez pidiendo la corrección del formato.
/// </summary>
public sealed class AgentService
{
    private readonly IAgentRunner _runner;
    private readonly IToolInvoker _tools;
    private readonly PromptBuilder _prompts;
    private readonly IAuditSink _audit;
    private readonly FoundrySettings _foundry;

    public AgentService(
        IAgentRunner runner,
        IToolInvoker tools,
        PromptBuilder prompts,
        IAuditSink audit,
        IOptions<InfraAgentSettings> settings)
    {
        _runner = runner;
        _tools = tools;
        _prompts = prompts;
        _audit = audit;
        _foundry = settings.Value.Foundry;
    }

    public async Task<AgentRunResult> RunTextAsync(
        AgentRole role,
        string message,
        ToolContext context,
        IReadOnlyList<ImageInput>? images = null,
        string? threadId = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var request = new AgentRunRequest(
            role,
            _prompts.BuildInstructions(role),
            message,
            images ?? Array.Empty<ImageInput>(),
            threadId,
            TimeSpan.FromSeconds(_foundry.RunTimeoutSeconds));

        try
        {
            var result = await _runner.RunAsync(request, _tools, context, ct);
            _audit.Record(new AuditEntry(AuditKind.Agent, role.ToString(), true, sw.ElapsedMilliseconds,
                context.CorrelationId, $"herramientas={result.ToolCalls}"));
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _audit.Record(new AuditEntry(AuditKind.Agent, role.ToString(), false, sw.ElapsedMilliseconds,
                context.CorrelationId, ex.GetType().Name));
            throw;
        }
    }

    /// <summary>Ejecuta el agente y deserializa su respuesta JSON, con un reintento de reparación.</summary>
    public async Task<T> RunJsonAsync<T>(
        AgentRole role,
        string message,
        ToolContext context,
        IReadOnlyList<ImageInput>? images = null,
        CancellationToken ct = default)
    {
        var first = await RunTextAsync(role, message, context, images, null, ct);
        try
        {
            try
            {
                return JsonOutput.Parse<T>(first.Text);
            }
            catch (JsonException ex)
            {
                var repair = $"Tu respuesta anterior no es un JSON válido para el formato pedido ({ex.Message}). " +
                             "Responde SOLO con el objeto JSON corregido, sin texto adicional ni cercos de código.";
                var second = await RunTextAsync(role, repair, context, null, first.ThreadId, ct);
                try
                {
                    return JsonOutput.Parse<T>(second.Text);
                }
                catch (JsonException ex2)
                {
                    throw new InvalidOperationException($"El agente {role.DisplayName()} no devolvió JSON válido: {ex2.Message}");
                }
            }
        }
        finally
        {
            // Los hilos de los especialistas son efímeros: no se conservan en Foundry.
            await _runner.DeleteThreadAsync(first.ThreadId, CancellationToken.None);
        }
    }
}
