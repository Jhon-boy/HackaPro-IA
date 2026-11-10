using System.Text;
using InfraAgent.Models;

namespace InfraAgent.Policies;

public sealed class Policy
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public Severity Severity { get; init; } = Severity.Medium;

    /// <summary>Tipos de recurso Terraform a los que aplica. Vacío = regla general (aplica a todo).</summary>
    public IReadOnlyList<string> AppliesTo { get; init; } = Array.Empty<string>();

    public string Rule { get; init; } = "";
    public string Remediation { get; init; } = "";
    public string? CompliantExample { get; init; }

    public bool IsGeneral => AppliesTo.Count == 0;
}

/// <summary>Catálogo versionado de políticas y reglas de seguridad del banco.</summary>
public sealed class PolicyCatalog
{
    private readonly Dictionary<string, Policy> _byId;

    public PolicyCatalog(string version, string description, IEnumerable<Policy> policies)
    {
        Version = version;
        Description = description;
        Policies = policies.ToList();
        _byId = Policies.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string Version { get; }
    public string Description { get; }
    public IReadOnlyList<Policy> Policies { get; }

    public static PolicyCatalog Empty { get; } = new("sin-catalogo", "", Array.Empty<Policy>());

    public Policy? Get(string id) => _byId.TryGetValue(id.Trim(), out var p) ? p : null;
    public bool Exists(string id) => _byId.ContainsKey(id.Trim());

    public IReadOnlyList<Policy> ByCategory(string category) =>
        Policies.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Políticas generales + las que aplican a alguno de los tipos de recurso dados.</summary>
    public IReadOnlyList<Policy> ApplicableTo(IEnumerable<string> resourceTypes)
    {
        var types = new HashSet<string>(resourceTypes.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        return Policies
            .Where(p => p.IsGeneral || p.AppliesTo.Any(t => types.Contains(Normalize(t))))
            .ToList();
    }

    private static string Normalize(string t) => t.Trim().ToLowerInvariant();

    /// <summary>Texto compacto para inyectar en el prompt (determinista y citable por id).</summary>
    public string Render(IEnumerable<Policy> policies, bool includeExamples = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Catálogo de políticas v{Version}");
        foreach (var p in policies)
        {
            sb.AppendLine();
            sb.AppendLine($"### {p.Id} — {p.Title} [{p.Severity}] ({p.Category})");
            if (!p.IsGeneral) sb.AppendLine($"Aplica a: {string.Join(", ", p.AppliesTo)}");
            sb.AppendLine($"Regla: {p.Rule.Trim()}");
            if (!string.IsNullOrWhiteSpace(p.Remediation)) sb.AppendLine($"Remediación: {p.Remediation.Trim()}");
            if (includeExamples && !string.IsNullOrWhiteSpace(p.CompliantExample))
            {
                sb.AppendLine("Ejemplo conforme:");
                sb.AppendLine("```hcl");
                sb.AppendLine(p.CompliantExample.TrimEnd());
                sb.AppendLine("```");
            }
        }
        return sb.ToString();
    }
}
