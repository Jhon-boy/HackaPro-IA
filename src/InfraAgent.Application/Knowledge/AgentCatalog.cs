using System.Security.Cryptography;
using System.Text;
using InfraAgent.Core.Models;
using InfraAgent.Core.Validation;

namespace InfraAgent.Application.Knowledge;

public sealed record SkillInfo(
    string Name,
    string Description,
    string Content,
    IReadOnlyDictionary<string, string> References);

public sealed class AgentDefinition
{
    public required AgentRole Role { get; init; }
    public required string Instructions { get; init; }
    public IReadOnlyList<SkillInfo> Skills { get; init; } = Array.Empty<SkillInfo>();
}

/// <summary>
/// Conocimiento de los agentes cargado desde el repositorio (carpeta /agents):
/// instrucciones, skills y reglas compartidas. Es la fuente de verdad (Foundry solo ejecuta).
/// </summary>
public sealed class AgentCatalog
{
    private readonly Dictionary<AgentRole, AgentDefinition> _agents;

    public AgentCatalog(string sharedRules, IEnumerable<AgentDefinition> agents, string version)
    {
        SharedRules = sharedRules;
        _agents = agents.ToDictionary(a => a.Role);
        Version = version;
    }

    public string SharedRules { get; }

    /// <summary>Huella corta del contenido (para auditoría en los reportes).</summary>
    public string Version { get; }

    public AgentDefinition Get(AgentRole role) =>
        _agents.TryGetValue(role, out var a) ? a : throw new InvalidOperationException($"No hay definición para el agente {role}.");

    public static AgentCatalog Load(string agentsDirectory)
    {
        if (!Directory.Exists(agentsDirectory))
            throw new DirectoryNotFoundException($"No se encontró la carpeta de agentes: {agentsDirectory}");

        var hash = SHA256.Create();
        var hashInput = new StringBuilder();

        var sharedPath = Path.Combine(agentsDirectory, "_compartido", "reglas.md");
        var shared = File.Exists(sharedPath) ? File.ReadAllText(sharedPath) : "";
        hashInput.Append(shared);

        var agents = new List<AgentDefinition>();
        foreach (var role in Enum.GetValues<AgentRole>())
        {
            var dir = Path.Combine(agentsDirectory, role.FolderName());
            var instructionsPath = Path.Combine(dir, "instructions.md");
            if (!File.Exists(instructionsPath))
                throw new FileNotFoundException($"Falta instructions.md del agente {role.DisplayName()}.", instructionsPath);

            var instructions = File.ReadAllText(instructionsPath);
            hashInput.Append(instructions);

            var skills = new List<SkillInfo>();
            var skillsDir = Path.Combine(dir, "skills");
            if (Directory.Exists(skillsDir))
            {
                foreach (var skillDir in Directory.GetDirectories(skillsDir).OrderBy(d => d, StringComparer.Ordinal))
                {
                    var skill = LoadSkill(skillDir);
                    hashInput.Append(skill.Content);
                    foreach (var r in skill.References.OrderBy(k => k.Key, StringComparer.Ordinal)) hashInput.Append(r.Value);
                    skills.Add(skill);
                }
            }

            agents.Add(new AgentDefinition { Role = role, Instructions = instructions, Skills = skills });
        }

        var digest = Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(hashInput.ToString())))[..10].ToLowerInvariant();
        return new AgentCatalog(shared, agents, digest);
    }

    private static SkillInfo LoadSkill(string skillDir)
    {
        var folderName = Path.GetFileName(skillDir);
        var skillFile = Path.Combine(skillDir, "SKILL.md");
        if (!File.Exists(skillFile))
            throw new FileNotFoundException($"La skill '{folderName}' no tiene SKILL.md.", skillFile);

        var raw = File.ReadAllText(skillFile);
        var (name, description, body) = ParseFrontMatter(raw);
        if (!string.Equals(name, folderName, StringComparison.Ordinal))
            throw new InvalidDataException($"La skill en '{folderName}' declara name='{name}'; debe coincidir con la carpeta.");
        InputValidators.SkillName(name);

        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var refDir = Path.Combine(skillDir, "references");
        if (Directory.Exists(refDir))
        {
            foreach (var file in Directory.GetFiles(refDir, "*.md"))
                references[Path.GetFileName(file)] = File.ReadAllText(file);
        }

        return new SkillInfo(name, description, body, references);
    }

    internal static (string Name, string Description, string Body) ParseFrontMatter(string raw)
    {
        var text = raw.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
            throw new InvalidDataException("SKILL.md debe iniciar con front matter (---).");

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("SKILL.md: front matter sin cierre.");

        string? name = null, description = null;
        foreach (var line in text[4..end].Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"');
            if (key == "name") name = value;
            else if (key == "description") description = value;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
            throw new InvalidDataException("SKILL.md requiere 'name' y 'description' en el front matter.");

        var body = text[(end + 4)..].TrimStart('\n');
        return (name, description, body);
    }
}
