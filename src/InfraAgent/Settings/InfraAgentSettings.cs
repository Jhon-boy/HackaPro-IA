using InfraAgent.Models;

namespace InfraAgent.Settings;

/// <summary>
/// Configuración ÚNICA de InfraAgent. Se enlaza a la sección "InfraAgent".
/// Precedencia (de menor a mayor): config/infraagent.settings.json → variables de entorno / App Settings
/// de la Function (InfraAgent__Foundry__Endpoint) → referencias a Key Vault.
/// El archivo JSON NO lleva secretos; los secretos solo por App Settings (Key Vault) o user-secrets.
/// Cada propiedad con valor por defecto es opcional. Referencia completa: docs/CONFIGURACION.md.
/// </summary>
public sealed class InfraAgentSettings
{
    public const string SectionName = "InfraAgent";

    public IdentitySettings Identity { get; set; } = new();
    public FoundrySettings Foundry { get; set; } = new();
    public FeatureSettings Features { get; set; } = new();
    public RepositorySettings Repositories { get; set; } = new();
    public AzureDevOpsSettings AzureDevOps { get; set; } = new();
    public KnowledgeSettings Knowledge { get; set; } = new();
    public AnalysisSettings Analysis { get; set; } = new();
    public ChatSettings Chat { get; set; } = new();
    public GenerationSettings Generation { get; set; } = new();

    /// <summary>Proyectos habilitados (lista cerrada). Máximo 20. Solo se usan si Repositories:Provider no es None.</summary>
    public List<ProjectSettings> Projects { get; set; } = new();
}

public sealed class IdentitySettings
{
    /// <summary>Client Id de la identidad administrada asignada por el usuario. Vacío = identidad del sistema / az login local.</summary>
    public string ManagedIdentityClientId { get; set; } = "";
}

public sealed class FoundrySettings
{
    /// <summary>Endpoint del proyecto de Azure AI Foundry (https://&lt;recurso&gt;.services.ai.azure.com/api/projects/&lt;proyecto&gt;).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Id del agente de Foundry usado cuando un rol no tiene uno propio.</summary>
    public string DefaultAgentId { get; set; } = "";

    public AgentIdsSettings Agents { get; set; } = new();

    public int RunTimeoutSeconds { get; set; } = 180;
    public int PollIntervalMs { get; set; } = 800;
    public int MaxToolRounds { get; set; } = 8;

    public string ResolveAgentId(AgentRole role)
    {
        var specific = role switch
        {
            AgentRole.Orchestrator => Agents.Orchestrator,
            AgentRole.Architecture => Agents.Architecture,
            AgentRole.Policy => Agents.Policy,
            AgentRole.IacGenerator => Agents.IacGenerator,
            AgentRole.Security => Agents.Security,
            _ => ""
        };
        return string.IsNullOrWhiteSpace(specific) ? DefaultAgentId : specific;
    }
}

/// <summary>Un Id de agente de Foundry por responsabilidad. Vacío = se usa DefaultAgentId.</summary>
public sealed class AgentIdsSettings
{
    public string Orchestrator { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Policy { get; set; } = "";
    public string IacGenerator { get; set; } = "";
    public string Security { get; set; } = "";
}

/// <summary>
/// Interruptores de capacidades. Solo pueden RESTRINGIR lo que el agente puede hacer; nunca ampliar la
/// lista cerrada de herramientas de solo lectura definida en código.
/// </summary>
public sealed class FeatureSettings
{
    /// <summary>Permite adjuntar imágenes de arquitectura en el chat.</summary>
    public bool Images { get; set; } = true;

    /// <summary>Permite al agente leer el repositorio del proyecto activo (get_repo_tree / get_file).</summary>
    public bool RepositoryAccess { get; set; } = true;

    public bool ArchitectureAnalysis { get; set; } = true;
    public bool PolicyEvaluation { get; set; } = true;
    public bool TerraformGeneration { get; set; } = true;
    public bool SecurityReview { get; set; } = true;
}

public enum RepositoryProvider
{
    /// <summary>Sin repositorio: el chat trabaja solo con texto e imágenes.</summary>
    None,

    /// <summary>Carpetas locales (ideal para demos y desarrollo).</summary>
    Local,

    /// <summary>Repositorios de Azure DevOps (solo lectura).</summary>
    AzureDevOps
}

public sealed class RepositorySettings
{
    public RepositoryProvider Provider { get; set; } = RepositoryProvider.None;

    /// <summary>Carpeta base para proyectos Local con ruta relativa (relativa a la carpeta de la aplicación si no es absoluta).</summary>
    public string LocalRoot { get; set; } = "";
}

public sealed class AzureDevOpsSettings
{
    /// <summary>URL de la organización, p. ej. https://dev.azure.com/mi-banco (solo si Provider = AzureDevOps).</summary>
    public string OrganizationUrl { get; set; } = "";

    /// <summary>
    /// PAT (SECRETO). Opcional: si está vacío se usa la identidad administrada (Entra ID), que debe
    /// estar agregada a la organización de Azure DevOps.
    /// </summary>
    public string Pat { get; set; } = "";

    public string ApiVersion { get; set; } = "7.1";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class KnowledgeSettings
{
    /// <summary>Carpeta con instrucciones y skills por agente (relativa a la carpeta de la aplicación).</summary>
    public string AgentsDirectory { get; set; } = "knowledge/agents";

    /// <summary>Catálogo de políticas de la organización (YAML).</summary>
    public string PolicyCatalogPath { get; set; } = "knowledge/policies/catalogo.yaml";

    /// <summary>Nombre con el que los agentes se refieren a la organización dueña de las políticas.</summary>
    public string OrganizationName { get; set; } = "el banco";
}

public sealed class AnalysisSettings
{
    public int MaxFiles { get; set; } = 40;
    public int MaxFileBytes { get; set; } = 100_000;
    public int MaxTotalBytes { get; set; } = 400_000;
    public int MaxToolOutputChars { get; set; } = 30_000;
    public int MaxTreeItems { get; set; } = 300;

    /// <summary>Extensiones de Terraform revisables, separadas por coma.</summary>
    public string FileExtensions { get; set; } = ".tf,.tfvars";

    /// <summary>Extensiones que el agente puede leer del repositorio (get_file / get_repo_tree). Lista blanca.</summary>
    public string ReadableExtensions { get; set; } = ".tf,.tfvars,.hcl,.md,.json,.yaml,.yml,.txt";

    /// <summary>Componentes y flujos máximos del modelo de arquitectura que viaja entre agentes.</summary>
    public int MaxComponents { get; set; } = 60;
    public int MaxFlows { get; set; } = 120;

    public IReadOnlyList<string> ExtensionList => ParseList(FileExtensions);
    public IReadOnlyList<string> ReadableExtensionList => ParseList(ReadableExtensions);

    internal static List<string> ParseList(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

public sealed class ChatSettings
{
    public int TimeoutSeconds { get; set; } = 200;
    public int MaxMessageChars { get; set; } = 8000;

    /// <summary>Idioma en el que responden los agentes.</summary>
    public string ResponseLanguage { get; set; } = "español";

    public int MaxImages { get; set; } = 4;
    public int MaxImageBytes { get; set; } = 5_000_000;

    /// <summary>Tipos de imagen aceptados (separados por coma). Admite image/png, image/jpeg, image/webp, image/gif.</summary>
    public string AllowedImageTypes { get; set; } = "image/png,image/jpeg";

    public int MaxGeneratedFiles { get; set; } = 20;
    public int MaxGeneratedFileBytes { get; set; } = 100_000;

    public IReadOnlyList<string> ImageTypeList =>
        AnalysisSettings.ParseList(AllowedImageTypes).Select(t => t.ToLowerInvariant()).ToList();
}

/// <summary>Parámetros que guían al generador de Terraform. Las políticas del catálogo siempre tienen prioridad.</summary>
public sealed class GenerationSettings
{
    /// <summary>Revisa con el agente de Seguridad el Terraform generado, antes de entregarlo.</summary>
    public bool AutoSecurityReview { get; set; } = true;

    /// <summary>Rondas máximas de autocorrección cuando la revisión halla algo bloqueante (0-3).</summary>
    public int MaxFixRounds { get; set; } = 1;

    /// <summary>Severidad desde la que un hallazgo se considera bloqueante para la autocorrección.</summary>
    public Severity BlockFrom { get; set; } = Severity.High;

    /// <summary>Políticas que se incluyen completas en el mensaje del agente; si hay más, se filtran por tipo de recurso.</summary>
    public int MaxPoliciesInline { get; set; } = 40;

    public string TerraformVersion { get; set; } = ">= 1.6.0";
    public string AzurermVersion { get; set; } = "~> 4.0";

    /// <summary>Región por defecto de los recursos generados.</summary>
    public string DefaultRegion { get; set; } = "eastus2";

    /// <summary>Regiones permitidas (coma). Vacío = las que defina el catálogo de políticas.</summary>
    public string AllowedRegions { get; set; } = "";

    /// <summary>Patrón de nombres de recursos.</summary>
    public string NamingPattern { get; set; } = "{tipo}-{app}-{ambiente}-{region}";

    /// <summary>Etiquetas obligatorias (coma).</summary>
    public string RequiredTags { get; set; } = "ambiente,proyecto,responsable,centro_costo";

    /// <summary>Extensiones permitidas en los archivos generados (coma). Solo texto declarativo.</summary>
    public string AllowedFileExtensions { get; set; } = ".tf,.tfvars,.md";

    public IReadOnlyList<string> AllowedRegionList => AnalysisSettings.ParseList(AllowedRegions);
    public IReadOnlyList<string> RequiredTagList => AnalysisSettings.ParseList(RequiredTags);
    public IReadOnlyList<string> AllowedFileExtensionList =>
        AnalysisSettings.ParseList(AllowedFileExtensions).Select(e => e.ToLowerInvariant()).ToList();
}

public sealed class ProjectSettings
{
    /// <summary>Id corto (a-z, 0-9, guion). Lo usa el chat.</summary>
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Provider Local: carpeta del proyecto (absoluta o relativa a Repositories:LocalRoot).</summary>
    public string Path { get; set; } = "";

    /// <summary>Provider AzureDevOps: proyecto.</summary>
    public string Project { get; set; } = "";

    /// <summary>Provider AzureDevOps: repositorio.</summary>
    public string Repository { get; set; } = "";

    /// <summary>Provider AzureDevOps: rama que se lee.</summary>
    public string Branch { get; set; } = "main";
}
