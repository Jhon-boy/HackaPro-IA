using System.Text;
using System.Text.RegularExpressions;
using InfraAgent.Core.Common;
using InfraAgent.Core.Security;
using InfraAgent.Core.Settings;

namespace InfraAgent.Application.Review;

/// <summary>
/// Conjunto de archivos que se envía al modelo: enmascarado y acotado (cantidad, tamaño por archivo y total).
/// El contenido guardado es el MISMO que ve el modelo; contra él se verifica la evidencia.
/// </summary>
public sealed class FileBundle
{
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();
    public bool Truncated { get; init; }
    public IReadOnlyList<string> Omitted { get; init; } = Array.Empty<string>();

    public static FileBundle Empty { get; } = new();

    public static FileBundle Build(IEnumerable<(string Path, string Content)> source, AnalysisSettings limits)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var omitted = new List<string>();
        var truncated = false;
        var total = 0;

        foreach (var (path, content) in source)
        {
            if (files.Count >= limits.MaxFiles) { omitted.Add(path); truncated = true; continue; }

            var masked = SecretMasker.MaskText(content);
            var (limited, fileTruncated) = TextLimits.TruncateBytes(masked, limits.MaxFileBytes);
            var size = Encoding.UTF8.GetByteCount(limited);

            if (total + size > limits.MaxTotalBytes) { omitted.Add(path); truncated = true; continue; }

            total += size;
            truncated |= fileTruncated;
            files[path.Trim().Replace('\\', '/').TrimStart('/')] = limited;
        }

        return new FileBundle { Files = files, Truncated = truncated, Omitted = omitted };
    }

    /// <summary>Texto para el prompt, con numeración de líneas (el número no forma parte del código).</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var (path, content) in Files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"### {path}");
            sb.AppendLine("```hcl");
            sb.AppendLine(Numbered(content));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (Omitted.Count > 0)
            sb.AppendLine($"[TRUNCADO] Archivos omitidos por límites de análisis: {string.Join(", ", Omitted.Take(20))}");
        return sb.ToString();
    }

    private static string Numbered(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var width = lines.Length.ToString().Length;
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
            sb.Append((i + 1).ToString().PadLeft(width)).Append(" | ").AppendLine(lines[i]);
        return sb.ToString().TrimEnd();
    }

    private static readonly Regex LineNumberPrefix = new(@"^\s*\d+\s\|\s?", RegexOptions.Compiled);

    /// <summary>Quita prefijos de numeración que el modelo pudo copiar dentro de un fragmento citado.</summary>
    public static string StripLineNumbers(string snippet) =>
        string.Join('\n', snippet.Replace("\r\n", "\n").Split('\n').Select(l => LineNumberPrefix.Replace(l, "")));
}
