namespace InfraAgent.Auditing;

public enum AuditKind
{
    Tool,
    Agent,
    Review,
    Chat,
    Security
}

/// <summary>Entrada de auditoría. Nunca contiene valores de secretos ni contenido completo de archivos.</summary>
public sealed record AuditEntry(
    AuditKind Kind,
    string Name,
    bool Success,
    long DurationMs,
    string CorrelationId,
    string? Detail = null);

public interface IAuditSink
{
    void Record(AuditEntry entry);
}

public sealed class NullAuditSink : IAuditSink
{
    public static readonly NullAuditSink Instance = new();
    public void Record(AuditEntry entry) { }
}
