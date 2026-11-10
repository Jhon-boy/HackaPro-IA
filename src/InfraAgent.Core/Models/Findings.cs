using System.Text.Json.Serialization;

namespace InfraAgent.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>Evidencia obligatoria de un hallazgo: archivo y fragmento literal.</summary>
public sealed class Evidence
{
    public string File { get; set; } = "";
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }

    /// <summary>Fragmento LITERAL del archivo (se verifica por código).</summary>
    public string Snippet { get; set; } = "";
}

public sealed class Finding
{
    /// <summary>Id de la política o regla del catálogo (p. ej. SEC-001).</summary>
    public string PolicyId { get; set; } = "";
    public string Title { get; set; } = "";
    public Severity Severity { get; set; } = Severity.Medium;
    public string Description { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string? Resource { get; set; }
    public Evidence Evidence { get; set; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComplianceStatus
{
    Cumple,
    NoCumple,
    Parcial,
    NoAplica,
    Desconocido
}

/// <summary>Resultado de contrastar la arquitectura con una política.</summary>
public sealed class ComplianceItem
{
    public string PolicyId { get; set; } = "";
    public ComplianceStatus Status { get; set; } = ComplianceStatus.Desconocido;
    public string Justification { get; set; } = "";

    /// <summary>Ajuste necesario para cumplir (si aplica).</summary>
    public string? Adjustment { get; set; }
}

/// <summary>Salida de los agentes revisores (Seguridad / Políticas) antes de la verificación por código.</summary>
public sealed class ReviewerOutput
{
    public string Summary { get; set; } = "";
    public List<Finding> Findings { get; set; } = new();
    public List<ComplianceItem> Compliance { get; set; } = new();
}
