using InfraAgent.Models;
using InfraAgent.Policies;
using InfraAgent.Review;
using InfraAgent.Settings;
using InfraAgent.Terraform;
using InfraAgent.Tests.Support;

namespace InfraAgent.Tests.Core;

public class EvidenceVerifierTests
{
    private static readonly PolicyCatalog Catalog = PolicyCatalogLoader.LoadFromFile(RepoPaths.PolicyCatalog);

    private const string MainTf = """
        resource "azurerm_storage_account" "datos" {
          name                          = "stdatos"
          public_network_access_enabled = true
        }
        """;

    private static Dictionary<string, string> Files() => new() { ["main.tf"] = MainTf };

    private static Finding Make(string policyId, string file, string snippet, Severity severity = Severity.Low, int? line = 3) => new()
    {
        PolicyId = policyId,
        Title = "t",
        Severity = severity,
        Evidence = new Evidence { File = file, Snippet = snippet, StartLine = line }
    };

    [Fact]
    public void Valid_finding_is_accepted_and_severity_comes_from_the_catalog()
    {
        var output = new ReviewerOutput { Findings = { Make("sec-001", "/main.tf", "public_network_access_enabled   =   true", Severity.Info) } };

        var result = EvidenceVerifier.Verify(output, Files(), Catalog);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("SEC-001", finding.PolicyId);
        Assert.Equal(Severity.High, finding.Severity);   // el modelo dijo Info; manda el catálogo
        Assert.Equal("main.tf", finding.Evidence.File);
        Assert.Empty(result.Discarded);
    }

    [Theory]
    [InlineData("SEC-999", "main.tf", "public_network_access_enabled = true", "no existe en el catálogo")]
    [InlineData("SEC-001", "otro.tf", "public_network_access_enabled = true", "no fue parte del análisis")]
    [InlineData("SEC-001", "main.tf", "public_network_access_enabled = false", "no existe literalmente")]
    [InlineData("SEC-001", "main.tf", "  ", "no incluye un fragmento")]
    public void Unverifiable_findings_are_discarded_with_a_reason(string policy, string file, string snippet, string reason)
    {
        var output = new ReviewerOutput { Findings = { Make(policy, file, snippet) } };

        var result = EvidenceVerifier.Verify(output, Files(), Catalog);

        Assert.Empty(result.Findings);
        var discarded = Assert.Single(result.Discarded);
        Assert.Contains(reason, discarded.Reason);
    }

    [Fact]
    public void Line_outside_the_file_is_rejected_and_duplicates_are_merged()
    {
        var output = new ReviewerOutput
        {
            Findings =
            {
                Make("SEC-001", "main.tf", "public_network_access_enabled = true", line: 999),
                Make("SEC-001", "main.tf", "public_network_access_enabled = true", line: 3),
                Make("SEC-001", "main.tf", "public_network_access_enabled = true", line: 3)
            }
        };

        var result = EvidenceVerifier.Verify(output, Files(), Catalog);

        Assert.Single(result.Findings);
        Assert.Single(result.Discarded);
    }

    [Fact]
    public void Compliance_items_for_unknown_policies_are_dropped()
    {
        var output = new ReviewerOutput
        {
            Compliance =
            {
                new ComplianceItem { PolicyId = "SEC-001", Status = ComplianceStatus.NoCumple },
                new ComplianceItem { PolicyId = "XXX-123", Status = ComplianceStatus.Cumple }
            }
        };

        var result = EvidenceVerifier.Verify(output, Files(), Catalog);

        Assert.Equal("SEC-001", Assert.Single(result.Compliance).PolicyId);
    }

    [Fact]
    public void TerraformInspector_extracts_resource_types_and_modules()
    {
        const string hcl = """
            resource "azurerm_key_vault" "kv" {
            }
            resource "azurerm_key_vault" "kv2" {
            }
            module "net" {
              source = "./net"
            }
            """;

        Assert.Equal(new[] { "azurerm_key_vault" }, TerraformInspector.ExtractResourceTypes(hcl));
        Assert.Equal(new[] { "net" }, TerraformInspector.ExtractModuleNames(hcl));
        Assert.True(TerraformInspector.IsTerraformPath("a/b/main.TF", new[] { ".tf" }));
        Assert.False(TerraformInspector.IsTerraformPath("a/readme.md", new[] { ".tf" }));
    }
}
