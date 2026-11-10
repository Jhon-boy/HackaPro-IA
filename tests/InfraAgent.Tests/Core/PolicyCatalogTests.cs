using InfraAgent.Core.Models;
using InfraAgent.Core.Policies;
using InfraAgent.Tests.Support;

namespace InfraAgent.Tests.Core;

public class PolicyCatalogTests
{
    [Fact]
    public void Repository_catalog_loads_and_is_consistent()
    {
        var catalog = PolicyCatalogLoader.LoadFromFile(RepoPaths.PolicyCatalog);

        Assert.NotEmpty(catalog.Version);
        Assert.True(catalog.Policies.Count >= 10);
        Assert.Equal(Severity.Critical, catalog.Get("SEC-004")!.Severity);
        Assert.True(catalog.Get("sec-001") is not null, "La búsqueda de ids no distingue mayúsculas.");
    }

    [Fact]
    public void ApplicableTo_returns_general_policies_plus_matching_types()
    {
        var catalog = PolicyCatalogLoader.LoadFromFile(RepoPaths.PolicyCatalog);

        var ids = catalog.ApplicableTo(new[] { "azurerm_key_vault" }).Select(p => p.Id).ToList();

        Assert.Contains("SEC-005", ids);      // específica de Key Vault
        Assert.Contains("SEC-004", ids);      // general
        Assert.DoesNotContain("SEC-006", ids); // SQL
    }

    [Fact]
    public void Render_includes_ids_severity_and_rule()
    {
        var catalog = PolicyCatalogLoader.LoadFromFile(RepoPaths.PolicyCatalog);
        var text = catalog.Render(new[] { catalog.Get("SEC-001")! }, includeExamples: true);

        Assert.Contains("SEC-001", text);
        Assert.Contains("[High]", text);
        Assert.Contains("public_network_access_enabled", text);
    }

    [Theory]
    [InlineData("version: '1'\npolicies:\n  - id: BAD\n    title: t\n    severity: High\n    rule: r")]
    [InlineData("version: '1'\npolicies:\n  - id: SEC-001\n    title: t\n    severity: Enorme\n    rule: r")]
    [InlineData("version: '1'\npolicies:\n  - id: SEC-001\n    title: t\n    severity: High\n    rule: r\n  - id: SEC-001\n    title: u\n    severity: Low\n    rule: r")]
    [InlineData("policies: []")]
    public void Invalid_catalogs_fail_with_clear_errors(string yaml) =>
        Assert.Throws<InvalidDataException>(() => PolicyCatalogLoader.LoadFromYaml(yaml));
}
