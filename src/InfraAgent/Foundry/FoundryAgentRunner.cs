using System.Diagnostics;
using System.Text;
using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using InfraAgent.Abstractions;
using InfraAgent.Models;
using InfraAgent.Security;
using InfraAgent.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraAgent.Foundry;

/// <summary>
/// Ejecuta un agente YA DESPLEGADO en Azure AI Foundry. Las instrucciones vienen del repositorio
/// (se envían en cada ejecución) y las herramientas se ejecutan en NUESTRO código: cuando Foundry
/// pide una herramienta (requires_action) se resuelve aquí mediante <see cref="IToolInvoker"/>.
/// </summary>
public sealed class FoundryAgentRunner : IAgentRunner
{
    private readonly PersistentAgentsClient _client;
    private readonly FoundrySettings _settings;
    private readonly ILogger<FoundryAgentRunner> _logger;

    public FoundryAgentRunner(TokenCredential credential, IOptions<InfraAgentSettings> settings, ILogger<FoundryAgentRunner> logger)
    {
        _settings = settings.Value.Foundry;
        _logger = logger;
        _client = new PersistentAgentsClient(_settings.Endpoint, credential);
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, IToolInvoker tools, ToolContext context, CancellationToken ct)
    {
        var agentId = _settings.ResolveAgentId(request.Role);
        var uploadedFiles = new List<string>();
        string threadId;

        if (string.IsNullOrWhiteSpace(request.ThreadId))
        {
            PersistentAgentThread thread = (await _client.Threads.CreateThreadAsync(cancellationToken: ct)).Value;
            threadId = thread.Id;
        }
        else
        {
            threadId = request.ThreadId;
        }

        try
        {
            await AddUserMessageAsync(threadId, request, uploadedFiles, ct);

            var toolDefinitions = tools.GetSpecs(request.Role)
                .Select(s => (ToolDefinition)new FunctionToolDefinition(s.Name, s.Description, BinaryData.FromString(s.ParametersJsonSchema)))
                .ToList();

            ThreadRun run = (await _client.Runs.CreateRunAsync(
                threadId,
                agentId,
                overrideInstructions: request.Instructions,
                overrideTools: toolDefinitions,
                cancellationToken: ct)).Value;

            var toolCalls = 0;
            var rounds = 0;
            var clock = Stopwatch.StartNew();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (clock.Elapsed > request.Timeout)
                {
                    await TryCancelAsync(threadId, run.Id);
                    throw new TimeoutException($"El agente {request.Role.DisplayName()} superó el tiempo máximo ({(int)request.Timeout.TotalSeconds} s).");
                }

                if (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress || run.Status == RunStatus.Cancelling)
                {
                    await Task.Delay(_settings.PollIntervalMs, ct);
                    run = (await _client.Runs.GetRunAsync(threadId, run.Id, ct)).Value;
                    continue;
                }

                if (run.Status == RunStatus.RequiresAction && run.RequiredAction is SubmitToolOutputsAction submit)
                {
                    if (++rounds > _settings.MaxToolRounds)
                    {
                        await TryCancelAsync(threadId, run.Id);
                        throw new InvalidOperationException($"El agente {request.Role.DisplayName()} superó el máximo de {_settings.MaxToolRounds} rondas de herramientas.");
                    }

                    var outputs = new List<ToolOutput>();
                    foreach (var call in submit.ToolCalls.OfType<RequiredFunctionToolCall>())
                    {
                        toolCalls++;
                        var output = await tools.InvokeAsync(request.Role, call.Name, call.Arguments, context, ct);
                        outputs.Add(new ToolOutput(call.Id, output));
                    }

                    run = (await _client.Runs.SubmitToolOutputsToRunAsync(run, outputs, ct)).Value;
                    continue;
                }

                break;
            }

            if (run.Status != RunStatus.Completed)
            {
                var detail = SecretMasker.MaskText(run.LastError?.Message ?? "sin detalle");
                _logger.LogWarning("Run de Foundry terminó en {Status} para {Role}: {Code}", run.Status, request.Role, run.LastError?.Code);
                throw new InvalidOperationException($"El agente {request.Role.DisplayName()} terminó en estado {run.Status}: {detail}");
            }

            var text = await ReadLastAssistantTextAsync(threadId, run.Id, ct);
            return new AgentRunResult(text, threadId, toolCalls);
        }
        finally
        {
            foreach (var fileId in uploadedFiles)
            {
                try { await _client.Files.DeleteFileAsync(fileId, CancellationToken.None); }
                catch (Exception ex) { _logger.LogDebug(ex, "No se pudo eliminar el archivo temporal de Foundry."); }
            }
        }
    }

    public async Task DeleteThreadAsync(string threadId, CancellationToken ct)
    {
        try
        {
            await _client.Threads.DeleteThreadAsync(threadId, ct);
        }
        catch (Exception ex) when (ex is RequestFailedException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "No se pudo eliminar el hilo de Foundry.");
        }
    }

    private async Task AddUserMessageAsync(string threadId, AgentRunRequest request, List<string> uploadedFiles, CancellationToken ct)
    {
        if (request.Images.Count == 0)
        {
            await _client.Messages.CreateMessageAsync(threadId, MessageRole.User, request.UserMessage, cancellationToken: ct);
            return;
        }

        var blocks = new List<MessageInputContentBlock> { new MessageInputTextBlock(request.UserMessage) };
        var index = 0;
        foreach (var image in request.Images)
        {
            index++;
            var extension = image.MimeType == "image/png" ? "png" : "jpg";
            using var stream = new MemoryStream(image.Data, writable: false);
            PersistentAgentFileInfo file = (await _client.Files.UploadFileAsync(
                stream, PersistentAgentFilePurpose.Agents, $"imagen-{index}.{extension}", ct)).Value;
            uploadedFiles.Add(file.Id);
            blocks.Add(new MessageInputImageFileBlock(new MessageImageFileParam(file.Id)));
        }

        await _client.Messages.CreateMessageAsync(threadId, MessageRole.User, blocks, cancellationToken: ct);
    }

    private async Task<string> ReadLastAssistantTextAsync(string threadId, string runId, CancellationToken ct)
    {
        await foreach (var message in _client.Messages.GetMessagesAsync(
                           threadId, runId: runId, order: ListSortOrder.Descending, cancellationToken: ct))
        {
            if (message.Role != MessageRole.Agent) continue;

            var sb = new StringBuilder();
            foreach (var content in message.ContentItems.OfType<MessageTextContent>())
                sb.AppendLine(content.Text);

            var text = sb.ToString().Trim();
            if (text.Length > 0) return text;
        }

        return "";
    }

    private async Task TryCancelAsync(string threadId, string runId)
    {
        try { await _client.Runs.CancelRunAsync(threadId, runId); }
        catch (Exception ex) { _logger.LogDebug(ex, "No se pudo cancelar el run de Foundry."); }
    }
}
