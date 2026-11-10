using System.Diagnostics;
using System.Text.Json;
using InfraAgent.Abstractions;
using InfraAgent.Auditing;
using InfraAgent.Common;
using InfraAgent.Models;
using InfraAgent.Security;
using InfraAgent.Settings;
using Microsoft.Extensions.Options;

namespace InfraAgent.Tools;

/// <summary>
/// Único punto por el que el modelo ejecuta herramientas. Aplica lista cerrada por rol, validación,
/// auditoría, enmascarado de secretos y límites de tamaño (TRUNCADO).
/// </summary>
public sealed class ToolInvoker : IToolInvoker
{
    private readonly Dictionary<string, IAgentTool> _tools;
    private readonly IAuditSink _audit;
    private readonly AnalysisSettings _analysis;
    private readonly FeatureSettings _features;

    public ToolInvoker(IEnumerable<IAgentTool> tools, IAuditSink audit, IOptions<InfraAgentSettings> settings)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _audit = audit;
        _analysis = settings.Value.Analysis;
        _features = settings.Value.Features;
    }

    public IReadOnlyList<ToolSpec> GetSpecs(AgentRole role) =>
        ToolAllowList.For(role)
            .Where(_tools.ContainsKey)
            .Where(n => ToolAllowList.IsEnabled(n, _features))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new ToolSpec(n, _tools[n].Description, _tools[n].ParametersJsonSchema))
            .ToList();

    public async Task<string> InvokeAsync(AgentRole role, string toolName, string argumentsJson, ToolContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!ToolAllowList.IsAllowed(role, toolName) || !_tools.TryGetValue(toolName, out var tool))
        {
            _audit.Record(new AuditEntry(AuditKind.Security, $"{role}:{toolName}", false, sw.ElapsedMilliseconds,
                context.CorrelationId, "Herramienta no permitida para el agente."));
            return "ERROR: herramienta no permitida.";
        }

        if (!ToolAllowList.IsEnabled(toolName, _features))
        {
            _audit.Record(new AuditEntry(AuditKind.Security, $"{role}:{toolName}", false, sw.ElapsedMilliseconds,
                context.CorrelationId, "Herramienta deshabilitada por configuración."));
            return "ERROR: esta capacidad está deshabilitada por configuración.";
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Los argumentos deben ser un objeto JSON.");

            context.ToolsUsed.Add(toolName);
            var raw = await tool.InvokeAsync(role, doc.RootElement.Clone(), context, ct);
            var masked = SecretMasker.MaskText(raw);
            var (limited, truncated) = TextLimits.Truncate(masked, _analysis.MaxToolOutputChars);

            _audit.Record(new AuditEntry(AuditKind.Tool, $"{role}:{toolName}", true, sw.ElapsedMilliseconds,
                context.CorrelationId, truncated ? "TRUNCADO" : null));
            return limited;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            _audit.Record(new AuditEntry(AuditKind.Tool, $"{role}:{toolName}", false, sw.ElapsedMilliseconds,
                context.CorrelationId, "Validación: " + SecretMasker.MaskText(ex.Message)));
            return $"ERROR de validación: {SecretMasker.MaskText(ex.Message)}";
        }
        catch (Exception ex)
        {
            _audit.Record(new AuditEntry(AuditKind.Tool, $"{role}:{toolName}", false, sw.ElapsedMilliseconds,
                context.CorrelationId, ex.GetType().Name));
            return $"ERROR: la herramienta falló (correlación {context.CorrelationId}).";
        }
    }
}
