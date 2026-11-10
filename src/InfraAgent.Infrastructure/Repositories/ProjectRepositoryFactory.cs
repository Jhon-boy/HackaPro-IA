using InfraAgent.Application;
using InfraAgent.Application.Abstractions;
using InfraAgent.Core.Settings;
using InfraAgent.Infrastructure.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InfraAgent.Infrastructure.Repositories;

/// <summary>
/// Elige el lector de repositorio según Repositories:Provider. El cliente de Azure DevOps solo se crea
/// cuando se usa, de modo que no hace falta configurarlo si el proveedor es Local o None.
/// </summary>
public sealed class ProjectRepositoryFactory : IRepositoryReaderFactory
{
    private readonly IServiceProvider _services;
    private readonly RepositorySettings _repositories;

    public ProjectRepositoryFactory(IServiceProvider services, IOptions<InfraAgentSettings> settings)
    {
        _services = services;
        _repositories = settings.Value.Repositories;
    }

    public RepositoryProvider Provider => _repositories.Provider;

    public IRepositoryReader Create(ProjectSettings project) => _repositories.Provider switch
    {
        RepositoryProvider.Local => new LocalRepositoryReader(ResolveLocalPath(project.Path)),
        RepositoryProvider.AzureDevOps => _services.GetRequiredService<AzureDevOpsClient>()
            .Create(project.Project, project.Repository, new RepoVersion(RepoVersionKind.Branch, project.Branch)),
        _ => throw new ArgumentException("No hay un proveedor de repositorios configurado (Repositories:Provider = None).")
    };

    private string ResolveLocalPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var root = string.IsNullOrWhiteSpace(_repositories.LocalRoot)
            ? AppContext.BaseDirectory
            : ApplicationServiceCollectionExtensions.ResolvePath(_repositories.LocalRoot);
        return Path.Combine(root, path);
    }
}
