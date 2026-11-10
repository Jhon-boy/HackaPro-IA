using InfraAgent.Chat;
using InfraAgent.Settings;
using InfraAgent.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraAgent.Functions.Functions;

/// <summary>Modo 2: chat con el orquestador (texto + imágenes de arquitectura).</summary>
public sealed class ChatFunctions
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ChatFunctions> _logger;

    public ChatFunctions(IServiceProvider services, ILogger<ChatFunctions> logger)
    {
        _services = services;
        _logger = logger;
    }

    [Function("Chat")]
    public Task<IActionResult> Chat(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "chat")] HttpRequest req,
        CancellationToken ct) =>
        ApiGuard.RunAsync(_logger, async () =>
        {
            var settings = _services.GetRequiredService<IOptions<InfraAgentSettings>>().Value.Chat;

            // Imágenes en Base64 (~4/3) + mensaje + archivos/arquitectura del turno anterior.
            var maxBody = (long)settings.MaxImages * settings.MaxImageBytes * 4 / 3
                          + (long)settings.MaxGeneratedFiles * settings.MaxGeneratedFileBytes * 2
                          + 512 * 1024;

            var request = await ApiGuard.ReadBodyAsync<ChatRequest>(req, maxBody, ct);
            var response = await _services.GetRequiredService<ChatService>().HandleAsync(request, ct);
            return ApiGuard.Ok(response);
        });

    [Function("ChatDelete")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "chat/{conversationId}")] HttpRequest req,
        string conversationId,
        CancellationToken ct) =>
        ApiGuard.RunAsync(_logger, async () =>
        {
            await _services.GetRequiredService<ChatService>().DeleteConversationAsync(conversationId, ct);
            return new NoContentResult();
        });
}
