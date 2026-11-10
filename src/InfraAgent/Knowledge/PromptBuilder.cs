using System.Text;
using InfraAgent.Models;
using InfraAgent.Policies;
using InfraAgent.Settings;
using Microsoft.Extensions.Options;

namespace InfraAgent.Knowledge;

/// <summary>Compone las instrucciones de un agente: reglas compartidas + rol + índice de skills.</summary>
public sealed class PromptBuilder
{
    private readonly AgentCatalog _agents;
    private readonly PolicyCatalog _policies;
    private readonly KnowledgeSettings _knowledge;
    private readonly ChatSettings _chat;

    public PromptBuilder(AgentCatalog agents, PolicyCatalog policies, IOptions<InfraAgentSettings> settings)
    {
        _agents = agents;
        _policies = policies;
        _knowledge = settings.Value.Knowledge;
        _chat = settings.Value.Chat;
    }

    public string BuildInstructions(AgentRole role)
    {
        var def = _agents.Get(role);
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(_agents.SharedRules))
        {
            sb.AppendLine(_agents.SharedRules.Trim());
            sb.AppendLine();
        }

        sb.AppendLine(def.Instructions.Trim());

        if (def.Skills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Skills disponibles");
            sb.AppendLine("Carga una skill con la herramienta `load_skill` cuando la tarea coincida con su descripción.");
            foreach (var s in def.Skills)
                sb.AppendLine($"- `{s.Name}`: {s.Description}");
        }

        sb.AppendLine();
        sb.AppendLine($"(Organización dueña de las políticas: {_knowledge.OrganizationName}. Idioma de respuesta: {_chat.ResponseLanguage}. " +
                      $"Catálogo de políticas vigente: v{_policies.Version}.)");
        return sb.ToString();
    }
}
