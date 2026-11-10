using InfraAgent.Core.Models;
using InfraAgent.Core.Settings;
using InfraAgent.Infrastructure;
using InfraAgent.Tests.Support;
using Microsoft.Extensions.Configuration;

namespace InfraAgent.Tests.Core;

// Colección propia: estas pruebas modifican variables de entorno del proceso.
[CollectionDefinition("entorno", DisableParallelization = true)]
public class EnvironmentCollection { }

[Collection("entorno")]
public class SettingsTests
{
    [Fact]
    public void Default_settings_report_what_is_missing_without_values()
    {
        var errors = InfraAgentSettingsValidator.Validate(new InfraAgentSettings());

        Assert.Contains(errors, e => e.Contains("Foundry:Endpoint"));
        Assert.Contains(errors, e => e.Contains("Orchestrator"));
    }

    [Fact]
    public void Complete_settings_are_valid_and_per_role_agent_overrides_the_default()
    {
        var s = TestHost.DefaultSettings();
        s.Foundry.Agents.Security = "asst_seg";

        Assert.Empty(InfraAgentSettingsValidator.Validate(s));
        Assert.Equal("asst_seg", s.Foundry.ResolveAgentId(AgentRole.Security));
        Assert.Equal("asst_demo", s.Foundry.ResolveAgentId(AgentRole.Policy));
    }

    [Fact]
    public void Ranges_and_projects_are_validated()
    {
        var s = TestHost.DefaultSettings();
        s.Analysis.MaxFiles = 0;
        s.Chat.TimeoutSeconds = 500;
        s.Generation.MaxFixRounds = 9;
        s.Projects.Add(new ProjectSettings { Id = "demo", Path = "x" });
        s.Analysis.FileExtensions = "tf";

        var errors = InfraAgentSettingsValidator.Validate(s);

        Assert.Contains(errors, e => e.Contains("Analysis:MaxFiles"));
        Assert.Contains(errors, e => e.Contains("Chat:TimeoutSeconds"));
        Assert.Contains(errors, e => e.Contains("MaxFixRounds"));
        Assert.Contains(errors, e => e.Contains("duplicado"));
        Assert.Contains(errors, e => e.Contains("FileExtensions"));
    }

    [Fact]
    public void Presence_never_exposes_values()
    {
        var s = TestHost.DefaultSettings();
        s.AzureDevOps.Pat = "pat-super-secreto";

        var presence = InfraAgentSettingsValidator.Presence(s);

        Assert.True(presence["InfraAgent:AzureDevOps:Pat"]);
        Assert.DoesNotContain("pat-super-secreto", string.Join(",", presence.Keys));
    }

    [Fact]
    public void Environment_variables_override_the_json_file()
    {
        var file = Path.Combine(Path.GetTempPath(), $"infraagent-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{"InfraAgent":{"Analysis":{"MaxFiles":10},"Foundry":{"RunTimeoutSeconds":99}}}""");
        var previousConfig = Environment.GetEnvironmentVariable(InfraAgentConfiguration.ConfigPathVariable);
        Environment.SetEnvironmentVariable(InfraAgentConfiguration.ConfigPathVariable, file);
        Environment.SetEnvironmentVariable("InfraAgent__Analysis__MaxFiles", "25");

        try
        {
            var config = new ConfigurationBuilder().AddInfraAgentConfiguration().Build();
            var settings = config.GetSection("InfraAgent").Get<InfraAgentSettings>()!;

            Assert.Equal(25, settings.Analysis.MaxFiles);            // variable de entorno gana
            Assert.Equal(99, settings.Foundry.RunTimeoutSeconds);    // del archivo
        }
        finally
        {
            Environment.SetEnvironmentVariable("InfraAgent__Analysis__MaxFiles", null);
            Environment.SetEnvironmentVariable(InfraAgentConfiguration.ConfigPathVariable, previousConfig);
            File.Delete(file);
        }
    }

    [Fact]
    public void Every_section_can_be_overridden_by_environment_variables()
    {
        var variables = new Dictionary<string, string>
        {
            ["InfraAgent__Features__Images"] = "false",
            ["InfraAgent__Repositories__Provider"] = "AzureDevOps",
            ["InfraAgent__Generation__BlockFrom"] = "Medium",
            ["InfraAgent__Generation__RequiredTags"] = "owner,costcenter",
            ["InfraAgent__Chat__ResponseLanguage"] = "inglés",
            ["InfraAgent__Projects__0__Id"] = "otro"
        };
        foreach (var (k, v) in variables) Environment.SetEnvironmentVariable(k, v);

        try
        {
            var config = new ConfigurationBuilder().AddInfraAgentConfiguration().Build();
            var s = config.GetSection("InfraAgent").Get<InfraAgentSettings>()!;

            Assert.False(s.Features.Images);
            Assert.Equal(RepositoryProvider.AzureDevOps, s.Repositories.Provider);
            Assert.Equal(Severity.Medium, s.Generation.BlockFrom);
            Assert.Equal(new[] { "owner", "costcenter" }, s.Generation.RequiredTagList);
            Assert.Equal("inglés", s.Chat.ResponseLanguage);
            Assert.Equal("otro", s.Projects[0].Id);
        }
        finally
        {
            foreach (var k in variables.Keys) Environment.SetEnvironmentVariable(k, null);
        }
    }

    [Fact]
    public void The_demo_project_in_the_default_configuration_points_to_an_existing_sample()
    {
        var path = Path.Combine(RepoPaths.Root, "config", "infraagent.settings.json");
        var settings = new ConfigurationBuilder().AddJsonFile(path).Build().GetSection("InfraAgent").Get<InfraAgentSettings>()!;

        var project = Assert.Single(settings.Projects);
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, project.Path, "main.tf")));
    }

    [Fact]
    public void Repository_settings_file_is_valid_json_with_the_expected_sections()
    {
        var path = Path.Combine(RepoPaths.Root, "config", "infraagent.settings.json");
        var config = new ConfigurationBuilder().AddJsonFile(path).Build();
        var settings = config.GetSection("InfraAgent").Get<InfraAgentSettings>()!;

        Assert.Equal("agents", settings.Knowledge.AgentsDirectory);
        Assert.Equal(Severity.High, settings.Generation.BlockFrom);
        Assert.Equal(RepositoryProvider.Local, settings.Repositories.Provider);
        Assert.True(settings.Features.TerraformGeneration);
        Assert.Equal(new[] { ".tf", ".tfvars" }, settings.Analysis.ExtensionList);
        Assert.NotEmpty(settings.Projects);
    }
}

