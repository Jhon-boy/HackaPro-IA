using InfraAgent.Models;
using InfraAgent.Settings;

namespace InfraAgent.Tools;

public static class ToolNames
{
    // Compartidas
    public const string LoadSkill = "load_skill";
    public const string GetPolicies = "get_policies";
    public const string GetFile = "get_file";
    public const string GetRepoTree = "get_repo_tree";

    // Del orquestador (cada una invoca a un agente especialista)
    public const string AnalyzeArchitecture = "analizar_arquitectura";
    public const string EvaluatePolicies = "evaluar_politicas";
    public const string GenerateTerraform = "generar_terraform";
    public const string ReviewSecurity = "revisar_seguridad";
    public const string ListProjects = "listar_proyectos";
}

/// <summary>
/// Lista cerrada de herramientas por agente. Vive en código (no en los .md) para que ninguna
/// instrucción o skill pueda ampliar privilegios. Todas son de solo lectura.
/// </summary>
public static class ToolAllowList
{
    private static readonly IReadOnlyDictionary<AgentRole, HashSet<string>> Map = new Dictionary<AgentRole, HashSet<string>>
    {
        [AgentRole.Orchestrator] = new(StringComparer.Ordinal)
        {
            ToolNames.AnalyzeArchitecture, ToolNames.EvaluatePolicies, ToolNames.GenerateTerraform,
            ToolNames.ReviewSecurity, ToolNames.ListProjects, ToolNames.LoadSkill
        },
        [AgentRole.Architecture] = new(StringComparer.Ordinal)
        {
            ToolNames.GetRepoTree, ToolNames.GetFile, ToolNames.LoadSkill
        },
        [AgentRole.Policy] = new(StringComparer.Ordinal)
        {
            ToolNames.GetPolicies, ToolNames.GetFile, ToolNames.LoadSkill
        },
        [AgentRole.IacGenerator] = new(StringComparer.Ordinal)
        {
            ToolNames.GetPolicies, ToolNames.LoadSkill
        },
        [AgentRole.Security] = new(StringComparer.Ordinal)
        {
            ToolNames.GetPolicies, ToolNames.GetFile, ToolNames.GetRepoTree, ToolNames.LoadSkill
        }
    };

    /// <summary>
    /// Los interruptores de configuración solo pueden quitar herramientas de la lista cerrada, nunca añadirlas.
    /// </summary>
    public static bool IsEnabled(string toolName, FeatureSettings features) => toolName switch
    {
        ToolNames.GetFile or ToolNames.GetRepoTree => features.RepositoryAccess,
        ToolNames.AnalyzeArchitecture => features.ArchitectureAnalysis,
        ToolNames.EvaluatePolicies => features.PolicyEvaluation,
        ToolNames.GenerateTerraform => features.TerraformGeneration,
        ToolNames.ReviewSecurity => features.SecurityReview,
        _ => true
    };

    public static bool IsAllowed(AgentRole role, string toolName) =>
        Map.TryGetValue(role, out var set) && set.Contains(toolName);

    public static IReadOnlyCollection<string> For(AgentRole role) =>
        Map.TryGetValue(role, out var set) ? set : Array.Empty<string>();
}
