using System.Diagnostics;
using System.Text.Json;
using InfraAgent.Application.Agents;
using InfraAgent.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraAgent.Functions.Http;

/// <summary>Traduce excepciones a respuestas HTTP coherentes, sin filtrar detalles internos ni secretos.</summary>
internal static class ApiGuard
{
    public static async Task<IActionResult> RunAsync(ILogger logger, Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return Json(StatusCodes.Status400BadRequest, new { error = SecretMasker.MaskText(ex.Message) });
        }
        catch (JsonException)
        {
            return Json(StatusCodes.Status400BadRequest, new { error = "El cuerpo de la solicitud no es un JSON válido." });
        }
        catch (OptionsValidationException ex)
        {
            logger.LogError("Configuración inválida: {Failures}", string.Join(" | ", ex.Failures));
            return Json(StatusCodes.Status503ServiceUnavailable,
                new { error = "La configuración de InfraAgent es inválida.", details = ex.Failures });
        }
        catch (TimeoutException ex)
        {
            return Json(StatusCodes.Status504GatewayTimeout, new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return new StatusCodeResult(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            var correlation = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
            logger.LogError(ex, "Error no controlado. Correlación {Correlation}", correlation);
            return Json(StatusCodes.Status500InternalServerError,
                new { error = "Error interno al procesar la solicitud.", correlation });
        }
    }

    public static IActionResult Json(int status, object body) =>
        new ContentResult
        {
            StatusCode = status,
            ContentType = "application/json; charset=utf-8",
            Content = JsonOutput.Serialize(body)
        };

    public static IActionResult Ok(object body) => Json(StatusCodes.Status200OK, body);

    /// <summary>Lee el cuerpo JSON con un tope de tamaño (evita agotar memoria con cuerpos enormes).</summary>
    public static async Task<T> ReadBodyAsync<T>(HttpRequest request, long maxBytes, CancellationToken ct)
    {
        if (request.ContentLength is { } length && length > maxBytes)
            throw new ArgumentException($"El cuerpo de la solicitud supera el máximo permitido ({maxBytes / 1024} KB).");

        using var limited = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (limited.Length + read > maxBytes)
                throw new ArgumentException($"El cuerpo de la solicitud supera el máximo permitido ({maxBytes / 1024} KB).");
            limited.Write(buffer, 0, read);
        }

        limited.Position = 0;
        var value = await JsonSerializer.DeserializeAsync<T>(limited, JsonOutput.Options, ct);
        return value ?? throw new ArgumentException("El cuerpo de la solicitud está vacío.");
    }
}
