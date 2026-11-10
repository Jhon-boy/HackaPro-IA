using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using InfraAgent.Abstractions;
using InfraAgent.Common;
using InfraAgent.Settings;
using Microsoft.Extensions.Options;

namespace InfraAgent.AzureDevOps;

/// <summary>
/// Cliente REST de Azure DevOps de SOLO LECTURA (listar y leer archivos de un repositorio).
/// Autenticación: PAT si está configurado; si no, identidad administrada (Entra ID).
/// </summary>
public sealed class AzureDevOpsClient
{
    // Id de recurso fijo de Azure DevOps para tokens de Entra ID.
    private const string AzureDevOpsScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly AzureDevOpsSettings _settings;

    public AzureDevOpsClient(HttpClient http, TokenCredential credential, IOptions<InfraAgentSettings> settings)
    {
        _http = http;
        _credential = credential;
        _settings = settings.Value.AzureDevOps;
        _http.BaseAddress = new Uri(_settings.OrganizationUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }

    public IRepositoryReader Create(string project, string repository, RepoVersion version) =>
        new AzureDevOpsRepositoryReader(this, project, repository, version);

    internal async Task<IReadOnlyList<string>> ListFilesAsync(string project, string repository, RepoVersion version, string? prefix, int max, CancellationToken ct)
    {
        var scope = "/" + (prefix ?? "").Trim('/');
        var query = $"scopePath={Uri.EscapeDataString(scope)}&recursionLevel=Full&{VersionQuery(version)}";
        using var doc = await GetJsonAsync($"{RepoPath(project, repository)}/items", query, ct);
        if (doc is null) return Array.Empty<string>();

        var files = new List<string>();
        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            if (item.TryGetProperty("isFolder", out var f) && f.ValueKind == JsonValueKind.True) continue;
            var path = item.GetProperty("path").GetString();
            if (!string.IsNullOrEmpty(path)) files.Add(path.TrimStart('/'));
            if (files.Count >= max) break;
        }
        return files;
    }

    internal async Task<RepoFile?> GetFileAsync(string project, string repository, RepoVersion version, string path, int maxBytes, CancellationToken ct)
    {
        var query = $"path={Uri.EscapeDataString("/" + path)}&{VersionQuery(version)}&includeContent=true&$format=text";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RepoPath(project, repository)}/items?{query}&api-version={_settings.ApiVersion}");
        using var response = await SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(ct);
        var (limited, truncated) = TextLimits.TruncateBytes(text, maxBytes);
        return new RepoFile(path, limited, truncated);
    }

    // ---- HTTP ----

    private static string RepoPath(string project, string repository) =>
        $"{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repository)}";

    private static string VersionQuery(RepoVersion v) =>
        $"versionDescriptor.version={Uri.EscapeDataString(v.Value)}&versionDescriptor.versionType={(v.Kind == RepoVersionKind.Commit ? "commit" : "branch")}";

    private async Task<JsonDocument?> GetJsonAsync(string path, string? query, CancellationToken ct)
    {
        var url = $"{path}?{(string.IsNullOrEmpty(query) ? "" : query + "&")}api-version={_settings.ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Reintentos acotados para 429 y 5xx (solo GET: es seguro repetirlos).
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            using var attemptRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            await ApplyAuthAsync(attemptRequest, ct);
            var response = await _http.SendAsync(attemptRequest, HttpCompletionOption.ResponseContentRead, ct);

            var transient = response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500;
            if (!transient || attempt == maxAttempts) return response;

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), ct);
        }
    }

    private async Task ApplyAuthAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_settings.Pat))
        {
            var raw = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + _settings.Pat));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }
        else
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { AzureDevOpsScope }), ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
    }
}

/// <summary>Lector de solo lectura fijado a un proyecto, repositorio y versión (commit o rama).</summary>
internal sealed class AzureDevOpsRepositoryReader : IRepositoryReader
{
    private readonly AzureDevOpsClient _client;
    private readonly string _project;
    private readonly string _repository;
    private readonly RepoVersion _version;

    public AzureDevOpsRepositoryReader(AzureDevOpsClient client, string project, string repository, RepoVersion version)
    {
        _client = client;
        _project = project;
        _repository = repository;
        _version = version;
    }

    public string Description =>
        $"{_project}/{_repository}@{(_version.Kind == RepoVersionKind.Commit ? _version.Value[..Math.Min(8, _version.Value.Length)] : _version.Value)}";

    public Task<IReadOnlyList<string>> ListFilesAsync(string? pathPrefix, int maxItems, CancellationToken ct) =>
        _client.ListFilesAsync(_project, _repository, _version, pathPrefix, maxItems, ct);

    public Task<RepoFile?> GetFileAsync(string path, int maxBytes, CancellationToken ct) =>
        _client.GetFileAsync(_project, _repository, _version, path, maxBytes, ct);
}
