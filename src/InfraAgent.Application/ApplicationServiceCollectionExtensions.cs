using InfraAgent.Application.Abstractions;
using InfraAgent.Application.Agents;
using InfraAgent.Application.Chat;
using InfraAgent.Application.Knowledge;
using InfraAgent.Application.Projects;
using InfraAgent.Application.Review;
using InfraAgent.Application.Tools;
using InfraAgent.Core.Policies;
using InfraAgent.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace InfraAgent.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registra agentes, herramientas, flujos y conocimiento (instrucciones, skills, políticas).</summary>
    public static IServiceCollection AddInfraAgentApplication(this IServiceCollection services)
    {
        // Conocimiento versionado en el repositorio (carga una vez al arrancar; falla rápido si es inválido).
        services.AddSingleton(sp =>
        {
            var knowledge = sp.GetRequiredService<KnowledgeSettings>();
            return AgentCatalog.Load(ResolvePath(knowledge.AgentsDirectory));
        });
        services.AddSingleton(sp =>
        {
            var knowledge = sp.GetRequiredService<KnowledgeSettings>();
            return PolicyCatalogLoader.LoadFromFile(ResolvePath(knowledge.PolicyCatalogPath));
        });
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<ProjectCatalog>();

        // Herramientas (lista cerrada por rol en ToolAllowList).
        services.AddSingleton<IAgentTool, LoadSkillTool>();
        services.AddSingleton<IAgentTool, GetPoliciesTool>();
        services.AddSingleton<IAgentTool, GetFileTool>();
        services.AddSingleton<IAgentTool, GetRepoTreeTool>();
        services.AddSingleton<IAgentTool, AnalyzeArchitectureTool>();
        services.AddSingleton<IAgentTool, EvaluatePoliciesTool>();
        services.AddSingleton<IAgentTool, GenerateTerraformTool>();
        services.AddSingleton<IAgentTool, ReviewSecurityTool>();
        services.AddSingleton<IAgentTool, ListProjectsTool>();
        services.AddSingleton<IToolInvoker, ToolInvoker>();

        // Agentes y flujos.
        services.AddSingleton<AgentService>();
        services.AddSingleton<ArchitectureService>();
        services.AddSingleton<ReviewerService>();
        services.AddSingleton<TerraformGenerationService>();
        services.AddSingleton<ChatService>();
        return services;
    }

    /// <summary>Las rutas relativas se resuelven contra la carpeta de la aplicación (donde se publican agents/ y policies/).</summary>
    public static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
