using System.Text.RegularExpressions;
using InfraAgent.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace InfraAgent.Policies;

/// <summary>Carga y valida el catálogo de políticas desde YAML.</summary>
public static partial class PolicyCatalogLoader
{
    [GeneratedRegex(@"^[A-Z]{2,6}-\d{3}$")]
    private static partial Regex IdPattern();

    public static PolicyCatalog LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No se encontró el catálogo de políticas: {path}", path);
        return LoadFromYaml(File.ReadAllText(path));
    }

    public static PolicyCatalog LoadFromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        CatalogDto dto;
        try
        {
            dto = deserializer.Deserialize<CatalogDto>(yaml) ?? new CatalogDto();
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"El catálogo de políticas no es un YAML válido: {ex.Message}", ex);
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(dto.Version)) errors.Add("Falta 'version'.");
        if (dto.Policies is null || dto.Policies.Count == 0) errors.Add("El catálogo no tiene políticas.");

        var policies = new List<Policy>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in dto.Policies ?? new())
        {
            var id = (p.Id ?? "").Trim();
            if (!IdPattern().IsMatch(id)) { errors.Add($"Id de política inválido: '{id}' (formato esperado SEC-001)."); continue; }
            if (!seen.Add(id)) { errors.Add($"Id duplicado: {id}."); continue; }
            if (string.IsNullOrWhiteSpace(p.Title)) errors.Add($"{id}: falta 'title'.");
            if (string.IsNullOrWhiteSpace(p.Rule)) errors.Add($"{id}: falta 'rule'.");
            if (!Enum.TryParse<Severity>(p.Severity, ignoreCase: true, out var severity))
            {
                errors.Add($"{id}: severidad inválida '{p.Severity}'.");
                continue;
            }

            policies.Add(new Policy
            {
                Id = id.ToUpperInvariant(),
                Title = (p.Title ?? "").Trim(),
                Category = (p.Category ?? "general").Trim().ToLowerInvariant(),
                Severity = severity,
                AppliesTo = (p.AppliesTo ?? new()).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList(),
                Rule = (p.Rule ?? "").Trim(),
                Remediation = (p.Remediation ?? "").Trim(),
                CompliantExample = string.IsNullOrWhiteSpace(p.CompliantExample) ? null : p.CompliantExample
            });
        }

        if (errors.Count > 0)
            throw new InvalidDataException("Catálogo de políticas inválido:\n- " + string.Join("\n- ", errors));

        return new PolicyCatalog(dto.Version!.Trim(), (dto.Description ?? "").Trim(), policies);
    }

    private sealed class CatalogDto
    {
        public string? Version { get; set; }
        public string? Description { get; set; }
        public List<PolicyDto>? Policies { get; set; }
    }

    private sealed class PolicyDto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Category { get; set; }
        public string? Severity { get; set; }
        public List<string>? AppliesTo { get; set; }
        public string? Rule { get; set; }
        public string? Remediation { get; set; }
        public string? CompliantExample { get; set; }
    }
}
