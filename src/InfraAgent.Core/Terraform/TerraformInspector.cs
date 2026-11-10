using System.Text.RegularExpressions;

namespace InfraAgent.Core.Terraform;

/// <summary>Inspección determinista y ligera de Terraform (sin parser HCL completo).</summary>
public static partial class TerraformInspector
{
    [GeneratedRegex("""^\s*resource\s+"(?<type>[A-Za-z0-9_]+)"\s+"(?<name>[A-Za-z0-9_\-]+)"\s*\{""", RegexOptions.Multiline)]
    private static partial Regex ResourceRegex();

    [GeneratedRegex("""^\s*module\s+"(?<name>[A-Za-z0-9_\-]+)"\s*\{""", RegexOptions.Multiline)]
    private static partial Regex ModuleRegex();

    public static IReadOnlyList<string> ExtractResourceTypes(string hcl) =>
        ResourceRegex().Matches(hcl).Select(m => m.Groups["type"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<string> ExtractResourceTypes(IEnumerable<string> files) =>
        files.SelectMany(ExtractResourceTypes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<string> ExtractModuleNames(string hcl) =>
        ModuleRegex().Matches(hcl).Select(m => m.Groups["name"].Value).Distinct().ToList();

    public static bool IsTerraformPath(string path, IEnumerable<string> allowedExtensions)
    {
        var p = path.Trim();
        return allowedExtensions.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}
