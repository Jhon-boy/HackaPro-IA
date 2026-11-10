using InfraAgent.Application.Knowledge;
using InfraAgent.Application.Projects;
using InfraAgent.Core.Policies;
using InfraAgent.Core.Settings;
using InfraAgent.Functions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraAgent.Functions.Functions;

public sealed class HealthFunctions
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly ILogger<HealthFunctions> _logger;

    public HealthFunctions(IConfiguration configuration, IServiceProvider services, ILogger<HealthFunctions> logger)
    {
        _configuration = configuration;
        _services = services;
        _logger = logger;
    }

    private InfraAgentSettings ReadSettings() =>
        _configuration.GetSection(InfraAgentSettings.SectionName).Get<InfraAgentSettings>() ?? new InfraAgentSettings();

    /// <summary>Sonda mínima (anónima) para monitoreo. No expone configuración.</summary>
    [Function("Health")]
    public IActionResult Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req) =>
        ApiGuard.Ok(new { status = "ok", service = "InfraAgent" });

    /// <summary>
    /// Diagnóstico de configuración: indica qué falta o es inválido. Solo nombres de variables, JAMÁS valores.
    /// </summary>
    [Function("Status")]
    public Task<IActionResult> Status([HttpTrigger(AuthorizationLevel.Function, "get", Route = "status")] HttpRequest req) =>
        ApiGuard.RunAsync(_logger, () =>
        {
            var settings = ReadSettings();
            var errors = InfraAgentSettingsValidator.Validate(settings);

            string? knowledgeVersion = null, catalogVersion = null, knowledgeError = null;
            var policyCount = 0;
            if (errors.Count == 0)
            {
                try
                {
                    knowledgeVersion = _services.GetRequiredService<AgentCatalog>().Version;
                    var catalog = _services.GetRequiredService<PolicyCatalog>();
                    catalogVersion = catalog.Version;
                    policyCount = catalog.Policies.Count;
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
                {
                    knowledgeError = ex.Message;
                }
            }

            var ready = errors.Count == 0 && knowledgeError is null;
            return Task.FromResult(ApiGuard.Json(ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, new
            {
                ready,
                configurationErrors = errors,
                knowledgeError,
                present = InfraAgentSettingsValidator.Presence(settings),
                agentsKnowledgeVersion = knowledgeVersion,
                policyCatalogVersion = catalogVersion,
                policyCount,
                repositoryProvider = settings.Repositories.Provider.ToString(),
                projects = settings.Projects.Select(p => p.Id)
            }));
        });

    /// <summary>
    /// Configuración efectiva NO secreta (para que el equipo vea con qué parámetros está corriendo).
    /// No incluye endpoints, ids de agentes, PAT ni identidades.
    /// </summary>
    [Function("Config")]
    public Task<IActionResult> Config([HttpTrigger(AuthorizationLevel.Function, "get", Route = "config")] HttpRequest req) =>
        ApiGuard.RunAsync(_logger, () =>
        {
            var s = ReadSettings();
            return Task.FromResult(ApiGuard.Ok(new
            {
                features = s.Features,
                repositories = new { provider = s.Repositories.Provider.ToString() },
                knowledge = s.Knowledge,
                analysis = s.Analysis,
                chat = s.Chat,
                generation = s.Generation,
                foundry = new { s.Foundry.RunTimeoutSeconds, s.Foundry.PollIntervalMs, s.Foundry.MaxToolRounds }
            }));
        });

    [Function("Projects")]
    public Task<IActionResult> Projects([HttpTrigger(AuthorizationLevel.Function, "get", Route = "projects")] HttpRequest req) =>
        ApiGuard.RunAsync(_logger, () =>
        {
            var projects = _services.GetRequiredService<ProjectCatalog>().All
                .Select(p => new { p.Id, p.DisplayName });
            return Task.FromResult(ApiGuard.Ok(projects));
        });
}
