using InfraAgent.Security;

namespace InfraAgent.Tests.Core;

public class SecretMaskerTests
{
    [Fact]
    public void Masks_literal_password_but_keeps_variable_references()
    {
        const string hcl = """
            admin_password = "SuperSecreta123!"
            other_password = var.db_password
            """;

        var masked = SecretMasker.MaskText(hcl);

        Assert.DoesNotContain("SuperSecreta123!", masked);
        Assert.Contains("admin_password = \"***MASKED***\"", masked);
        Assert.Contains("other_password = var.db_password", masked);
    }

    [Theory]
    [InlineData("secret_name = \"kv-secret-sql\"")]
    [InlineData("key_vault_secret_id = \"/subscriptions/x\"")]
    [InlineData("token_validity_days = \"30\"")]
    public void Does_not_mask_names_ids_or_settings_of_sensitive_words(string line)
    {
        Assert.Equal(line, SecretMasker.MaskText(line));
    }

    [Fact]
    public void Masks_private_keys_connection_strings_and_tokens()
    {
        var pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIEabc123\n-----END RSA PRIVATE KEY-----";
        Assert.DoesNotContain("MIIEabc123", SecretMasker.MaskText(pem));

        var conn = "Server=tcp:x;Database=d;User Id=u;Password=Abc12345;";
        Assert.DoesNotContain("Abc12345", SecretMasker.MaskText(conn));

        var key = "DefaultEndpointsProtocol=https;AccountName=a;AccountKey=abcdefghijklmnopqrstuvwxyz0123456789ABCD==;";
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123456789ABCD", SecretMasker.MaskText(key));

        Assert.DoesNotContain("ghp_", SecretMasker.MaskText("token ghp_abcdefghijklmnopqrstuvwxyz0123456789"));
    }

    [Fact]
    public void Masking_is_idempotent()
    {
        var once = SecretMasker.MaskText("password = \"abcdef\"");
        Assert.Equal(once, SecretMasker.MaskText(once));
    }

    [Fact]
    public void DetectSecrets_reports_rule_names_never_values()
    {
        var found = SecretMasker.DetectSecrets("client_secret = \"valorultrasecreto\"");
        Assert.Contains("asignacion-sensible-literal", found);
        Assert.DoesNotContain(found, f => f.Contains("valorultrasecreto"));
        Assert.Empty(SecretMasker.DetectSecrets("client_secret = var.client_secret"));
    }
}
