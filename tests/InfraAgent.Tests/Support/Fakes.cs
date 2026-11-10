using InfraAgent;
using InfraAgent.Abstractions;
using InfraAgent.Auditing;
using InfraAgent.Models;
using InfraAgent.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraAgent.Tests.Support;

public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InfraAgent.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    public static string Agents => Path.Combine(Root, "src", "InfraAgent", "Knowledge", "agents");
    public static string PolicyCatalog => Path.Combine(Root, "src", "InfraAgent", "Knowledge", "policies", "catalogo.yaml");
}

/// <summary>Auditoría en memoria para verificar qué se registró.</summary>
public sealed class MemoryAuditSink : IAuditSink
{
    public List<AuditEntry> Entries { get; } = new();
    public void Record(AuditEntry entry) => Entries.Add(entry);
}

/// <summary>
/// Runner de agentes falso: responde con textos programados por rol y permite que el guion
/// invoque herramientas como lo haría Foundry.
/// </summary>
public sealed class FakeAgentRunner : IAgentRunner
{
    private readonly Dictionary<AgentRole, Queue<Func<AgentRunRequest, IToolInvoker, ToolContext, Task<string>>>> _scripts = new();
    private int _threadCounter;

    public List<AgentRunRequest> Requests { get; } = new();
    public List<string> DeletedThreads { get; } = new();

    public FakeAgentRunner Script(AgentRole role, params string[] responses)
    {
        foreach (var r in responses) Script(role, (_, _, _) => Task.FromResult(r));
        return this;
    }

    public FakeAgentRunner Script(AgentRole role, Func<AgentRunRequest, IToolInvoker, ToolContext, Task<string>> handler)
    {
        if (!_scripts.TryGetValue(role, out var queue)) _scripts[role] = queue = new();
        queue.Enqueue(handler);
        return this;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, IToolInvoker tools, ToolContext context, CancellationToken ct)
    {
        Requests.Add(request);
        if (!_scripts.TryGetValue(request.Role, out var queue) || queue.Count == 0)
            throw new InvalidOperationException($"Sin respuesta programada para {request.Role}.");

        var work = queue.Dequeue()(request, tools, context);
        var cancelled = Task.Delay(Timeout.Infinite, ct);
        if (await Task.WhenAny(work, cancelled) == cancelled) throw new OperationCanceledException(ct);

        var text = await work;
        return new AgentRunResult(text, request.ThreadId ?? $"thread_{++_threadCounter:D4}", 0);
    }

    public Task DeleteThreadAsync(string threadId, CancellationToken ct)
    {
        DeletedThreads.Add(threadId);
        return Task.CompletedTask;
    }

    public IEnumerable<AgentRunRequest> For(AgentRole role) => Requests.Where(r => r.Role == role);
}

public sealed class FakeRepositoryReader : IRepositoryReader
{
    private readonly Dictionary<string, string> _files;
    public FakeRepositoryReader(Dictionary<string, string> files) => _files = files;

    public string Description => "fake/repo@main";

    public Task<IReadOnlyList<string>> ListFilesAsync(string? pathPrefix, int maxItems, CancellationToken ct)
    {
        IEnumerable<string> paths = _files.Keys;
        if (!string.IsNullOrEmpty(pathPrefix)) paths = paths.Where(p => p.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<string>>(paths.Take(maxItems).ToList());
    }

    public Task<RepoFile?> GetFileAsync(string path, int maxBytes, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(path, out var c) ? new RepoFile(path, c, false) : null);
}

public sealed class FakeRepositoryFactory : IRepositoryReaderFactory
{
    public Dictionary<string, string> Files { get; } = new();
    public List<ProjectSettings> Projects { get; } = new();

    public RepositoryProvider Provider => RepositoryProvider.Local;

    public IRepositoryReader Create(ProjectSettings project)
    {
        Projects.Add(project);
        return new FakeRepositoryReader(Files);
    }
}
/// <summary>Construye el contenedor real de Application con los puertos externos reemplazados por fakes.</summary>
public sealed class TestHost
{
    public FakeAgentRunner Runner { get; } = new();
    public FakeRepositoryFactory Repositories { get; } = new();
    public MemoryAuditSink Audit { get; } = new();
    public InfraAgentSettings Settings { get; } = DefaultSettings();
    public IServiceProvider Services { get; private set; } = null!;

    public static InfraAgentSettings DefaultSettings() => new()
    {
        Foundry = { Endpoint = "https://demo.services.ai.azure.com/api/projects/demo", DefaultAgentId = "asst_demo" },
        Repositories = { Provider = RepositoryProvider.Local },
        Knowledge = { AgentsDirectory = RepoPaths.Agents, PolicyCatalogPath = RepoPaths.PolicyCatalog },
        Projects =
        {
            new ProjectSettings { Id = "demo", DisplayName = "Demo", Path = "demo", Branch = "main" }
        }
    };

    public TestHost Build(Action<InfraAgentSettings>? configure = null)
    {
        configure?.Invoke(Settings);

        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Settings));
        services.AddSingleton(Settings.Knowledge);
        services.AddSingleton<IAgentRunner>(Runner);
        services.AddSingleton<IRepositoryReaderFactory>(Repositories);
        services.AddSingleton<IAuditSink>(Audit);
        services.AddLogging();
        services.AddInfraAgentApplication();
        Services = services.BuildServiceProvider();
        return this;
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
}

/// <summary>Generadores de respuestas JSON de los agentes.</summary>
public static class Json
{
    public static string Architecture(string summary = "App con storage", string type = "azurerm_storage_account") => $$"""
        {"summary":"{{summary}}","components":[{"id":"c1","name":"Datos","type":"{{type}}","exposure":"public"}],
         "flows":[],"assumptions":[],"openQuestions":[]}
        """;

    public static string Finding(string policyId, string file, string snippet, int line = 1, string severity = "Low") => $$"""
        {"policyId":"{{policyId}}","title":"Hallazgo {{policyId}}","severity":"{{severity}}","description":"d","recommendation":"r",
         "resource":"azurerm_storage_account.datos",
         "evidence":{"file":"{{file}}","startLine":{{line}},"snippet":"{{snippet}}"} }
        """;

    public static string Review(string summary, params string[] findings) =>
        "{\"summary\":\"" + summary + "\",\"findings\":[" + string.Join(",", findings) + "],\"compliance\":[]}";

    public static string Generator(string path, string content, params string[] policies) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            summary = "Generado",
            files = new[] { new { path, content } },
            policiesApplied = policies,
            notes = Array.Empty<string>()
        });
}
