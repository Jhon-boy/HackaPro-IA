using System.Text.RegularExpressions;
using InfraAgent.Core.Models;

namespace InfraAgent.Core.Settings;

/// <summary>Validación de la configuración al arrancar. Nunca incluye valores, solo rutas de configuración.</summary>
public static partial class InfraAgentSettingsValidator
{
    /// <summary>Extensiones que se pueden permitir en archivos generados: solo texto declarativo, nunca ejecutables.</summary>
    public static readonly IReadOnlySet<string> SafeGeneratedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".tf", ".tfvars", ".hcl", ".md", ".txt", ".json", ".yaml", ".yml" };

    public static readonly IReadOnlySet<string> SupportedImageTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp", "image/gif" };

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{1,39}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex TagPattern();

    public static IReadOnlyList<string> Validate(InfraAgentSettings s)
    {
        var e = new List<string>();

        // --- Foundry ---
        if (!IsHttpsUrl(s.Foundry.Endpoint))
            e.Add("InfraAgent:Foundry:Endpoint debe ser una URL https válida.");

        foreach (var role in Enum.GetValues<AgentRole>())
        {
            if (string.IsNullOrWhiteSpace(s.Foundry.ResolveAgentId(role)))
                e.Add($"Falta el agente de Foundry para '{role}': defina InfraAgent:Foundry:Agents:{role} o InfraAgent:Foundry:DefaultAgentId.");
        }

        Range(e, "InfraAgent:Foundry:RunTimeoutSeconds", s.Foundry.RunTimeoutSeconds, 10, 900);
        Range(e, "InfraAgent:Foundry:PollIntervalMs", s.Foundry.PollIntervalMs, 100, 10_000);
        Range(e, "InfraAgent:Foundry:MaxToolRounds", s.Foundry.MaxToolRounds, 1, 30);

        // --- Repositorios ---
        if (s.Repositories.Provider == RepositoryProvider.AzureDevOps)
        {
            if (!IsHttpsUrl(s.AzureDevOps.OrganizationUrl))
                e.Add("InfraAgent:AzureDevOps:OrganizationUrl debe ser una URL https válida cuando Repositories:Provider = AzureDevOps.");
            Range(e, "InfraAgent:AzureDevOps:TimeoutSeconds", s.AzureDevOps.TimeoutSeconds, 5, 300);
        }

        // --- Análisis ---
        Range(e, "InfraAgent:Analysis:MaxFiles", s.Analysis.MaxFiles, 1, 200);
        Range(e, "InfraAgent:Analysis:MaxFileBytes", s.Analysis.MaxFileBytes, 1_000, 1_000_000);
        Range(e, "InfraAgent:Analysis:MaxTotalBytes", s.Analysis.MaxTotalBytes, 1_000, 5_000_000);
        Range(e, "InfraAgent:Analysis:MaxToolOutputChars", s.Analysis.MaxToolOutputChars, 1_000, 200_000);
        Range(e, "InfraAgent:Analysis:MaxTreeItems", s.Analysis.MaxTreeItems, 10, 2_000);
        Range(e, "InfraAgent:Analysis:MaxComponents", s.Analysis.MaxComponents, 5, 200);
        Range(e, "InfraAgent:Analysis:MaxFlows", s.Analysis.MaxFlows, 5, 500);
        Extensions(e, "InfraAgent:Analysis:FileExtensions", s.Analysis.ExtensionList);
        Extensions(e, "InfraAgent:Analysis:ReadableExtensions", s.Analysis.ReadableExtensionList);

        // --- Chat ---
        Range(e, "InfraAgent:Chat:TimeoutSeconds", s.Chat.TimeoutSeconds, 10, 225);   // límite de 230 s del balanceador HTTP
        Range(e, "InfraAgent:Chat:MaxMessageChars", s.Chat.MaxMessageChars, 100, 50_000);
        Range(e, "InfraAgent:Chat:MaxImages", s.Chat.MaxImages, 0, 10);
        Range(e, "InfraAgent:Chat:MaxImageBytes", s.Chat.MaxImageBytes, 10_000, 20_000_000);
        Range(e, "InfraAgent:Chat:MaxGeneratedFiles", s.Chat.MaxGeneratedFiles, 1, 100);
        Range(e, "InfraAgent:Chat:MaxGeneratedFileBytes", s.Chat.MaxGeneratedFileBytes, 1_000, 1_000_000);
        if (string.IsNullOrWhiteSpace(s.Chat.ResponseLanguage) || s.Chat.ResponseLanguage.Length > 40)
            e.Add("InfraAgent:Chat:ResponseLanguage es obligatorio (máx. 40 caracteres).");
        if (s.Chat.ImageTypeList.Count == 0 || s.Chat.ImageTypeList.Any(t => !SupportedImageTypes.Contains(t)))
            e.Add("InfraAgent:Chat:AllowedImageTypes solo admite image/png, image/jpeg, image/webp e image/gif separados por coma.");

        // --- Generación ---
        var g = s.Generation;
        Range(e, "InfraAgent:Generation:MaxFixRounds", g.MaxFixRounds, 0, 3);
        Range(e, "InfraAgent:Generation:MaxPoliciesInline", g.MaxPoliciesInline, 1, 200);
        if (string.IsNullOrWhiteSpace(g.DefaultRegion))
            e.Add("InfraAgent:Generation:DefaultRegion es obligatorio.");
        if (g.AllowedRegionList.Count > 0 && !g.AllowedRegionList.Contains(g.DefaultRegion, StringComparer.OrdinalIgnoreCase))
            e.Add("InfraAgent:Generation:DefaultRegion debe estar incluida en AllowedRegions.");
        if (string.IsNullOrWhiteSpace(g.NamingPattern) || g.NamingPattern.Length > 100)
            e.Add("InfraAgent:Generation:NamingPattern es obligatorio (máx. 100 caracteres).");
        if (g.RequiredTagList.Any(t => !TagPattern().IsMatch(t)))
            e.Add("InfraAgent:Generation:RequiredTags: use nombres en minúsculas, números y guion bajo, separados por coma.");
        if (g.AllowedFileExtensionList.Count == 0 || g.AllowedFileExtensionList.Any(x => !SafeGeneratedExtensions.Contains(x)))
            e.Add($"InfraAgent:Generation:AllowedFileExtensions solo admite: {string.Join(", ", SafeGeneratedExtensions.Order())}.");

        // --- Proyectos ---
        if (s.Projects.Count > 20)
            e.Add("InfraAgent:Projects admite como máximo 20 proyectos.");

        if (s.Repositories.Provider != RepositoryProvider.None)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < s.Projects.Count; i++)
            {
                var p = s.Projects[i];
                var prefix = $"InfraAgent:Projects:{i}";
                if (!IdPattern().IsMatch(p.Id ?? "")) e.Add($"{prefix}:Id inválido (minúsculas, números y guiones).");
                else if (!ids.Add(p.Id!)) e.Add($"{prefix}:Id duplicado.");

                if (s.Repositories.Provider == RepositoryProvider.Local)
                {
                    if (string.IsNullOrWhiteSpace(p.Path)) e.Add($"{prefix}:Path es obligatorio con Repositories:Provider = Local.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(p.Project)) e.Add($"{prefix}:Project es obligatorio.");
                    if (string.IsNullOrWhiteSpace(p.Repository)) e.Add($"{prefix}:Repository es obligatorio.");
                    if (string.IsNullOrWhiteSpace(p.Branch)) e.Add($"{prefix}:Branch es obligatorio.");
                }
            }
        }

        return e;
    }

    /// <summary>Valores de configuración que existen/no existen. Solo nombres, jamás valores.</summary>
    public static IReadOnlyDictionary<string, bool> Presence(InfraAgentSettings s) => new Dictionary<string, bool>
    {
        ["InfraAgent:Foundry:Endpoint"] = !string.IsNullOrWhiteSpace(s.Foundry.Endpoint),
        ["InfraAgent:Foundry:DefaultAgentId"] = !string.IsNullOrWhiteSpace(s.Foundry.DefaultAgentId),
        ["InfraAgent:AzureDevOps:OrganizationUrl"] = !string.IsNullOrWhiteSpace(s.AzureDevOps.OrganizationUrl),
        ["InfraAgent:AzureDevOps:Pat"] = !string.IsNullOrWhiteSpace(s.AzureDevOps.Pat),
        ["InfraAgent:Identity:ManagedIdentityClientId"] = !string.IsNullOrWhiteSpace(s.Identity.ManagedIdentityClientId),
        ["InfraAgent:Projects"] = s.Projects.Count > 0
    };

    private static void Extensions(List<string> errors, string name, IReadOnlyList<string> list)
    {
        if (list.Count == 0 || list.Any(x => !x.StartsWith('.')))
            errors.Add($"{name} debe ser una lista separada por comas de extensiones que empiecen con '.' (p. ej. .tf,.tfvars).");
    }

    private static void Range(List<string> errors, string name, int value, int min, int max)
    {
        if (value < min || value > max)
            errors.Add($"{name} debe estar entre {min} y {max}.");
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
