using System.Text.Json;
using InfraAgent.Core.Models;

namespace InfraAgent.Application.Abstractions;

public sealed record ImageInput(string MimeType, byte[] Data, string? Name = null);

/// <summary>Definición de una herramienta que se publica al modelo.</summary>
public sealed record ToolSpec(string Name, string Description, string ParametersJsonSchema);

/// <summary>Herramienta ejecutada en NUESTRO código (nunca en Foundry). Solo lectura, salvo notas explícitas.</summary>
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON Schema de los parámetros (type: object).</summary>
    string ParametersJsonSchema { get; }

    Task<string> InvokeAsync(AgentRole caller, JsonElement arguments, ToolContext context, CancellationToken ct);
}

/// <summary>Estado compartido de una solicitud (chat o revisión). El modelo nunca elige repositorio ni rama.</summary>
public sealed class ToolContext
{
    public required string CorrelationId { get; init; }

    /// <summary>Lector de repositorio ya acotado (proyecto + rama/commit). Null si no hay repositorio.</summary>
    public IRepositoryReader? Repository { get; init; }

    public string? ProjectId { get; init; }
    public IReadOnlyList<ImageInput> Images { get; init; } = Array.Empty<ImageInput>();

    public ArchitectureModel? Architecture { get; set; }

    /// <summary>Archivos Terraform del chat (previos + generados en este turno).</summary>
    public List<GeneratedFile> Artifacts { get; } = new();

    public List<string> ToolsUsed { get; } = new();
}

public interface IToolInvoker
{
    IReadOnlyList<ToolSpec> GetSpecs(AgentRole role);
    Task<string> InvokeAsync(AgentRole role, string toolName, string argumentsJson, ToolContext context, CancellationToken ct);
}

public sealed record AgentRunRequest(
    AgentRole Role,
    string Instructions,
    string UserMessage,
    IReadOnlyList<ImageInput> Images,
    string? ThreadId,
    TimeSpan Timeout);

public sealed record AgentRunResult(string Text, string ThreadId, int ToolCalls);

/// <summary>Puerto hacia el agente de Foundry. Implementado en Infrastructure.</summary>
public interface IAgentRunner
{
    Task<AgentRunResult> RunAsync(AgentRunRequest request, IToolInvoker tools, ToolContext context, CancellationToken ct);

    /// <summary>Elimina un hilo (best effort). Se usa con los hilos efímeros de los especialistas y al borrar una conversación.</summary>
    Task DeleteThreadAsync(string threadId, CancellationToken ct);
}

public sealed record RepoFile(string Path, string Content, bool Truncated);

/// <summary>Lector de repositorio de SOLO LECTURA ya fijado a un proyecto y a una versión (commit/rama).</summary>
public interface IRepositoryReader
{
    string Description { get; }
    Task<IReadOnlyList<string>> ListFilesAsync(string? pathPrefix, int maxItems, CancellationToken ct);
    Task<RepoFile?> GetFileAsync(string path, int maxBytes, CancellationToken ct);
}

public sealed record RepoVersion(RepoVersionKind Kind, string Value);

public enum RepoVersionKind
{
    Branch,
    Commit
}

/// <summary>Crea el lector de repositorio de SOLO LECTURA de un proyecto habilitado, según el proveedor configurado.</summary>
public interface IRepositoryReaderFactory
{
    /// <summary>Proveedor activo. Con None no hay repositorios.</summary>
    InfraAgent.Core.Settings.RepositoryProvider Provider { get; }

    IRepositoryReader Create(InfraAgent.Core.Settings.ProjectSettings project);
}
