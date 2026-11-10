using System.Text;
using InfraAgent.Abstractions;
using InfraAgent.Agents;
using InfraAgent.Projects;
using InfraAgent.Auditing;
using InfraAgent.Models;
using InfraAgent.Security;
using InfraAgent.Settings;
using InfraAgent.Validation;
using Microsoft.Extensions.Options;

namespace InfraAgent.Chat;

public sealed class ChatImage
{
    public string MimeType { get; set; } = "";
    public string Base64 { get; set; } = "";
    public string? Name { get; set; }
}

/// <summary>
/// Solicitud de chat. El servicio es sin estado: el cliente devuelve la arquitectura y los archivos
/// del turno anterior para continuar (la conversación del orquestador vive en un hilo de Foundry).
/// </summary>
public sealed class ChatRequest
{
    public string? ConversationId { get; set; }
    public string Message { get; set; } = "";
    public string? ProjectId { get; set; }
    public List<ChatImage> Images { get; set; } = new();
    public ArchitectureModel? Architecture { get; set; }
    public List<GeneratedFile> Files { get; set; } = new();
}

public sealed class ChatResponse
{
    public string ConversationId { get; set; } = "";
    public string Reply { get; set; } = "";
    public List<GeneratedFile> Files { get; set; } = new();
    public ArchitectureModel? Architecture { get; set; }
    public List<string> ToolsUsed { get; set; } = new();
}

/// <summary>Chat con el agente orquestador (texto + imágenes de arquitectura).</summary>
public sealed class ChatService
{
    private readonly AgentService _agents;
    private readonly IAgentRunner _runner;
    private readonly ProjectCatalog _projects;
    private readonly IRepositoryReaderFactory _readers;
    private readonly IAuditSink _audit;
    private readonly InfraAgentSettings _settings;

    public ChatService(
        AgentService agents,
        IAgentRunner runner,
        ProjectCatalog projects,
        IRepositoryReaderFactory readers,
        IAuditSink audit,
        IOptions<InfraAgentSettings> settings)
    {
        _agents = agents;
        _runner = runner;
        _projects = projects;
        _readers = readers;
        _audit = audit;
        _settings = settings.Value;
    }

    public async Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct)
    {
        var chat = _settings.Chat;

        // 1) Validar todo lo que viene del usuario.
        var message = InputValidators.Message(request.Message, chat.MaxMessageChars);
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? null
            : InputValidators.ConversationId(request.ConversationId);

        var images = DecodeImages(request.Images);
        var carriedFiles = ValidateCarriedFiles(request.Files);
        var architecture = ValidateCarriedArchitecture(request.Architecture);

        ProjectSettings? project = string.IsNullOrWhiteSpace(request.ProjectId) ? null : _projects.Get(request.ProjectId);
        if (project is not null && !_settings.Features.RepositoryAccess)
            throw new ArgumentException("El acceso a repositorios está deshabilitado por configuración (Features:RepositoryAccess).");
        var reader = project is null ? null : _readers.Create(project);

        var ctx = new ToolContext
        {
            CorrelationId = $"chat-{Guid.NewGuid():N}"[..16],
            Repository = reader,
            ProjectId = project?.Id,
            Images = images,
            Architecture = architecture
        };
        ctx.Artifacts.AddRange(carriedFiles);

        // 2) Presupuesto de tiempo del chat (el balanceador HTTP corta a ~230 s).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(chat.TimeoutSeconds));

        var started = DateTimeOffset.UtcNow;
        try
        {
            var result = await _agents.RunTextAsync(
                AgentRole.Orchestrator,
                BuildUserMessage(message, project, images.Count, architecture is not null, carriedFiles.Count),
                ctx,
                images: null,           // las imágenes las interpreta el agente de Arquitectura, no el orquestador
                threadId: conversationId,
                ct: timeout.Token);

            _audit.Record(new AuditEntry(AuditKind.Chat, "chat", true, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                ctx.CorrelationId, $"herramientas={ctx.ToolsUsed.Count};archivos={ctx.Artifacts.Count}"));

            return new ChatResponse
            {
                ConversationId = result.ThreadId,
                Reply = SecretMasker.MaskText(result.Text),
                Files = ctx.Artifacts.ToList(),
                Architecture = ctx.Architecture,
                ToolsUsed = ctx.ToolsUsed.Distinct().ToList()
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _audit.Record(new AuditEntry(AuditKind.Chat, "chat", false, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                ctx.CorrelationId, "timeout"));
            throw new TimeoutException($"La consulta superó el tiempo máximo del chat ({chat.TimeoutSeconds} s). Intente con una solicitud más acotada.");
        }
    }

    public Task DeleteConversationAsync(string conversationId, CancellationToken ct) =>
        _runner.DeleteThreadAsync(InputValidators.ConversationId(conversationId), ct);

    private string BuildUserMessage(string message, ProjectSettings? project, int images, bool hasArchitecture, int files)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SecretMasker.MaskText(message));
        sb.AppendLine();
        sb.AppendLine("[Contexto de la solicitud generado por el sistema]");
        sb.AppendLine($"- Proyecto activo: {(project is null ? "ninguno" : $"{project.Id} ({project.DisplayName})")}");
        sb.AppendLine($"- Imágenes de arquitectura adjuntas en este mensaje: {images}");
        sb.AppendLine($"- Arquitectura previa disponible: {(hasArchitecture ? "sí" : "no")}");
        sb.AppendLine($"- Archivos Terraform previos en la conversación: {files}");
        sb.AppendLine($"- Idioma de respuesta: {_settings.Chat.ResponseLanguage}");

        var disabled = DisabledCapabilities();
        if (disabled.Count > 0)
            sb.AppendLine($"- Capacidades deshabilitadas por configuración (no las ofrezcas ni intentes usarlas): {string.Join(", ", disabled)}");
        return sb.ToString();
    }

    private List<string> DisabledCapabilities()
    {
        var f = _settings.Features;
        var list = new List<string>();
        if (!f.Images) list.Add("análisis de imágenes");
        if (!f.RepositoryAccess) list.Add("lectura de repositorios");
        if (!f.ArchitectureAnalysis) list.Add("análisis de arquitectura");
        if (!f.PolicyEvaluation) list.Add("evaluación de políticas");
        if (!f.TerraformGeneration) list.Add("generación de Terraform");
        if (!f.SecurityReview) list.Add("revisión de seguridad");
        return list;
    }

    private List<ImageInput> DecodeImages(List<ChatImage> images)
    {
        if (images.Count > 0 && !_settings.Features.Images)
            throw new ArgumentException("El análisis de imágenes está deshabilitado por configuración (Features:Images).");

        if (images.Count > _settings.Chat.MaxImages)
            throw new ArgumentException($"Se permiten como máximo {_settings.Chat.MaxImages} imágenes.");

        var result = new List<ImageInput>();
        foreach (var image in images)
        {
            // Rechazar antes de decodificar para no reservar memoria con entradas enormes.
            if (image.Base64.Length > (long)_settings.Chat.MaxImageBytes * 4 / 3 + 16)
                throw new ArgumentException($"La imagen supera el máximo de {_settings.Chat.MaxImageBytes / 1024} KB.");

            byte[] data;
            try { data = Convert.FromBase64String(image.Base64); }
            catch (FormatException) { throw new ArgumentException("La imagen no es Base64 válido."); }

            var mime = InputValidators.Image(image.MimeType, data, _settings.Chat);
            result.Add(new ImageInput(mime, data, string.IsNullOrWhiteSpace(image.Name) ? null : SafeName(image.Name)));
        }
        return result;
    }

    private List<GeneratedFile> ValidateCarriedFiles(List<GeneratedFile> files)
    {
        if (files.Count > _settings.Chat.MaxGeneratedFiles)
            throw new ArgumentException($"Se permiten como máximo {_settings.Chat.MaxGeneratedFiles} archivos.");

        return files.Select(f =>
        {
            if (Encoding.UTF8.GetByteCount(f.Content ?? "") > _settings.Chat.MaxGeneratedFileBytes)
                throw new ArgumentException($"El archivo {f.Path} supera el tamaño máximo.");
            return new GeneratedFile { Path = InputValidators.GeneratedFilePath(f.Path, _settings.Generation.AllowedFileExtensionList), Content = f.Content ?? "" };
        }).ToList();
    }

    private ArchitectureModel? ValidateCarriedArchitecture(ArchitectureModel? model)
    {
        if (model is null) return null;
        if (model.Components.Count > _settings.Analysis.MaxComponents || model.Flows.Count > _settings.Analysis.MaxFlows)
            throw new ArgumentException("La arquitectura enviada supera los límites permitidos.");
        return model;
    }

    private static string SafeName(string name)
    {
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return cleaned.Length == 0 ? "imagen" : cleaned[..Math.Min(cleaned.Length, 60)];
    }
}
