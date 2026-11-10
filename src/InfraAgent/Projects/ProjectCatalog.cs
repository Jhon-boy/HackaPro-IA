using InfraAgent.Settings;
using InfraAgent.Validation;
using Microsoft.Extensions.Options;

namespace InfraAgent.Projects;

/// <summary>Lista cerrada de proyectos habilitados. Nada fuera de esta lista se puede leer.</summary>
public sealed class ProjectCatalog
{
    private readonly InfraAgentSettings _settings;

    public ProjectCatalog(IOptions<InfraAgentSettings> settings) => _settings = settings.Value;

    /// <summary>Con Provider = None no hay proyectos disponibles, aunque estén listados en la configuración.</summary>
    public IReadOnlyList<ProjectSettings> All =>
        _settings.Repositories.Provider == RepositoryProvider.None ? Array.Empty<ProjectSettings>() : _settings.Projects;

    public ProjectSettings Get(string id)
    {
        var normalized = InputValidators.ProjectId(id);
        return All.FirstOrDefault(p => p.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException($"El proyecto '{normalized}' no está habilitado.");
    }
}
