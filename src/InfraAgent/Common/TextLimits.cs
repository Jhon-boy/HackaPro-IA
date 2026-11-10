namespace InfraAgent.Common;

public static class TextLimits
{
    public const string TruncatedMarker = "\n[TRUNCADO]";

    /// <summary>Recorta a un máximo de caracteres e indica TRUNCADO.</summary>
    public static (string Text, bool Truncated) Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars) return (text, false);
        return (text[..Math.Max(0, maxChars)] + TruncatedMarker, true);
    }

    /// <summary>Recorta por bytes UTF-8 (para archivos).</summary>
    public static (string Text, bool Truncated) TruncateBytes(string text, int maxBytes)
    {
        var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
        if (bytes <= maxBytes) return (text, false);

        // Aproximación segura: recortar por caracteres hasta caber.
        var chars = Math.Min(text.Length, maxBytes);
        while (chars > 0 && System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, chars)) > maxBytes)
            chars -= Math.Max(1, chars / 20);
        return (text[..chars] + TruncatedMarker, true);
    }
}
