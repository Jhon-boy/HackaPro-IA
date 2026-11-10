using InfraAgent.Core.Settings;
using InfraAgent.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InfraAgent.Tests.Infrastructure;

public sealed class TempProject : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"infraagent-{Guid.NewGuid():N}");

    public TempProject(Dictionary<string, string> files)
    {
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        Directory.CreateDirectory(Root);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

public class LocalRepositoryTests
{
    private static TempProject Sample() => new(new()
    {
        ["main.tf"] = "resource \"x\" \"y\" {}",
        ["modules/net/vnet.tf"] = "# net",
        [".git/config"] = "secreto-git",
        [".terraform/providers/x.tf"] = "ruido"
    });

    [Fact]
    public async Task Lists_files_with_relative_paths_skipping_noise_folders()
    {
        using var project = Sample();
        var reader = new LocalRepositoryReader(project.Root);

        var files = await reader.ListFilesAsync(null, 100, default);

        Assert.Equal(new[] { "main.tf", "modules/net/vnet.tf" }, files);
        Assert.Equal(new[] { "modules/net/vnet.tf" }, await reader.ListFilesAsync("modules", 100, default));
        Assert.Single(await reader.ListFilesAsync(null, 1, default));
    }

    [Fact]
    public async Task Reads_files_truncates_by_size_and_returns_null_when_missing()
    {
        using var project = new TempProject(new() { ["big.tf"] = new string('a', 5_000) });
        var reader = new LocalRepositoryReader(project.Root);

        var file = await reader.GetFileAsync("big.tf", 1_000, default);

        Assert.True(file!.Truncated);
        Assert.Equal(1_000, file.Content.Length);
        Assert.Null(await reader.GetFileAsync("no-existe.tf", 1_000, default));
    }

    [Theory]
    [InlineData("../fuera.tf")]
    [InlineData("a/../../fuera.tf")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("/etc/passwd")]
    public async Task Paths_outside_the_project_are_rejected_or_not_found(string path)
    {
        using var project = Sample();
        var reader = new LocalRepositoryReader(project.Root);

        try
        {
            Assert.Null(await reader.GetFileAsync(path, 1_000, default));
        }
        catch (ArgumentException)
        {
            // También es un resultado válido: la ruta se rechaza.
        }
    }

    [Fact]
    public void A_missing_project_folder_fails_without_exposing_the_full_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), "carpeta-que-no-existe-xyz");
        var ex = Assert.Throws<DirectoryNotFoundException>(() => new LocalRepositoryReader(missing));

        Assert.DoesNotContain(Path.GetTempPath(), ex.Message);
    }

    [Fact]
    public async Task Factory_resolves_absolute_and_relative_local_paths_and_rejects_none()
    {
        using var project = Sample();
        var parent = Path.GetDirectoryName(project.Root)!;

        IRepositoryFactoryHolder Build(RepositoryProvider provider, string localRoot) => new(provider, localRoot);

        var absolute = Build(RepositoryProvider.Local, "").Factory.Create(new ProjectSettings { Id = "demo", Path = project.Root });
        Assert.Contains("main.tf", await absolute.ListFilesAsync(null, 10, default));

        var relative = Build(RepositoryProvider.Local, parent).Factory.Create(new ProjectSettings { Id = "demo", Path = Path.GetFileName(project.Root) });
        Assert.Contains("main.tf", await relative.ListFilesAsync(null, 10, default));

        Assert.Throws<ArgumentException>(() => Build(RepositoryProvider.None, "").Factory.Create(new ProjectSettings { Id = "demo" }));
    }

    private sealed class IRepositoryFactoryHolder
    {
        public ProjectRepositoryFactory Factory { get; }

        public IRepositoryFactoryHolder(RepositoryProvider provider, string localRoot)
        {
            var settings = new InfraAgentSettings { Repositories = { Provider = provider, LocalRoot = localRoot } };
            Factory = new ProjectRepositoryFactory(new ServiceCollection().BuildServiceProvider(), Options.Create(settings));
        }
    }
}
