using System.Text;
using InfraAgent.Abstractions;
using InfraAgent.Agents;
using InfraAgent.Models;
using InfraAgent.Policies;
using InfraAgent.Review;
using InfraAgent.Settings;
using InfraAgent.Terraform;
using Microsoft.Extensions.Options;

namespace InfraAgent.Review;

/// <summary>
/// Orquesta a los agentes revisores (Seguridad y Políticas) sobre un conjunto de archivos y verifica
/// por código la evidencia de lo que proponen.
/// </summary>
public sealed class ReviewerService
{
    private static readonly HashSet<string> SecurityCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "security", "network", "identity", "data", "encryption", "monitoring"
    };

    private readonly AgentService _agents;
    private readonly PolicyCatalog _catalog;
    private readonly InfraAgentSettings _settings;

    public ReviewerService(AgentService agents, PolicyCatalog catalog, IOptions<InfraAgentSettings> settings)
    {
        _agents = agents;
        _catalog = catalog;
        _settings = settings.Value;
    }

    public Task<VerifiedReview> ReviewSecurityAsync(FileBundle bundle, ArchitectureModel? architecture, ToolContext ctx, CancellationToken ct) =>
        ReviewAsync(AgentRole.Security, bundle, architecture, ctx, ct);

    public Task<VerifiedReview> ReviewPoliciesAsync(FileBundle bundle, ArchitectureModel? architecture, ToolContext ctx, CancellationToken ct) =>
        ReviewAsync(AgentRole.Policy, bundle, architecture, ctx, ct);

    private async Task<VerifiedReview> ReviewAsync(AgentRole role, FileBundle bundle, ArchitectureModel? architecture, ToolContext ctx, CancellationToken ct)
    {
        if (bundle.Files.Count == 0)
            return new VerifiedReview { Summary = "No hay archivos de infraestructura para revisar." };

        var types = TerraformInspector.ExtractResourceTypes(bundle.Files.Values)
            .Concat(architecture?.Components.Select(c => c.Type) ?? Enumerable.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var applicable = _catalog.ApplicableTo(types);
        if (role == AgentRole.Security)
            applicable = applicable.Where(p => SecurityCategories.Contains(p.Category)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(role == AgentRole.Security
            ? "Revisa la SEGURIDAD de los siguientes archivos Terraform según las reglas indicadas."
            : "Evalúa el CUMPLIMIENTO de las políticas de la organización en los siguientes archivos Terraform.");
        sb.AppendLine();

        if (architecture is not null)
        {
            sb.AppendLine("## Arquitectura (contexto)");
            sb.AppendLine(JsonOutput.Serialize(architecture));
            sb.AppendLine();
        }

        sb.AppendLine("## Reglas a verificar");
        sb.AppendLine(applicable.Count == 0 ? "No hay reglas aplicables en el catálogo." : _catalog.Render(applicable));
        sb.AppendLine();
        sb.AppendLine("## Archivos (cada línea va numerada como `  12 | código`; el número NO es parte del código)");
        sb.Append(bundle.Render());

        var output = await _agents.RunJsonAsync<ReviewerOutput>(role, sb.ToString(), ctx, null, ct);

        foreach (var f in output.Findings)
        {
            f.Evidence ??= new Evidence();
            f.Evidence.Snippet = FileBundle.StripLineNumbers(f.Evidence.Snippet ?? "");
        }

        var verified = EvidenceVerifier.Verify(output, bundle.Files, _catalog);
        verified.Truncated = bundle.Truncated;
        return verified;
    }

    /// <summary>Evalúa una arquitectura (aún sin código) contra las políticas del banco.</summary>
    public async Task<List<ComplianceItem>> EvaluateArchitectureAsync(ArchitectureModel architecture, ToolContext ctx, CancellationToken ct)
    {
        var maxInline = _settings.Generation.MaxPoliciesInline;
        var policies = _catalog.Policies.Count <= maxInline
            ? _catalog.Policies
            : _catalog.ApplicableTo(architecture.Components.Select(c => c.Type));

        var sb = new StringBuilder();
        sb.AppendLine("Evalúa la ARQUITECTURA (todavía sin código) contra las políticas de la organización.");
        sb.AppendLine("Para cada política aplicable indica Cumple, NoCumple, Parcial, NoAplica o Desconocido, con justificación y el ajuste necesario.");
        sb.AppendLine("No hay archivos: deja `findings` vacío.");
        sb.AppendLine();
        sb.AppendLine("## Arquitectura");
        sb.AppendLine(JsonOutput.Serialize(architecture));
        sb.AppendLine();
        sb.AppendLine("## Políticas");
        sb.AppendLine(policies.Count == 0 ? "No hay políticas aplicables en el catálogo." : _catalog.Render(policies));

        var output = await _agents.RunJsonAsync<ReviewerOutput>(AgentRole.Policy, sb.ToString(), ctx, null, ct);
        return output.Compliance
            .Where(c => _catalog.Exists(c.PolicyId))
            .Select(c => { c.PolicyId = _catalog.Get(c.PolicyId)!.Id; return c; })
            .GroupBy(c => c.PolicyId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
}
