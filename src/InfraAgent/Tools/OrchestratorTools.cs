using System.Text.Json;
using InfraAgent.Abstractions;
using InfraAgent.Agents;
using InfraAgent.Projects;
using InfraAgent.Review;
using InfraAgent.Models;
using InfraAgent.Settings;
using InfraAgent.Terraform;
using InfraAgent.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InfraAgent.Tools;

// Las herramientas del orquestador son "agente como herramienta": cada una delega en un especialista.
// Resuelven los servicios de forma diferida (IServiceProvider) para evitar dependencias circulares
// Orquestador → herramienta → servicio → AgentService → ToolInvoker → herramientas.

public sealed class AnalyzeArchitectureTool : IAgentTool
{
    private readonly IServiceProvider _sp;
    public AnalyzeArchitectureTool(IServiceProvider sp) => _sp = sp;

    public string Name => ToolNames.AnalyzeArchitecture;
    public string Description =>
        "Invoca al agente de Arquitectura. Interpreta las imágenes adjuntas, el repositorio del proyecto activo y/o la descripción, y devuelve el modelo de arquitectura (componentes, flujos, supuestos, preguntas abiertas).";
    public string ParametersJsonSchema => """
        {"type":"object","properties":{
          "descripcion":{"type":"string","description":"Resumen de lo que el usuario describió o necesita."}}}
        """;

    public async Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var settings = _sp.GetRequiredService<IOptions<InfraAgentSettings>>().Value;
        var description = ArgReader.String(args, "descripcion");
        if (description is { Length: > 8000 }) throw new ArgumentException("descripcion demasiado larga.");

        FileBundle? bundle = ctx.Artifacts.Count > 0
            ? FileBundle.Build(ctx.Artifacts.Select(a => (a.Path, a.Content)), settings.Analysis)
            : null;

        var model = await _sp.GetRequiredService<ArchitectureService>().AnalyzeAsync(ctx, description, bundle, ct);
        ctx.Architecture = model;
        return JsonOutput.Serialize(model);
    }
}

public sealed class EvaluatePoliciesTool : IAgentTool
{
    private readonly IServiceProvider _sp;
    public EvaluatePoliciesTool(IServiceProvider sp) => _sp = sp;

    public string Name => ToolNames.EvaluatePolicies;
    public string Description =>
        "Invoca al agente de Políticas. Evalúa la arquitectura vigente (y el Terraform ya generado, si existe) contra las políticas del banco e indica qué cumple, qué no y qué ajustes requiere. Requiere haber ejecutado antes analizar_arquitectura.";
    public string ParametersJsonSchema => """{"type":"object","properties":{}}""";

    public async Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        if (ctx.Architecture is null)
            return "ERROR: no hay una arquitectura analizada. Ejecuta primero analizar_arquitectura.";

        var reviewer = _sp.GetRequiredService<ReviewerService>();
        var compliance = await reviewer.EvaluateArchitectureAsync(ctx.Architecture, ctx, ct);

        object? codeReview = null;
        if (ctx.Artifacts.Count > 0)
        {
            var settings = _sp.GetRequiredService<IOptions<InfraAgentSettings>>().Value;
            var bundle = FileBundle.Build(ctx.Artifacts.Select(a => (a.Path, a.Content)), settings.Analysis);
            var review = await reviewer.ReviewPoliciesAsync(bundle, ctx.Architecture, ctx, ct);
            codeReview = new
            {
                hallazgos = review.Findings.Select(Slim),
                descartados = review.Discarded.Count
            };
        }

        return JsonOutput.Serialize(new { cumplimiento = compliance, revisionDelCodigo = codeReview });
    }

    internal static object Slim(Finding f) => new
    {
        f.PolicyId, f.Title, f.Severity, archivo = f.Evidence.File, linea = f.Evidence.StartLine, f.Recommendation
    };
}

public sealed class GenerateTerraformTool : IAgentTool
{
    private readonly IServiceProvider _sp;
    public GenerateTerraformTool(IServiceProvider sp) => _sp = sp;

    public string Name => ToolNames.GenerateTerraform;
    public string Description =>
        "Invoca al agente Generador IaC. Genera archivos Terraform (azurerm) conformes a las políticas, los revisa automáticamente con el agente de Seguridad y los deja disponibles para el usuario. NO aplica nada en Azure.";
    public string ParametersJsonSchema => """
        {"type":"object","properties":{
          "requisitos":{"type":"string","description":"Qué recursos y configuraciones hay que crear (en el lenguaje del usuario)."}},
         "required":["requisitos"]}
        """;

    public async Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var settings = _sp.GetRequiredService<IOptions<InfraAgentSettings>>().Value;
        var requirements = InputValidators.Message(ArgReader.String(args, "requisitos"), settings.Chat.MaxMessageChars);

        var result = await _sp.GetRequiredService<TerraformGenerationService>()
            .GenerateAsync(requirements, ctx.Architecture, ctx, ct);

        // Los archivos nuevos reemplazan a los previos con la misma ruta.
        foreach (var file in result.Files)
        {
            ctx.Artifacts.RemoveAll(a => a.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
            ctx.Artifacts.Add(file);
        }

        return JsonOutput.Serialize(new
        {
            resumen = result.Summary,
            archivos = result.Files.Select(f => f.Path),
            tiposDeRecurso = TerraformInspector.ExtractResourceTypes(result.Files.Select(f => f.Content)),
            politicasAplicadas = result.PoliciesApplied,
            notas = result.Notes,
            problemasDeValidacion = result.Problems,
            rondasDeCorreccion = result.FixRounds,
            hallazgosBloqueantesPendientes = result.HasBlockingFindings,
            hallazgosDeSeguridad = result.SecurityReview?.Findings.Select(EvaluatePoliciesTool.Slim),
            nota = "Los archivos se entregan al usuario por separado; no los repitas completos en tu respuesta."
        });
    }
}

public sealed class ReviewSecurityTool : IAgentTool
{
    private readonly IServiceProvider _sp;
    public ReviewSecurityTool(IServiceProvider sp) => _sp = sp;

    public string Name => ToolNames.ReviewSecurity;
    public string Description =>
        "Invoca al agente de Seguridad. Revisa el Terraform disponible en la conversación (archivos generados o adjuntos) o, si no hay, el Terraform del repositorio del proyecto activo, y devuelve hallazgos con evidencia verificada.";
    public string ParametersJsonSchema => """{"type":"object","properties":{}}""";

    public async Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var settings = _sp.GetRequiredService<IOptions<InfraAgentSettings>>().Value;
        var sources = new List<(string Path, string Content)>();

        if (ctx.Artifacts.Count > 0)
        {
            sources.AddRange(ctx.Artifacts.Select(a => (a.Path, a.Content)));
        }
        else if (ctx.Repository is not null)
        {
            var paths = await ctx.Repository.ListFilesAsync(null, settings.Analysis.MaxTreeItems, ct);
            foreach (var path in paths.Where(p => TerraformInspector.IsTerraformPath(p, settings.Analysis.ExtensionList))
                         .Take(settings.Analysis.MaxFiles))
            {
                var file = await ctx.Repository.GetFileAsync(InputValidators.RelativePath(path), settings.Analysis.MaxFileBytes, ct);
                if (file is not null) sources.Add((path, file.Content));
            }
        }

        if (sources.Count == 0)
            return "No hay Terraform que revisar: genera código o selecciona un proyecto con archivos .tf.";

        var bundle = FileBundle.Build(sources, settings.Analysis);
        var review = await _sp.GetRequiredService<ReviewerService>().ReviewSecurityAsync(bundle, ctx.Architecture, ctx, ct);

        return JsonOutput.Serialize(new
        {
            resumen = review.Summary,
            archivosAnalizados = review.AnalyzedFiles,
            truncado = review.Truncated,
            hallazgos = review.Findings.Select(EvaluatePoliciesTool.Slim),
            hallazgosDescartadosPorFaltaDeEvidencia = review.Discarded.Count
        });
    }
}

public sealed class ListProjectsTool : IAgentTool
{
    private readonly ProjectCatalog _projects;
    public ListProjectsTool(ProjectCatalog projects) => _projects = projects;

    public string Name => ToolNames.ListProjects;
    public string Description => "Lista los proyectos habilitados (id y nombre). El usuario elige el proyecto en la solicitud; no se puede cambiar desde aquí.";
    public string ParametersJsonSchema => """{"type":"object","properties":{}}""";

    public Task<string> InvokeAsync(AgentRole caller, JsonElement args, ToolContext ctx, CancellationToken ct) =>
        Task.FromResult(JsonOutput.Serialize(_projects.All.Select(p => new { p.Id, nombre = p.DisplayName })));
}
