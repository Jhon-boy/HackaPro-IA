using InfraAgent.Application.Chat;
using InfraAgent.Application.Knowledge;
using InfraAgent.Application.Projects;
using InfraAgent.Core.Models;
using InfraAgent.Core.Policies;
using InfraAgent.Core.Settings;
using InfraAgent.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Consola local con los mismos servicios que la Function.
//   dotnet run --project src/InfraAgent.Cli -- status
//   dotnet run --project src/InfraAgent.Cli -- policies
//   dotnet run --project src/InfraAgent.Cli -- projects
//   dotnet run --project src/InfraAgent.Cli -- chat [--project <id>] [--image <ruta>]... [--out <carpeta>]
// Secretos locales: dotnet user-secrets --project src/InfraAgent.Cli set "InfraAgent:Foundry:DefaultAgentId" "<valor>"

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddInfraAgentConfiguration();
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddInfraAgent(builder.Configuration);

using var host = builder.Build();
var sp = host.Services;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

try
{
    switch (command)
    {
        case "status": return Status();
        case "policies": return Policies();
        case "projects": return Projects();
        case "chat": return await ChatAsync(args.Skip(1).ToArray());
        default:
            Console.WriteLine("Comandos: status | policies | projects | chat [--project id] [--image ruta]... [--out carpeta]");
            return 0;
    }
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine("Configuración inválida:");
    foreach (var f in ex.Failures) Console.Error.WriteLine($"  - {f}");
    return 2;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Solicitud inválida: {ex.Message}");
    return 2;
}

int Status()
{
    var settings = sp.GetRequiredService<IOptions<InfraAgentSettings>>().Value;   // valida al leer
    Console.WriteLine("Configuración: OK");
    foreach (var (name, present) in InfraAgentSettingsValidator.Presence(settings))
        Console.WriteLine($"  {(present ? "✔" : "·")} {name}");

    Console.WriteLine($"Repositorios: {settings.Repositories.Provider}");
    var f = settings.Features;
    Console.WriteLine($"Capacidades: imágenes={f.Images}, repositorio={f.RepositoryAccess}, arquitectura={f.ArchitectureAnalysis}, " +
                      $"políticas={f.PolicyEvaluation}, terraform={f.TerraformGeneration}, seguridad={f.SecurityReview}");
    Console.WriteLine($"Generación: región={settings.Generation.DefaultRegion}, nombres={settings.Generation.NamingPattern}, " +
                      $"autorrevisión={settings.Generation.AutoSecurityReview}, rondas={settings.Generation.MaxFixRounds}");
    Console.WriteLine($"Conocimiento de agentes: {sp.GetRequiredService<AgentCatalog>().Version}");
    var catalog = sp.GetRequiredService<PolicyCatalog>();
    Console.WriteLine($"Catálogo de políticas: v{catalog.Version} ({catalog.Policies.Count} políticas)");
    return 0;
}

int Policies()
{
    var catalog = sp.GetRequiredService<PolicyCatalog>();
    Console.WriteLine($"Catálogo v{catalog.Version}");
    foreach (var p in catalog.Policies)
        Console.WriteLine($"  {p.Id,-8} {p.Severity,-9} {p.Category,-11} {p.Title}");
    return 0;
}

int Projects()
{
    var projects = sp.GetRequiredService<ProjectCatalog>().All;
    if (projects.Count == 0) Console.WriteLine("No hay proyectos habilitados (revise Repositories:Provider y Projects).");
    foreach (var p in projects) Console.WriteLine($"  {p.Id,-20} {p.DisplayName}");
    return 0;
}

async Task<int> ChatAsync(string[] opts)
{
    var chat = sp.GetRequiredService<ChatService>();
    string? projectId = Option(opts, "--project");
    var outDir = Option(opts, "--out") ?? Path.Combine(Environment.CurrentDirectory, "salida-terraform");
    var imagePaths = Options(opts, "--image");

    var images = imagePaths.Select(p => new ChatImage
    {
        MimeType = MimeFor(p),
        Base64 = Convert.ToBase64String(File.ReadAllBytes(p)),
        Name = Path.GetFileName(p)
    }).ToList();

    string? conversationId = null;
    ArchitectureModel? architecture = null;
    var files = new List<GeneratedFile>();

    Console.WriteLine("Chat con InfraAgent. Escribe 'salir' para terminar." +
                      (images.Count > 0 ? $" ({images.Count} imagen(es) se adjuntarán al primer mensaje)" : ""));

    while (true)
    {
        Console.Write("\ntú> ");
        var line = Console.ReadLine();
        if (line is null || line.Trim().Equals("salir", StringComparison.OrdinalIgnoreCase)) break;
        if (string.IsNullOrWhiteSpace(line)) continue;

        try
        {
            var response = await chat.HandleAsync(new ChatRequest
            {
                ConversationId = conversationId,
                Message = line,
                ProjectId = projectId,
                Images = images,
                Architecture = architecture,
                Files = files
            }, CancellationToken.None);

            images = new();   // solo se adjuntan en el primer mensaje
            conversationId = response.ConversationId;
            architecture = response.Architecture;
            files = response.Files;

            Console.WriteLine($"\nagente> {response.Reply}");
            if (response.ToolsUsed.Count > 0) Console.WriteLine($"  [herramientas: {string.Join(", ", response.ToolsUsed)}]");

            if (response.Files.Count > 0)
            {
                Directory.CreateDirectory(outDir);
                foreach (var f in response.Files)
                {
                    var target = Path.GetFullPath(Path.Combine(outDir, f.Path));
                    if (!target.StartsWith(Path.GetFullPath(outDir), StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, f.Content);
                }
                Console.WriteLine($"  [{response.Files.Count} archivo(s) guardados en {outDir}]");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or TimeoutException)
        {
            Console.WriteLine($"  ! {ex.Message}");
        }
    }

    if (conversationId is not null) await chat.DeleteConversationAsync(conversationId, CancellationToken.None);
    return 0;
}

static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".png" => "image/png",
    ".webp" => "image/webp",
    ".gif" => "image/gif",
    _ => "image/jpeg"
};

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static List<string> Options(string[] args, string name)
{
    var result = new List<string>();
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) result.Add(args[i + 1]);
    return result;
}
