namespace InfraAgent.Models;

/// <summary>Modelo estructurado de una arquitectura (salida del agente de Arquitectura).</summary>
public sealed class ArchitectureModel
{
    public string Summary { get; set; } = "";
    public List<ArchitectureComponent> Components { get; set; } = new();
    public List<ArchitectureFlow> Flows { get; set; } = new();

    /// <summary>Supuestos que el agente tuvo que hacer.</summary>
    public List<string> Assumptions { get; set; } = new();

    /// <summary>Preguntas o información faltante para poder cerrar el diseño.</summary>
    public List<string> OpenQuestions { get; set; } = new();
}

public sealed class ArchitectureComponent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Tipo de recurso Azure o Terraform (p. ej. azurerm_storage_account, "Azure SQL").</summary>
    public string Type { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>public | private | internal | unknown</summary>
    public string Exposure { get; set; } = "unknown";

    /// <summary>Clasificación de datos si se conoce (pública, interna, confidencial...).</summary>
    public string? DataClassification { get; set; }
}

public sealed class ArchitectureFlow
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? Protocol { get; set; }
    public string? Description { get; set; }
}

/// <summary>Archivo de Terraform generado por el agente.</summary>
public sealed class GeneratedFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>Salida del agente generador.</summary>
public sealed class GeneratorOutput
{
    public string Summary { get; set; } = "";
    public List<GeneratedFile> Files { get; set; } = new();

    /// <summary>Políticas que el diseño consideró (ids).</summary>
    public List<string> PoliciesApplied { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}
