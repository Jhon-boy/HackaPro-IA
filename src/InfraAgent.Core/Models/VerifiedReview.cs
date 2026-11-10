namespace InfraAgent.Core.Models;

/// <summary>Resultado de verificar los hallazgos de un agente contra el código real.</summary>
public sealed class VerifiedReview
{
    public string Summary { get; set; } = "";
    public List<Finding> Findings { get; set; } = new();
    public List<ComplianceItem> Compliance { get; set; } = new();

    /// <summary>Hallazgos descartados por falta de evidencia verificable o política inexistente.</summary>
    public List<DiscardedFinding> Discarded { get; set; } = new();
    public List<string> AnalyzedFiles { get; set; } = new();
    public bool Truncated { get; set; }
}

public sealed class DiscardedFinding
{
    public string PolicyId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
}
