using InfraAgent.Application.Abstractions;
using InfraAgent.Application.Chat;
using InfraAgent.Application.Knowledge;
using InfraAgent.Application.Projects;
using InfraAgent.Application.Review;
using InfraAgent.Application.Tools;
using InfraAgent.Core.Models;
using InfraAgent.Core.Settings;
using InfraAgent.Tests.Support;

namespace InfraAgent.Tests.Application;

/// <summary>Verifica que el comportamiento cambie solo con configuración, sin tocar código.</summary>
public class ConfigurabilityTests
{
    private const string GoodTf = "resource \"azurerm_storage_account\" \"datos\" {\n  public_network_access_enabled = false\n}";
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };
    private static readonly byte[] Webp = { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0, 0 };

    private static ToolContext Ctx(IRepositoryReader? repo = null) => new() { CorrelationId = "t", Repository = repo };

    // ---------------- Interruptores de capacidades ----------------

    [Fact]
    public void Disabled_features_remove_tools_from_what_the_model_can_see()
    {
        var host = new TestHost().Build(s =>
        {
            s.Features.TerraformGeneration = false;
            s.Features.RepositoryAccess = false;
        });

        var orchestrator = host.Get<IToolInvoker>().GetSpecs(AgentRole.Orchestrator).Select(t => t.Name).ToList();
        var architecture = host.Get<IToolInvoker>().GetSpecs(AgentRole.Architecture).Select(t => t.Name).ToList();

        Assert.DoesNotContain(ToolNames.GenerateTerraform, orchestrator);
        Assert.Contains(ToolNames.ReviewSecurity, orchestrator);
        Assert.DoesNotContain(ToolNames.GetFile, architecture);
        Assert.DoesNotContain(ToolNames.GetRepoTree, architecture);
        Assert.Contains(ToolNames.LoadSkill, architecture);
    }

    [Fact]
    public async Task A_disabled_tool_cannot_be_invoked_even_if_the_model_asks_for_it()
    {
        var host = new TestHost().Build(s => s.Features.SecurityReview = false);

        var result = await host.Get<IToolInvoker>().InvokeAsync(AgentRole.Orchestrator, ToolNames.ReviewSecurity, "{}", Ctx(), default);

        Assert.Contains("deshabilitada", result);
        Assert.Contains(host.Audit.Entries, e => e.Kind == InfraAgent.Core.Auditing.AuditKind.Security && !e.Success);
    }

    [Fact]
    public async Task Disabling_images_or_repository_access_rejects_requests_before_calling_agents()
    {
        var host = new TestHost().Build(s => { s.Features.Images = false; s.Features.RepositoryAccess = false; });
        var chat = host.Get<ChatService>();

        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest
        {
            Message = "hola",
            Images = { new ChatImage { MimeType = "image/png", Base64 = Convert.ToBase64String(Png) } }
        }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest { Message = "hola", ProjectId = "demo" }, default));

        Assert.Empty(host.Runner.Requests);
    }

    [Fact]
    public async Task The_orchestrator_is_told_about_language_and_disabled_capabilities()
    {
        var host = new TestHost().Build(s => { s.Chat.ResponseLanguage = "inglés"; s.Features.TerraformGeneration = false; });
        host.Runner.Script(AgentRole.Orchestrator, "ok");

        await host.Get<ChatService>().HandleAsync(new ChatRequest { Message = "hola" }, default);

        var message = host.Runner.Requests.Single().UserMessage;
        Assert.Contains("Idioma de respuesta: inglés", message);
        Assert.Contains("generación de Terraform", message);
    }

    // ---------------- Imágenes ----------------

    [Fact]
    public async Task Allowed_image_types_are_configurable_and_signatures_are_still_checked()
    {
        var host = new TestHost().Build(s => s.Chat.AllowedImageTypes = "image/png,image/webp");
        host.Runner.Script(AgentRole.Orchestrator, "ok");
        var chat = host.Get<ChatService>();

        await chat.HandleAsync(new ChatRequest
        {
            Message = "hola",
            Images = { new ChatImage { MimeType = "image/webp", Base64 = Convert.ToBase64String(Webp) } }
        }, default);

        // JPEG ya no está permitido y un WEBP falso se rechaza por su firma.
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest
        {
            Message = "hola",
            Images = { new ChatImage { MimeType = "image/jpeg", Base64 = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }) } }
        }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest
        {
            Message = "hola",
            Images = { new ChatImage { MimeType = "image/webp", Base64 = Convert.ToBase64String(Png) } }
        }, default));
    }

    // ---------------- Generación ----------------

    [Fact]
    public async Task Generation_parameters_reach_the_generator_message()
    {
        var host = new TestHost().Build(s =>
        {
            s.Generation.DefaultRegion = "brazilsouth";
            s.Generation.AllowedRegions = "brazilsouth,eastus2";
            s.Generation.NamingPattern = "{app}-{tipo}-{ambiente}";
            s.Generation.RequiredTags = "owner,costcenter";
            s.Generation.AzurermVersion = "~> 3.100";
        });
        host.Runner
            .Script(AgentRole.IacGenerator, Json.Generator("main.tf", GoodTf))
            .Script(AgentRole.Security, Json.Review("ok"));

        await host.Get<TerraformGenerationService>().GenerateAsync("storage", null, Ctx(), default);

        var message = host.Runner.For(AgentRole.IacGenerator).Single().UserMessage;
        Assert.Contains("`brazilsouth`", message);
        Assert.Contains("`{app}-{tipo}-{ambiente}`", message);
        Assert.Contains("`owner`", message);
        Assert.Contains("`~> 3.100`", message);
    }

    [Fact]
    public async Task Automatic_security_review_can_be_turned_off()
    {
        var host = new TestHost().Build(s => s.Generation.AutoSecurityReview = false);
        host.Runner.Script(AgentRole.IacGenerator, Json.Generator("main.tf", GoodTf));

        var result = await host.Get<TerraformGenerationService>().GenerateAsync("storage", null, Ctx(), default);

        Assert.Single(result.Files);
        Assert.Null(result.SecurityReview);
        Assert.Empty(host.Runner.For(AgentRole.Security));
    }

    [Fact]
    public async Task Blocking_severity_threshold_is_configurable()
    {
        const string badTf = "resource \"azurerm_storage_account\" \"datos\" {\n  public_network_access_enabled = true\n}";
        // SEC-001 es High: con BlockFrom = Critical ya no dispara autocorrección.
        var host = new TestHost().Build(s => s.Generation.BlockFrom = Severity.Critical);
        host.Runner
            .Script(AgentRole.IacGenerator, Json.Generator("main.tf", badTf))
            .Script(AgentRole.Security, Json.Review("v1", Json.Finding("SEC-001", "main.tf", "public_network_access_enabled = true", line: 2)));

        var result = await host.Get<TerraformGenerationService>().GenerateAsync("storage", null, Ctx(), default);

        Assert.False(result.HasBlockingFindings);
        Assert.Equal(0, result.FixRounds);
    }

    [Fact]
    public async Task Allowed_generated_extensions_are_configurable_but_never_executables()
    {
        var output = System.Text.Json.JsonSerializer.Serialize(new
        {
            summary = "x",
            files = new[] { new { path = "config.json", content = "{}" }, new { path = "run.sh", content = "echo" } },
            policiesApplied = Array.Empty<string>(),
            notes = Array.Empty<string>()
        });

        var strict = new TestHost().Build();
        strict.Runner.Script(AgentRole.IacGenerator, output);
        var withDefaults = await strict.Get<TerraformGenerationService>().GenerateAsync("x", null, Ctx(), default);
        Assert.Empty(withDefaults.Files);

        var flexible = new TestHost().Build(s => s.Generation.AllowedFileExtensions = ".tf,.json");
        flexible.Runner.Script(AgentRole.IacGenerator, output).Script(AgentRole.Security, Json.Review("ok"));
        var result = await flexible.Get<TerraformGenerationService>().GenerateAsync("x", null, Ctx(), default);
        Assert.Equal(new[] { "config.json" }, result.Files.Select(f => f.Path));

        // La validación de configuración impide habilitar extensiones ejecutables.
        var unsafeSettings = TestHost.DefaultSettings();
        unsafeSettings.Generation.AllowedFileExtensions = ".tf,.sh";
        Assert.Contains(InfraAgentSettingsValidator.Validate(unsafeSettings), e => e.Contains("AllowedFileExtensions"));
    }

    // ---------------- Lectura de repositorio ----------------

    [Fact]
    public async Task Repository_tools_only_read_whitelisted_extensions()
    {
        var host = new TestHost().Build();
        var repo = new FakeRepositoryReader(new()
        {
            ["main.tf"] = "ok",
            ["terraform.tfstate"] = "{\"secreto\":true}",
            ["id_rsa.pem"] = "-----BEGIN",
            ["docs/arq.md"] = "# doc"
        });
        var invoker = host.Get<IToolInvoker>();

        var tree = await invoker.InvokeAsync(AgentRole.Architecture, ToolNames.GetRepoTree, "{}", Ctx(repo), default);
        var state = await invoker.InvokeAsync(AgentRole.Architecture, ToolNames.GetFile, """{"path":"terraform.tfstate"}""", Ctx(repo), default);

        Assert.Contains("main.tf", tree);
        Assert.Contains("docs/arq.md", tree);
        Assert.DoesNotContain("tfstate", tree);
        Assert.DoesNotContain("pem", tree);
        Assert.StartsWith("ERROR de validación", state);

        var custom = new TestHost().Build(s => s.Analysis.ReadableExtensions = ".tf");
        var customTree = await custom.Get<IToolInvoker>().InvokeAsync(AgentRole.Architecture, ToolNames.GetRepoTree, "{}", Ctx(repo), default);
        Assert.DoesNotContain("arq.md", customTree);
    }

    [Fact]
    public void Repository_provider_none_hides_configured_projects()
    {
        var host = new TestHost().Build(s => s.Repositories.Provider = RepositoryProvider.None);

        Assert.Empty(host.Get<ProjectCatalog>().All);
        Assert.Throws<ArgumentException>(() => host.Get<ProjectCatalog>().Get("demo"));
    }

    // ---------------- Conocimiento ----------------

    [Fact]
    public void Organization_name_and_language_are_part_of_every_agent_prompt()
    {
        var host = new TestHost().Build(s => { s.Knowledge.OrganizationName = "Banco Andino"; s.Chat.ResponseLanguage = "portugués"; });

        var text = host.Get<PromptBuilder>().BuildInstructions(AgentRole.Policy);

        Assert.Contains("Banco Andino", text);
        Assert.Contains("portugués", text);
    }

    [Fact]
    public void Policy_catalog_path_can_point_to_another_file()
    {
        var host = new TestHost().Build();
        Assert.True(host.Get<InfraAgent.Core.Policies.PolicyCatalog>().Policies.Count > 0);

        var other = TestHost.DefaultSettings();
        other.Knowledge.PolicyCatalogPath = Path.Combine(Path.GetTempPath(), "no-existe.yaml");
        Assert.ThrowsAny<IOException>(() => InfraAgent.Core.Policies.PolicyCatalogLoader.LoadFromFile(other.Knowledge.PolicyCatalogPath));
    }

    // ---------------- Validación de la configuración ----------------

    [Fact]
    public void Validation_depends_on_the_selected_repository_provider()
    {
        var none = TestHost.DefaultSettings();
        none.Repositories.Provider = RepositoryProvider.None;
        none.Projects.Clear();
        none.Projects.Add(new ProjectSettings { Id = "!!", Path = "" });          // se ignoran con None
        Assert.Empty(InfraAgentSettingsValidator.Validate(none));

        var azdo = TestHost.DefaultSettings();
        azdo.Repositories.Provider = RepositoryProvider.AzureDevOps;
        var errors = InfraAgentSettingsValidator.Validate(azdo);
        Assert.Contains(errors, e => e.Contains("OrganizationUrl"));
        Assert.Contains(errors, e => e.Contains("Projects:0:Project"));

        var local = TestHost.DefaultSettings();
        local.Projects[0].Path = "";
        Assert.Contains(InfraAgentSettingsValidator.Validate(local), e => e.Contains("Projects:0:Path"));
    }

    [Fact]
    public void Generation_and_chat_parameters_are_validated()
    {
        var s = TestHost.DefaultSettings();
        s.Generation.AllowedRegions = "eastus2";
        s.Generation.DefaultRegion = "westus";
        s.Generation.RequiredTags = "Owner Name";
        s.Chat.AllowedImageTypes = "image/svg+xml";
        s.Chat.ResponseLanguage = "";

        var errors = InfraAgentSettingsValidator.Validate(s);

        Assert.Contains(errors, e => e.Contains("DefaultRegion"));
        Assert.Contains(errors, e => e.Contains("RequiredTags"));
        Assert.Contains(errors, e => e.Contains("AllowedImageTypes"));
        Assert.Contains(errors, e => e.Contains("ResponseLanguage"));
    }
}
