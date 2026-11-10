using System.Text;
using System.Text.Json;
using InfraAgent.Application.Abstractions;
using InfraAgent.Application.Knowledge;
using InfraAgent.Core.Models;
using InfraAgent.Core.Policies;
using InfraAgent.Core.Settings;
using InfraAgent.Core.Validation;
using Microsoft.Extensions.Options;

namespace InfraAgent.Application.Tools;

internal static class ArgReader
{
    public static string? String(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static bool Bool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static IReadOnlyList<string> StringArray(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
    }
}

/// <summary>Carga el contenido de una skill (solo texto) o uno de sus archivos de referencia.</summary>
public sealed class LoadSkillTool : IAgentTool
{
    private readonly AgentCatalog _agents;
    public LoadSkillTool(AgentCatalog agents) => _agents = agents;

    public string Name => ToolNames.LoadSkill;
    public string Description => "Carga las instrucciones de una skill del agente (o un archivo de references/ de esa skill). Solo devuelve texto.";
    public string ParametersJsonSchema => """
        {"type":"object","properties":{
          "name":{"type":"string","description":"Nombre de la skill."},
          "resource":{"type":"string","description":"Opcional: nombre de un archivo .md dentro de references/."}},
         "required":["name"]}
        """;

    public Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext context, CancellationToken ct)
    {
        var name = InputValidators.SkillName(ArgReader.String(args, "name"));
        var skill = _agents.Get(caller).Skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.Ordinal))
                    ?? throw new ArgumentException($"La skill '{name}' no existe para este agente.");

        var resource = ArgReader.String(args, "resource");
        if (string.IsNullOrWhiteSpace(resource))
        {
            var sb = new StringBuilder(skill.Content);
            if (skill.References.Count > 0)
            {
                sb.AppendLine().AppendLine().AppendLine("Archivos de referencia disponibles: " + string.Join(", ", skill.References.Keys));
            }
            return Task.FromResult(sb.ToString());
        }

        var file = InputValidators.RelativePath(resource, "resource");
        if (!skill.References.TryGetValue(file, out var content))
            throw new ArgumentException($"La referencia '{file}' no existe en la skill '{name}'.");
        return Task.FromResult(content);
    }
}

/// <summary>Consulta del catálogo de políticas del banco (datos versionados).</summary>
public sealed class GetPoliciesTool : IAgentTool
{
    private const int MaxPolicies = 30;
    private readonly PolicyCatalog _catalog;
    public GetPoliciesTool(PolicyCatalog catalog) => _catalog = catalog;

    public string Name => ToolNames.GetPolicies;
    public string Description => "Consulta el catálogo de políticas del banco por ids, categoría o tipo de recurso Terraform. Devuelve regla, severidad y remediación.";
    public string ParametersJsonSchema => """
        {"type":"object","properties":{
          "ids":{"type":"array","items":{"type":"string"},"description":"Ids de política, p. ej. SEC-001."},
          "categoria":{"type":"string","description":"Categoría: security, network, identity, data, naming, tagging, regions, monitoring..."},
          "tipoRecurso":{"type":"string","description":"Tipo de recurso Terraform, p. ej. azurerm_storage_account."},
          "incluirEjemplos":{"type":"boolean","description":"Incluir ejemplo de código conforme."}}}
        """;

    public Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext context, CancellationToken ct)
    {
        IEnumerable<Policy> selected = _catalog.Policies;

        var ids = ArgReader.StringArray(args, "ids");
        if (ids.Count > 0)
        {
            var normalized = ids.Select(InputValidators.PolicyId).ToList();
            selected = normalized.Select(_catalog.Get).Where(p => p is not null).Cast<Policy>().ToList();
        }

        var category = ArgReader.String(args, "categoria");
        if (!string.IsNullOrWhiteSpace(category))
            selected = selected.Where(p => p.Category.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase));

        var type = ArgReader.String(args, "tipoRecurso");
        if (!string.IsNullOrWhiteSpace(type))
        {
            var applicable = _catalog.ApplicableTo(new[] { type }).Select(p => p.Id).ToHashSet();
            selected = selected.Where(p => applicable.Contains(p.Id));
        }

        var list = selected.Take(MaxPolicies + 1).ToList();
        var truncated = list.Count > MaxPolicies;
        var text = _catalog.Render(list.Take(MaxPolicies), ArgReader.Bool(args, "incluirEjemplos"));
        return Task.FromResult(list.Count == 0 ? "No hay políticas que coincidan." : truncated ? text + "\n[TRUNCADO: refine la consulta]" : text);
    }
}

public sealed class GetFileTool : IAgentTool
{
    private readonly IOptions<InfraAgentSettings> _settings;
    public GetFileTool(IOptions<InfraAgentSettings> settings) => _settings = settings;

    public string Name => ToolNames.GetFile;
    public string Description => "Lee un archivo del repositorio del proyecto activo (solo lectura, solo extensiones permitidas por configuración). No se puede elegir rama ni repositorio.";
    public string ParametersJsonSchema => """
        {"type":"object","properties":{"path":{"type":"string","description":"Ruta relativa del archivo."}},"required":["path"]}
        """;

    public async Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext context, CancellationToken ct)
    {
        if (context.Repository is null) return "No hay un repositorio asociado a esta consulta.";
        var path = InputValidators.RelativePath(ArgReader.String(args, "path"));
        var readable = _settings.Value.Analysis.ReadableExtensionList;
        if (!InputValidators.HasExtension(path, readable))
            throw new ArgumentException($"Solo se pueden leer archivos con estas extensiones: {string.Join(", ", readable)}.");
        var file = await context.Repository.GetFileAsync(path, _settings.Value.Analysis.MaxFileBytes, ct);
        if (file is null) return $"El archivo '{path}' no existe.";
        return file.Truncated ? file.Content + "\n[TRUNCADO]" : file.Content;
    }
}

public sealed class GetRepoTreeTool : IAgentTool
{
    private readonly IOptions<InfraAgentSettings> _settings;
    public GetRepoTreeTool(IOptions<InfraAgentSettings> settings) => _settings = settings;

    public string Name => ToolNames.GetRepoTree;
    public string Description => "Lista los archivos legibles del repositorio del proyecto activo, opcionalmente bajo una carpeta.";
    public string ParametersJsonSchema => """
        {"type":"object","properties":{"carpeta":{"type":"string","description":"Carpeta opcional (ruta relativa)."}}}
        """;

    public async Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext context, CancellationToken ct)
    {
        if (context.Repository is null) return "No hay un repositorio asociado a esta consulta.";

        var folder = ArgReader.String(args, "carpeta");
        var prefix = string.IsNullOrWhiteSpace(folder) ? null : InputValidators.RelativePath(folder, "carpeta");
        var max = _settings.Value.Analysis.MaxTreeItems;

        var readable = _settings.Value.Analysis.ReadableExtensionList;
        var files = (await context.Repository.ListFilesAsync(prefix, max * 4 + 1, ct))
            .Where(f => InputValidators.HasExtension(f, readable))
            .ToList();
        var text = string.Join('\n', files.Take(max));
        return files.Count > max ? text + "\n[TRUNCADO]" : text;
    }
}
