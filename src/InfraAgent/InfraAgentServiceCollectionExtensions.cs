using Azure.Core;
using Azure.Identity;
using InfraAgent.Abstractions;
using InfraAgent.Auditing;
using InfraAgent.Settings;
using InfraAgent.AzureDevOps;
using InfraAgent.Foundry;
using InfraAgent.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace InfraAgent;

public static class InfraAgentServiceCollectionExtensions
{
    /// <summary>Registra todo InfraAgent (configuración validada, Foundry, repositorios, auditoría y casos de uso).</summary>
    public static IServiceCollection AddInfraAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InfraAgentSettings>()
            .Bind(configuration.GetSection(InfraAgentSettings.SectionName));
        // La validación ocurre al primer uso (IOptions.Value). La Function la expone en /api/status sin caerse;
        // la CLI la ejecuta explícitamente al arrancar.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InfraAgentSettings>, InfraAgentSettingsValidation>());

        // El conocimiento (instrucciones, skills, políticas) se carga sin exigir el resto de la configuración,
        // para poder diagnosticarlo y listar políticas aunque falte Foundry o Azure DevOps.
        services.AddSingleton(configuration.GetSection($"{InfraAgentSettings.SectionName}:Knowledge").Get<KnowledgeSettings>() ?? new KnowledgeSettings());

        // Identidad: identidad administrada en Azure, az login / Visual Studio en local.
        services.AddSingleton<TokenCredential>(sp =>
        {
            var clientId = sp.GetRequiredService<IOptions<InfraAgentSettings>>().Value.Identity.ManagedIdentityClientId;
            return new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId
            });
        });

        services.AddHttpClient<AzureDevOpsClient>();
        services.AddSingleton<IRepositoryReaderFactory, ProjectRepositoryFactory>();

        services.AddSingleton<IAgentRunner, FoundryAgentRunner>();
        services.AddSingleton<IAuditSink, LoggingAuditSink>();

        services.AddInfraAgentApplication();
        return services;
    }
}

/// <summary>Valida la configuración al arrancar y falla con mensajes claros (sin valores secretos).</summary>
internal sealed class InfraAgentSettingsValidation : IValidateOptions<InfraAgentSettings>
{
    public ValidateOptionsResult Validate(string? name, InfraAgentSettings options)
    {
        var errors = InfraAgentSettingsValidator.Validate(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
