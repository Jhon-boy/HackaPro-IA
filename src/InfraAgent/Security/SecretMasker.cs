using System.Text.RegularExpressions;

namespace InfraAgent.Security;

/// <summary>
/// Enmascara secretos antes de enviar contenido al modelo y antes de registrar/devolver salidas.
/// Los valores literales de secretos nunca deben salir de este proceso.
/// </summary>
public static partial class SecretMasker
{
    public const string Mask = "***MASKED***";

    // Asignaciones literales a atributos "sensibles": password = "valor", secret: 'valor', etc.
    // No enmascara referencias (var.x, local.x, data.x) porque no van entre comillas.
    [GeneratedRegex("""(?im)(\b[\w\.\-]*(?:password|passwd|pwd|secret|token|api_?key|access_?key|primary_?key|secondary_?key|client_?secret|sas_?token|shared_?access_?key|connection_?string)(?![\w\.\-]*?(?:_name|_id|_ids|_uri|_url|_enabled|_days|_permissions|_version|_type)\b)[\w\.\-]*\s*[=:]\s*)(["'])(?!\$\{)(?!\s*\2)([^"'\r\n]{3,})\2""")]
    private static partial Regex SensitiveAssignment();

    [GeneratedRegex("""-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----""")]
    private static partial Regex PrivateKey();

    [GeneratedRegex("""(?i)(AccountKey|SharedAccessKey)\s*=\s*[A-Za-z0-9+/=]{16,}""")]
    private static partial Regex ConnectionStringPart();

    [GeneratedRegex("""(?i)(?<=[;\s"'])(Password|Pwd)=[^;"'\s]{3,}""")]
    private static partial Regex ConnectionStringPassword();

    [GeneratedRegex("""\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\b""")]
    private static partial Regex Jwt();

    [GeneratedRegex("""(?i)\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9]{32,})\b""")]
    private static partial Regex KnownTokens();

    [GeneratedRegex("""(?i)(sig|sv|se)=[A-Za-z0-9%+/=]{20,}""")]
    private static partial Regex SasSignature();

    public static string MaskText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var result = PrivateKey().Replace(text, Mask);
        result = SensitiveAssignment().Replace(result, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Mask}{m.Groups[2].Value}");
        result = ConnectionStringPart().Replace(result, m => $"{m.Groups[1].Value}={Mask}");
        result = ConnectionStringPassword().Replace(result, m => $"{m.Groups[1].Value}={Mask}");
        result = Jwt().Replace(result, Mask);
        result = KnownTokens().Replace(result, Mask);
        result = SasSignature().Replace(result, m => $"{m.Groups[1].Value}={Mask}");
        return result;
    }

    /// <summary>Nombres de las reglas que detectan un secreto literal (sin devolver el valor).</summary>
    public static IReadOnlyList<string> DetectSecrets(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var found = new List<string>();
        if (PrivateKey().IsMatch(text)) found.Add("clave-privada");
        if (SensitiveAssignment().IsMatch(text)) found.Add("asignacion-sensible-literal");
        if (ConnectionStringPart().IsMatch(text)) found.Add("cadena-de-conexion");
        if (ConnectionStringPassword().IsMatch(text)) found.Add("password-en-cadena");
        if (Jwt().IsMatch(text)) found.Add("jwt");
        if (KnownTokens().IsMatch(text)) found.Add("token-conocido");
        if (SasSignature().IsMatch(text)) found.Add("firma-sas");
        return found;
    }
}
