using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfraAgent.Application.Agents;

/// <summary>Serialización común y extracción tolerante de JSON desde la respuesta del modelo.</summary>
public static class JsonOutput
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Extrae el primer objeto JSON de un texto (tolera cercos ```json y texto alrededor).</summary>
    public static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new JsonException("La respuesta no contiene un objeto JSON.");
        return text[start..(end + 1)];
    }

    public static T Parse<T>(string text)
    {
        var json = ExtractJsonObject(text);
        var value = JsonSerializer.Deserialize<T>(json, Options);
        return value ?? throw new JsonException("El JSON está vacío.");
    }
}
