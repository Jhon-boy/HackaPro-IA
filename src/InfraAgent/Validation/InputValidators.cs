using System.Text.RegularExpressions;
using InfraAgent.Settings;

namespace InfraAgent.Validation;

/// <summary>
/// Validación de TODO parámetro que viene del usuario o del modelo.
/// Lanza <see cref="ArgumentException"/> con mensajes en español (el modelo los ve y puede corregir).
/// </summary>
public static partial class InputValidators
{
    /// <summary>Extensiones por defecto de los archivos que genera el modelo.</summary>
    public static readonly IReadOnlyList<string> DefaultGeneratedExtensions = new[] { ".tf", ".tfvars", ".md" };

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{1,39}$")]
    private static partial Regex ProjectIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{5,128}$")]
    private static partial Regex ConversationIdPattern();

    [GeneratedRegex(@"^[A-Z]{2,6}-\d{3}$", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}$")]
    private static partial Regex SkillNamePattern();

    public static string ProjectId(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (!ProjectIdPattern().IsMatch(v))
            throw new ArgumentException("projectId inválido: use minúsculas, números y guiones (2-40 caracteres).");
        return v;
    }

    public static string ConversationId(string? value)
    {
        var v = (value ?? "").Trim();
        if (!ConversationIdPattern().IsMatch(v))
            throw new ArgumentException("conversationId inválido.");
        return v;
    }

    public static string PolicyId(string? value)
    {
        var v = (value ?? "").Trim().ToUpperInvariant();
        if (!PolicyIdPattern().IsMatch(v))
            throw new ArgumentException("Id de política inválido (formato esperado: SEC-001).");
        return v;
    }

    public static string SkillName(string? value)
    {
        var v = (value ?? "").Trim();
        if (!SkillNamePattern().IsMatch(v))
            throw new ArgumentException("Nombre de skill inválido.");
        return v;
    }

    public static string Message(string? value, int maxChars)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) throw new ArgumentException("El mensaje no puede estar vacío.");
        if (v.Length > maxChars) throw new ArgumentException($"El mensaje supera el máximo de {maxChars} caracteres.");
        return v;
    }

    /// <summary>
    /// Ruta relativa de repositorio o de archivo generado. Rechaza rutas absolutas, '..', unidades,
    /// caracteres de control y segmentos vacíos. Devuelve la ruta normalizada con '/'.
    /// </summary>
    public static string RelativePath(string? value, string field = "path")
    {
        var v = (value ?? "").Trim().Replace('\\', '/');
        if (v.Length == 0 || v.Length > 260) throw new ArgumentException($"{field} inválido.");
        if (v.Any(char.IsControl)) throw new ArgumentException($"{field} contiene caracteres no permitidos.");
        if (v.Contains(':')) throw new ArgumentException($"{field} no puede contener ':'.");

        var trimmed = v.TrimStart('/');
        var segments = trimmed.Split('/', StringSplitOptions.None);
        if (segments.Any(s => s.Length == 0 || s == "." || s == ".."))
            throw new ArgumentException($"{field} no puede contener segmentos vacíos, '.' ni '..'.");
        return trimmed;
    }

    /// <summary>Ruta de un archivo generado por el modelo: relativa y con una extensión permitida (configurable).</summary>
    public static string GeneratedFilePath(string? value, IEnumerable<string>? allowedExtensions = null)
    {
        var path = RelativePath(value, "ruta del archivo generado");
        var allowed = (allowedExtensions ?? DefaultGeneratedExtensions).ToList();
        if (!allowed.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Solo se permiten archivos {string.Join(", ", allowed)}.");
        return path;
    }

    /// <summary>¿La ruta tiene una de las extensiones indicadas?</summary>
    public static bool HasExtension(string path, IEnumerable<string> extensions) =>
        extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>Valida tipo declarado (configurable), tamaño y firma binaria (magic bytes) de una imagen.</summary>
    public static string Image(string? mimeType, byte[] data, ChatSettings settings)
    {
        var mime = (mimeType ?? "").Trim().ToLowerInvariant();
        if (!settings.ImageTypeList.Contains(mime))
            throw new ArgumentException($"Solo se aceptan imágenes de tipo: {string.Join(", ", settings.ImageTypeList)}.");
        if (data.Length == 0) throw new ArgumentException("La imagen está vacía.");
        if (data.Length > settings.MaxImageBytes)
            throw new ArgumentException($"La imagen supera el máximo de {settings.MaxImageBytes / 1024} KB.");

        if (!MatchesSignature(mime, data))
            throw new ArgumentException("El contenido de la imagen no coincide con su tipo declarado.");
        return mime;
    }

    private static bool MatchesSignature(string mime, byte[] d) => mime switch
    {
        "image/png" => d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47,
        "image/jpeg" => d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF,
        "image/gif" => d.Length > 6 && d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38,
        "image/webp" => d.Length > 12 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46
                        && d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50,
        _ => false
    };
}
