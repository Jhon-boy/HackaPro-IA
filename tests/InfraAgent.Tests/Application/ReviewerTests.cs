using InfraAgent.Abstractions;
using InfraAgent.Agents;
using InfraAgent.Review;
using InfraAgent.Models;
using InfraAgent.Settings;
using InfraAgent.Tests.Support;

namespace InfraAgent.Tests.Application;

public class ReviewerTests
{
    private const string MainTf = "resource \"azurerm_storage_account\" \"datos\" {\n  name = \"stdatos\"\n  public_network_access_enabled = true\n}\n";

    private static ToolContext Ctx() => new() { CorrelationId = "t" };

    [Fact]
    public async Task Json_agent_output_is_repaired_once_and_ephemeral_thread_is_deleted()
    {
        var host = new TestHost().Build();
        host.Runner.Script(AgentRole.Architecture, "Aquí está el análisis (sin JSON)", Json.Architecture());

        var model = await host.Get<AgentService>().RunJsonAsync<ArchitectureModel>(AgentRole.Architecture, "analiza", Ctx());

        Assert.Equal("App con storage", model.Summary);
        var second = host.Runner.For(AgentRole.Architecture).Last();
        Assert.Contains("no es un JSON válido", second.UserMessage);
        Assert.NotNull(second.ThreadId);                       // la reparación reutiliza el hilo
        Assert.Single(host.Runner.DeletedThreads);             // y al final se elimina
    }

    [Fact]
    public async Task Json_agent_failing_twice_raises_a_clear_error()
    {
        var host = new TestHost().Build();
        host.Runner.Script(AgentRole.Architecture, "no json", "sigue sin json");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Get<AgentService>().RunJsonAsync<ArchitectureModel>(AgentRole.Architecture, "analiza", Ctx()));

        Assert.Contains("no devolvió JSON válido", ex.Message);
        Assert.Single(host.Runner.DeletedThreads);
    }

    [Fact]
    public async Task Security_review_keeps_verified_findings_and_discards_invented_ones()
    {
        var host = new TestHost().Build();
        host.Runner.Script(AgentRole.Security, Json.Review("Revisado",
            Json.Finding("SEC-001", "main.tf", "public_network_access_enabled = true", line: 3),
            Json.Finding("SEC-001", "main.tf", "esto no existe en el archivo"),
            Json.Finding("ZZZ-001", "main.tf", "name = ")));
        var bundle = FileBundle.Build(new[] { ("main.tf", MainTf) }, new AnalysisSettings());

        var review = await host.Get<ReviewerService>().ReviewSecurityAsync(bundle, null, Ctx(), default);

        Assert.Single(review.Findings);
        Assert.Equal(2, review.Discarded.Count);
        Assert.Equal(Severity.High, review.Findings[0].Severity);

        // El prompt incluye solo políticas de seguridad aplicables y el archivo numerado.
        var prompt = host.Runner.For(AgentRole.Security).Single().UserMessage;
        Assert.Contains("SEC-001", prompt);
        Assert.DoesNotContain("TAG-001", prompt);
        Assert.Contains("3 |   public_network_access_enabled = true", prompt);
    }

    [Fact]
    public async Task Line_numbers_copied_into_snippets_are_stripped_before_verification()
    {
        var host = new TestHost().Build();
        host.Runner.Script(AgentRole.Security, Json.Review("ok",
            Json.Finding("SEC-001", "main.tf", "3 |   public_network_access_enabled = true", line: 3)));
        var bundle = FileBundle.Build(new[] { ("main.tf", MainTf) }, new AnalysisSettings());

        var review = await host.Get<ReviewerService>().ReviewSecurityAsync(bundle, null, Ctx(), default);

        Assert.Single(review.Findings);
    }

    [Fact]
    public async Task Secrets_are_masked_before_reaching_the_model_and_evidence_is_checked_against_what_it_saw()
    {
        var host = new TestHost().Build();
        const string tf = "resource \"azurerm_mssql_server\" \"sql\" {\n  administrator_login_password = \"Clave-Real-9876\"\n}\n";
        host.Runner.Script(AgentRole.Security, Json.Review("ok",
            Json.Finding("SEC-004", "main.tf", "administrator_login_password = ", line: 2)));
        var bundle = FileBundle.Build(new[] { ("main.tf", tf) }, new AnalysisSettings());

        var review = await host.Get<ReviewerService>().ReviewSecurityAsync(bundle, null, Ctx(), default);

        Assert.DoesNotContain("Clave-Real-9876", host.Runner.Requests.Single().UserMessage);
        Assert.Single(review.Findings);
        Assert.DoesNotContain("Clave-Real-9876", System.Text.Json.JsonSerializer.Serialize(review));
    }

    [Fact]
    public void FileBundle_applies_count_size_and_total_limits_and_flags_truncation()
    {
        var limits = new AnalysisSettings { MaxFiles = 2, MaxFileBytes = 1_000, MaxTotalBytes = 2_500 };
        var sources = new[]
        {
            ("a.tf", new string('a', 900)),
            ("b.tf", new string('b', 2_000)),   // se recorta a 1000 bytes
            ("c.tf", "x")                       // excede MaxFiles
        };

        var bundle = FileBundle.Build(sources, limits);

        Assert.True(bundle.Truncated);
        Assert.Equal(new[] { "c.tf" }, bundle.Omitted);
        Assert.Contains("[TRUNCADO]", bundle.Files["b.tf"]);

        // El límite total también omite archivos y lo informa.
        var tight = FileBundle.Build(sources, new AnalysisSettings { MaxFiles = 5, MaxFileBytes = 1_000, MaxTotalBytes = 1_500 });
        Assert.True(tight.Truncated);
        Assert.Contains("b.tf", tight.Omitted);
    }
}
