using InfraAgent.Core.Auditing;
using Microsoft.Extensions.Logging;

namespace InfraAgent.Infrastructure.Auditing;

/// <summary>
/// Auditoría estructurada vía ILogger: en Azure Functions llega a Application Insights (tabla traces)
/// con las propiedades Kind, Name, Success, DurationMs y CorrelationId. Sin estado local.
/// </summary>
public sealed class LoggingAuditSink : IAuditSink
{
    private readonly ILogger<LoggingAuditSink> _logger;
    public LoggingAuditSink(ILogger<LoggingAuditSink> logger) => _logger = logger;

    public void Record(AuditEntry entry)
    {
        var level = entry.Success ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(level,
            "AUDIT {Kind} {Name} success={Success} durationMs={DurationMs} correlation={CorrelationId} detail={Detail}",
            entry.Kind, entry.Name, entry.Success, entry.DurationMs, entry.CorrelationId, entry.Detail ?? "");
    }
}
