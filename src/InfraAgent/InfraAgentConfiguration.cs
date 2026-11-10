using Microsoft.Extensions.Configuration;

namespace InfraAgent;

/// <summary>
/// Punto ÚNICO donde se arma la configuración de InfraAgent.
/// Precedencia (menor → mayor):
///   1. config/infraagent.settings.json   (valores no secretos; se publica junto a la Function)
///   2. Variables de entorno / App Settings de la Function  (InfraAgent__Foundry__Endpoint)
///   3. Referencias a Key Vault en App Settings (@Microsoft.KeyVault(...)) — para los secretos
/// Otra ruta para el archivo JSON: variable INFRAAGENT_CONFIG.
/// </summary>
public static class InfraAgentConfiguration
{
    public const string ConfigPathVariable = "INFRAAGENT_CONFIG";
    public const string DefaultRelativePath = "config/infraagent.settings.json";

    public static IConfigurationBuilder AddInfraAgentConfiguration(this IConfigurationBuilder builder, string? baseDirectory = null)
    {
        var baseDir = baseDirectory ?? AppContext.BaseDirectory;
        var overridePath = Environment.GetEnvironmentVariable(ConfigPathVariable);
        var path = string.IsNullOrWhiteSpace(overridePath) ? Path.Combine(baseDir, DefaultRelativePath) : overridePath;

        builder.AddJsonFile(path, optional: true, reloadOnChange: false);

        // Se vuelve a agregar al final para que las variables de entorno SIEMPRE ganen al archivo JSON.
        builder.AddEnvironmentVariables();
        return builder;
    }
}
