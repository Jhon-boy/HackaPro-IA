using InfraAgent.Abstractions;
using InfraAgent.Knowledge;
using InfraAgent.Tools;
using InfraAgent.Models;
using InfraAgent.Tests.Support;

namespace InfraAgent.Tests.Application;

public class KnowledgeAndToolsTests
{
    [Fact]
    public void Every_agent_has_instructions_and_skills_load_with_matching_names()
    {
        var catalog = AgentCatalog.Load(RepoPaths.Agents);

        foreach (var role in Enum.GetValues<AgentRole>())
            Assert.False(string.IsNullOrWhiteSpace(catalog.Get(role).Instructions), $"Faltan instrucciones de {role}");

        Assert.Contains(catalog.Get(AgentRole.Security).Skills, s => s.Name == "revisar-terraform-azure" && s.References.ContainsKey("checklist-seguridad.md"));
        Assert.Contains(catalog.Get(AgentRole.IacGenerator).Skills, s => s.Name == "patrones-seguros-azure");
        Assert.Contains("***MASKED***", catalog.SharedRules);
        Assert.Equal(10, catalog.Version.Length);
    }

    [Fact]
    public void Instructions_include_shared_rules_skill_index_and_catalog_version()
    {
        var host = new TestHost().Build();

        var text = host.Get<PromptBuilder>().BuildInstructions(AgentRole.Security);

        Assert.Contains("Reglas fijas de InfraAgent", text);
        Assert.Contains("`revisar-terraform-azure`", text);
        Assert.Contains("v0.1-ejemplo", text);
    }

    [Fact]
    public void Skill_whose_name_does_not_match_its_folder_is_rejected()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agents-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(RepoPaths.Agents, dir);
            File.WriteAllText(Path.Combine(dir, "seguridad", "skills", "revisar-terraform-azure", "SKILL.md"),
                "---\nname: otro-nombre\ndescription: x\n---\ncuerpo");

            Assert.Throws<InvalidDataException>(() => AgentCatalog.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ToolAllowList_is_closed_and_read_only_by_role()
    {
        Assert.True(ToolAllowList.IsAllowed(AgentRole.Security, ToolNames.GetFile));
        Assert.False(ToolAllowList.IsAllowed(AgentRole.IacGenerator, ToolNames.GetFile));
        Assert.False(ToolAllowList.IsAllowed(AgentRole.Orchestrator, ToolNames.GetFile));
        Assert.False(ToolAllowList.IsAllowed(AgentRole.Security, "run_command"));
        Assert.False(ToolAllowList.IsAllowed(AgentRole.Architecture, ToolNames.GenerateTerraform));

        // Ninguna herramienta publicada puede ser de ejecución: solo nombres conocidos de lectura/consulta/orquestación.
        var known = typeof(ToolNames).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet();
        foreach (var role in Enum.GetValues<AgentRole>())
            Assert.All(ToolAllowList.For(role), name => Assert.Contains(name, known));
    }

    [Fact]
    public async Task Invoker_blocks_tools_not_allowed_for_the_role_and_audits_it()
    {
        var host = new TestHost().Build();
        var invoker = host.Get<IToolInvoker>();

        var result = await invoker.InvokeAsync(AgentRole.IacGenerator, ToolNames.GetFile, """{"path":"main.tf"}""", NewContext(), default);

        Assert.StartsWith("ERROR: herramienta no permitida", result);
        Assert.Contains(host.Audit.Entries, e => e.Kind == InfraAgent.Auditing.AuditKind.Security && !e.Success);
    }

    [Fact]
    public async Task Invoker_returns_validation_errors_to_the_model_instead_of_throwing()
    {
        var host = new TestHost().Build();
        host.Repositories.Files["main.tf"] = "x";
        var ctx = NewContext(new FakeRepositoryReader(host.Repositories.Files));

        var result = await host.Get<IToolInvoker>().InvokeAsync(AgentRole.Security, ToolNames.GetFile, """{"path":"../../etc/passwd"}""", ctx, default);

        Assert.StartsWith("ERROR de validación", result);
    }

    [Fact]
    public async Task Invoker_masks_secrets_in_tool_output()
    {
        var host = new TestHost().Build();
        var ctx = NewContext(new FakeRepositoryReader(new() { ["vars.tf"] = "admin_password = \"Valor-Real-123\"" }));

        var result = await host.Get<IToolInvoker>().InvokeAsync(AgentRole.Security, ToolNames.GetFile, """{"path":"vars.tf"}""", ctx, default);

        Assert.DoesNotContain("Valor-Real-123", result);
        Assert.Contains("***MASKED***", result);
    }

    [Fact]
    public async Task Invoker_truncates_large_output_and_flags_it()
    {
        var host = new TestHost().Build(s => s.Analysis.MaxToolOutputChars = 1_000);
        var ctx = NewContext(new FakeRepositoryReader(new() { ["big.tf"] = new string('a', 5_000) }));

        var result = await host.Get<IToolInvoker>().InvokeAsync(AgentRole.Security, ToolNames.GetFile, """{"path":"big.tf"}""", ctx, default);

        Assert.EndsWith("[TRUNCADO]", result);
        Assert.True(result.Length < 1_100);
    }

    [Fact]
    public async Task LoadSkill_is_scoped_to_the_calling_agent_and_reads_references()
    {
        var host = new TestHost().Build();
        var invoker = host.Get<IToolInvoker>();

        var own = await invoker.InvokeAsync(AgentRole.Security, ToolNames.LoadSkill, """{"name":"revisar-terraform-azure"}""", NewContext(), default);
        Assert.Contains("Checklist de revisión", own);

        var reference = await invoker.InvokeAsync(AgentRole.Security, ToolNames.LoadSkill,
            """{"name":"revisar-terraform-azure","resource":"checklist-seguridad.md"}""", NewContext(), default);
        Assert.Contains("azurerm_storage_account", reference);

        var foreign = await invoker.InvokeAsync(AgentRole.Security, ToolNames.LoadSkill, """{"name":"patrones-seguros-azure"}""", NewContext(), default);
        Assert.StartsWith("ERROR de validación", foreign);
    }

    [Fact]
    public async Task GetPolicies_filters_by_ids_category_and_resource_type()
    {
        var host = new TestHost().Build();
        var invoker = host.Get<IToolInvoker>();

        var byId = await invoker.InvokeAsync(AgentRole.Policy, ToolNames.GetPolicies, """{"ids":["SEC-001"]}""", NewContext(), default);
        Assert.Contains("SEC-001", byId);
        Assert.DoesNotContain("SEC-002", byId);

        var byType = await invoker.InvokeAsync(AgentRole.Policy, ToolNames.GetPolicies, """{"tipoRecurso":"azurerm_mssql_server"}""", NewContext(), default);
        Assert.Contains("SEC-006", byType);

        var invalid = await invoker.InvokeAsync(AgentRole.Policy, ToolNames.GetPolicies, """{"ids":["no-valido"]}""", NewContext(), default);
        Assert.StartsWith("ERROR de validación", invalid);
    }

    [Fact]
    public async Task Repository_tools_report_when_there_is_no_repository()
    {
        var host = new TestHost().Build();
        var result = await host.Get<IToolInvoker>().InvokeAsync(AgentRole.Architecture, ToolNames.GetRepoTree, "{}", NewContext(), default);
        Assert.Contains("No hay un repositorio", result);
    }

    [Fact]
    public void Published_specs_match_the_allow_list()
    {
        var host = new TestHost().Build();
        var specs = host.Get<IToolInvoker>().GetSpecs(AgentRole.IacGenerator).Select(s => s.Name).ToList();

        Assert.Equal(new[] { ToolNames.GetPolicies, ToolNames.LoadSkill }.OrderBy(x => x), specs.OrderBy(x => x));
        Assert.All(host.Get<IToolInvoker>().GetSpecs(AgentRole.Orchestrator),
            s => System.Text.Json.JsonDocument.Parse(s.ParametersJsonSchema).Dispose());
    }

    private static ToolContext NewContext(IRepositoryReader? repo = null) => new() { CorrelationId = "test", Repository = repo };

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
