namespace InfraAgent.Models;

/// <summary>Responsabilidades del sistema. Cada una tiene su agente (instrucciones, skills y herramientas propias).</summary>
public enum AgentRole
{
    Orchestrator,
    Architecture,
    Policy,
    IacGenerator,
    Security
}

public static class AgentRoleExtensions
{
    /// <summary>Nombre de carpeta en /agents (en español, estable).</summary>
    public static string FolderName(this AgentRole role) => role switch
    {
        AgentRole.Orchestrator => "orquestador",
        AgentRole.Architecture => "arquitectura",
        AgentRole.Policy => "politicas",
        AgentRole.IacGenerator => "generador-iac",
        AgentRole.Security => "seguridad",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static string DisplayName(this AgentRole role) => role switch
    {
        AgentRole.Orchestrator => "Orquestador",
        AgentRole.Architecture => "Arquitectura",
        AgentRole.Policy => "Políticas",
        AgentRole.IacGenerator => "Generador IaC",
        AgentRole.Security => "Seguridad",
        _ => role.ToString()
    };
}
