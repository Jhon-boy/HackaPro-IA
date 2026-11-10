using System.Net;
using System.Text;
using Azure.Core;
using InfraAgent.Abstractions;
using InfraAgent.Settings;
using InfraAgent.AzureDevOps;
using Microsoft.Extensions.Options;

namespace InfraAgent.Tests.Infrastructure;

public class AzureDevOpsClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Responses.Dequeue()(request));
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token-entra", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AzureDevOpsClient Client, StubHandler Handler) Create(string pat = "")
    {
        var handler = new StubHandler();
        var settings = new InfraAgentSettings { AzureDevOps = { OrganizationUrl = "https://dev.azure.com/banco", Pat = pat } };
        var client = new AzureDevOpsClient(new HttpClient(handler), new FakeCredential(), Options.Create(settings));
        return (client, handler);
    }

    private static IRepositoryReader Reader(AzureDevOpsClient client, RepoVersionKind kind = RepoVersionKind.Branch, string version = "main") =>
        client.Create("Mi Proyecto", "infra", new RepoVersion(kind, version));

    [Fact]
    public async Task Uses_basic_auth_with_pat_and_entra_bearer_token_without_it()
    {
        var (withPat, patHandler) = Create(pat: "mi-pat");
        patHandler.Responses.Enqueue(_ => Json("""{"value":[]}"""));
        await Reader(withPat).ListFilesAsync(null, 10, default);
        Assert.Equal("Basic", patHandler.Requests[0].Headers.Authorization!.Scheme);

        var (withEntra, entraHandler) = Create();
        entraHandler.Responses.Enqueue(_ => Json("""{"value":[]}"""));
        await Reader(withEntra).ListFilesAsync(null, 10, default);
        Assert.Equal("Bearer", entraHandler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("token-entra", entraHandler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Reads_file_at_a_commit_and_returns_null_when_missing()
    {
        var (client, handler) = Create(pat: "x");
        handler.Responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("resource {}") });
        handler.Responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var reader = client.Create("P", "r", new RepoVersion(RepoVersionKind.Commit, "abc123"));

        var found = await reader.GetFileAsync("infra/main.tf", 10_000, default);
        var missing = await reader.GetFileAsync("nope.tf", 10_000, default);

        Assert.Equal("resource {}", found!.Content);
        Assert.Null(missing);
        var url = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("path=/infra/main.tf", url);
        Assert.Contains("versionDescriptor.versionType=commit", url);
        Assert.Contains("versionDescriptor.version=abc123", url);
        Assert.Contains("api-version=7.1", url);
    }

    [Fact]
    public async Task Lists_only_files_under_the_scope_and_escapes_the_project_name()
    {
        var (client, handler) = Create(pat: "x");
        handler.Responses.Enqueue(_ => Json("""{"value":[{"path":"/","isFolder":true},{"path":"/main.tf"},{"path":"/net","isFolder":true},{"path":"/net/vnet.tf"}]}"""));

        var files = await Reader(client).ListFilesAsync(null, 50, default);

        Assert.Equal(new[] { "main.tf", "net/vnet.tf" }, files);
        Assert.Contains("Mi%20Proyecto/_apis/git/repositories/infra/items", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("versionDescriptor.versionType=branch", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Retries_transient_failures_but_not_client_errors()
    {
        var (client, handler) = Create(pat: "x");
        handler.Responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.Responses.Enqueue(_ => Json("""{"value":[]}"""));

        await Reader(client).ListFilesAsync(null, 10, default);
        Assert.Equal(2, handler.Requests.Count);

        handler.Responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<HttpRequestException>(() => Reader(client).ListFilesAsync(null, 10, default));
        Assert.Equal(3, handler.Requests.Count);
    }
}
