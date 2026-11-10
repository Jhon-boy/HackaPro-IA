using System.Text.RegularExpressions;
using InfraAgent.Core.Models;
using InfraAgent.Core.Policies;

namespace InfraAgent.Core.Review;

/// <summary>
/// Verifica por código cada hallazgo propuesto por el modelo: la política debe existir en el catálogo,
/// el archivo debe haber sido analizado y el fragmento citado debe existir literalmente en él.
/// La severidad de una política del catálogo la fija el catálogo (el modelo no puede rebajarla).
/// </summary>
public static partial class EvidenceVerifier
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static VerifiedReview Verify(
        ReviewerOutput output,
        IReadOnlyDictionary<string, string> filesByPath,
        PolicyCatalog catalog)
    {
        var normalizedFiles = filesByPath.ToDictionary(
            kv => NormalizePath(kv.Key),
            kv => kv.Value,
            StringComparer.OrdinalIgnoreCase);

        var result = new VerifiedReview
        {
            Summary = output.Summary,
            Compliance = output.Compliance
                .Where(c => catalog.Exists(c.PolicyId))
                .Select(c => { c.PolicyId = catalog.Get(c.PolicyId)!.Id; return c; })
                .ToList(),
            AnalyzedFiles = filesByPath.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in output.Findings)
        {
            var reason = Validate(finding, normalizedFiles, catalog);
            if (reason is not null)
            {
                result.Discarded.Add(new DiscardedFinding
                {
                    PolicyId = finding.PolicyId,
                    Title = finding.Title,
                    Reason = reason
                });
                continue;
            }

            var policy = catalog.Get(finding.PolicyId)!;
            finding.PolicyId = policy.Id;
            finding.Severity = policy.Severity;   // el catálogo manda
            finding.Evidence.File = NormalizePath(finding.Evidence.File);

            // Deduplicar por política + archivo + fragmento.
            var key = $"{finding.PolicyId}|{finding.Evidence.File}|{Collapse(finding.Evidence.Snippet)}";
            if (seen.Add(key))
                result.Findings.Add(finding);
        }

        result.Findings = result.Findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.PolicyId, StringComparer.Ordinal)
            .ToList();
        return result;
    }

    private static string? Validate(Finding finding, Dictionary<string, string> files, PolicyCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(finding.PolicyId) || !catalog.Exists(finding.PolicyId))
            return $"La política '{finding.PolicyId}' no existe en el catálogo.";

        if (string.IsNullOrWhiteSpace(finding.Evidence?.File))
            return "El hallazgo no indica el archivo de evidencia.";

        if (!files.TryGetValue(NormalizePath(finding.Evidence.File), out var content))
            return $"El archivo '{finding.Evidence.File}' no fue parte del análisis.";

        var snippet = Collapse(finding.Evidence.Snippet);
        if (snippet.Length < 3)
            return "El hallazgo no incluye un fragmento de evidencia.";

        if (!Collapse(content).Contains(snippet, StringComparison.Ordinal))
            return "El fragmento citado no existe literalmente en el archivo.";

        if (finding.Evidence.StartLine is { } start)
        {
            var lineCount = content.Split('\n').Length;
            if (start < 1 || start > lineCount)
                return "El número de línea está fuera del archivo.";
        }

        return null;
    }

    public static string NormalizePath(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

    private static string Collapse(string text) => Whitespace().Replace(text ?? "", " ").Trim();
}
