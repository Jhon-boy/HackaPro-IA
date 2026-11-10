using InfraAgent.Application.Abstractions;
using InfraAgent.Application.Chat;
using InfraAgent.Application.Review;
using InfraAgent.Application.Tools;
using InfraAgent.Core.Models;
using InfraAgent.Tests.Support;

namespace InfraAgent.Tests.Application;

public class GenerationAndChatTests
{
    private const string BadTf = "resource \"azurerm_storage_account\" \"datos\" {\n  public_network_access_enabled = true\n}";
    private const string GoodTf = "resource \"azurerm_storage_account\" \"datos\" {\n  public_network_access_enabled = false\n}";

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };

    private static ToolContext Ctx() => new() { CorrelationId = "t" };

    // ---------------- Generación de Terraform ----------------

    [Fact]
    public async Task Generation_fixes_blocking_security_findings_in_a_bounded_round()
    {
        var host = new TestHost().Build();
        host.Runner
            .Script(AgentRole.IacGenerator, Json.Generator("main.tf", BadTf, "SEC-001"), Json.Generator("main.tf", GoodTf, "SEC-001"))
            .Script(AgentRole.Security,
                Json.Review("v1", Json.Finding("SEC-001", "main.tf", "public_network_access_enabled = true", line: 2)),
                Json.Review("v2"));

        var result = await host.Get<TerraformGenerationService>().GenerateAsync("un storage privado", null, Ctx(), default);

        Assert.Equal(1, result.FixRounds);
        Assert.False(result.HasBlockingFindings);
        Assert.Contains("= false", result.Files.Single().Content);
        // La segunda ronda recibe la versión anterior y el problema concreto.
        var second = host.Runner.For(AgentRole.IacGenerator).Last().UserMessage;
        Assert.Contains("Versión anterior", second);
        Assert.Contains("SEC-001", second);
    }

    [Fact]
    public async Task Generation_never_exceeds_the_configured_fix_rounds()
    {
        var host = new TestHost().Build(s => s.Generation.MaxFixRounds = 0);
        host.Runner
            .Script(AgentRole.IacGenerator, Json.Generator("main.tf", BadTf))
            .Script(AgentRole.Security, Json.Review("v1", Json.Finding("SEC-001", "main.tf", "public_network_access_enabled = true", line: 2)));

        var result = await host.Get<TerraformGenerationService>().GenerateAsync("storage", null, Ctx(), default);

        Assert.Equal(0, result.FixRounds);
        Assert.True(result.HasBlockingFindings);
        Assert.Single(host.Runner.For(AgentRole.IacGenerator));
    }

    [Fact]
    public async Task Generated_files_with_literal_secrets_or_unsafe_paths_are_discarded()
    {
        var host = new TestHost().Build();
        var output = System.Text.Json.JsonSerializer.Serialize(new
        {
            summary = "x",
            files = new[]
            {
                new { path = "main.tf", content = "resource \"azurerm_mssql_server\" \"s\" {\n  administrator_login_password = \"Clave-Real-9876\"\n}" },
                new { path = "../evil.tf", content = "# x" },
                new { path = "deploy.sh", content = "rm -rf /" },
                new { path = "variables.tf", content = "variable \"x\" {\n  type = string\n}" }
            },
            policiesApplied = new[] { "SEC-004", "XXX-000" },
            notes = Array.Empty<string>()
        });
        host.Runner.Script(AgentRole.IacGenerator, output).Script(AgentRole.Security, Json.Review("ok"));

        var result = await host.Get<TerraformGenerationService>().GenerateAsync("sql", null, Ctx(), default);

        Assert.Equal(new[] { "variables.tf" }, result.Files.Select(f => f.Path));
        Assert.Equal(3, result.Problems.Count);
        Assert.DoesNotContain(result.Problems, p => p.Contains("Clave-Real-9876"));
        Assert.Equal(new[] { "SEC-004" }, result.PoliciesApplied);   // ids inexistentes se descartan
    }

    // ---------------- Chat ----------------

    private static ChatImage Image(byte[]? data = null, string mime = "image/png") =>
        new() { MimeType = mime, Base64 = Convert.ToBase64String(data ?? Png), Name = "arq.png" };

    [Fact]
    public async Task Chat_rejects_invalid_input_before_calling_any_agent()
    {
        var host = new TestHost().Build(s => s.Chat.MaxImages = 1);
        var chat = host.Get<ChatService>();

        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest { Message = "  " }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest { Message = "hola", ConversationId = "a b" }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest { Message = "hola", ProjectId = "no-existe" }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest { Message = "hola", Images = { Image(), Image() } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest { Message = "hola", Images = { Image(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }) } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest { Message = "hola", Images = { new ChatImage { MimeType = "image/png", Base64 = "###" } } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.HandleAsync(new ChatRequest
        {
            Message = "hola",
            Files = { new GeneratedFile { Path = "script.sh", Content = "x" } }
        }, default));

        Assert.Empty(host.Runner.Requests);
    }

    [Fact]
    public async Task Chat_architecture_flow_routes_images_to_the_architecture_agent_only()
    {
        var host = new TestHost().Build();
        host.Runner
            .Script(AgentRole.Orchestrator, async (req, tools, ctx) =>
            {
                await tools.InvokeAsync(AgentRole.Orchestrator, ToolNames.AnalyzeArchitecture, """{"descripcion":"app con storage"}""", ctx, default);
                await tools.InvokeAsync(AgentRole.Orchestrator, ToolNames.EvaluatePolicies, "{}", ctx, default);
                return "El storage no cumple SEC-001.";
            })
            .Script(AgentRole.Architecture, Json.Architecture())
            .Script(AgentRole.Policy, """{"summary":"s","findings":[],"compliance":[{"policyId":"sec-001","status":"NoCumple","justification":"j","adjustment":"a"},{"policyId":"FAKE-999","status":"Cumple"}]}""");

        var response = await host.Get<ChatService>().HandleAsync(
            new ChatRequest { Message = "¿cómo funcionaría esto según las políticas?", Images = { Image() } }, default);

        Assert.Equal("El storage no cumple SEC-001.", response.Reply);
        Assert.StartsWith("thread_", response.ConversationId);
        Assert.NotNull(response.Architecture);
        Assert.Equal(new[] { ToolNames.AnalyzeArchitecture, ToolNames.EvaluatePolicies }, response.ToolsUsed);

        Assert.Empty(host.Runner.For(AgentRole.Orchestrator).Single().Images);               // el orquestador no ve imágenes
        Assert.Single(host.Runner.For(AgentRole.Architecture).Single().Images);              // la arquitectura sí
        Assert.Contains("Imágenes de arquitectura adjuntas en este mensaje: 1", host.Runner.For(AgentRole.Orchestrator).Single().UserMessage);
    }

    [Fact]
    public async Task Evaluate_policies_without_architecture_returns_a_guiding_error()
    {
        var host = new TestHost().Build();
        string? toolResult = null;
        host.Runner.Script(AgentRole.Orchestrator, async (req, tools, ctx) =>
        {
            toolResult = await tools.InvokeAsync(AgentRole.Orchestrator, ToolNames.EvaluatePolicies, "{}", ctx, default);
            return "ok";
        });

        await host.Get<ChatService>().HandleAsync(new ChatRequest { Message = "evalúa" }, default);

        Assert.Contains("analizar_arquitectura", toolResult);
    }

    [Fact]
    public async Task Chat_generation_flow_returns_files_and_keeps_them_for_the_next_turn()
    {
        var host = new TestHost().Build();
        host.Runner
            .Script(AgentRole.Orchestrator, async (req, tools, ctx) =>
            {
                await tools.InvokeAsync(AgentRole.Orchestrator, ToolNames.GenerateTerraform, """{"requisitos":"storage privado"}""", ctx, default);
                return "Generé 1 archivo.";
            })
            .Script(AgentRole.IacGenerator, Json.Generator("main.tf", GoodTf, "SEC-001"))
            .Script(AgentRole.Security, Json.Review("ok"));

        var first = await host.Get<ChatService>().HandleAsync(new ChatRequest { Message = "genera un storage privado" }, default);

        Assert.Equal("main.tf", first.Files.Single().Path);

        // Segundo turno: el cliente devuelve los archivos y el orquestador pide revisarlos.
        host.Runner
            .Script(AgentRole.Orchestrator, async (req, tools, ctx) =>
            {
                Assert.Equal(first.ConversationId, req.ThreadId);
                return await tools.InvokeAsync(AgentRole.Orchestrator, ToolNames.ReviewSecurity, "{}", ctx, default);
            })
            .Script(AgentRole.Security, Json.Review("revisado"));

        var second = await host.Get<ChatService>().HandleAsync(
            new ChatRequest { Message = "revisa lo generado", ConversationId = first.ConversationId, Files = first.Files }, default);

        Assert.Contains("archivosAnalizados", second.Reply);
        Assert.Contains("Archivos Terraform previos en la conversación: 1", host.Runner.For(AgentRole.Orchestrator).Last().UserMessage);
    }

    [Fact]
    public async Task Chat_with_a_project_reads_its_configured_branch_and_never_a_model_chosen_one()
    {
        var host = new TestHost().Build();
        host.Repositories.Files["main.tf"] = GoodTf;
        string? tree = null;
        host.Runner
            .Script(AgentRole.Orchestrator, async (req, tools, ctx) =>
                await tools.InvokeAsync(AgentRole.Orchestrator, ToolNames.AnalyzeArchitecture, "{}", ctx, default))
            .Script(AgentRole.Architecture, async (req, tools, ctx) =>
            {
                tree = await tools.InvokeAsync(AgentRole.Architecture, ToolNames.GetRepoTree, "{}", ctx, default);
                return Json.Architecture();
            });

        await host.Get<ChatService>().HandleAsync(new ChatRequest { Message = "analiza el repo", ProjectId = "DEMO" }, default);

        var project = Assert.Single(host.Repositories.Projects);
        Assert.Equal("demo", project.Id);
        Assert.Equal("main", project.Branch);
        Assert.Equal("main.tf", tree);
    }

    [Fact]
    public async Task Chat_masks_secrets_in_the_user_message_before_sending_to_the_orchestrator()
    {
        var host = new TestHost().Build();
        host.Runner.Script(AgentRole.Orchestrator, "ok");

        await host.Get<ChatService>().HandleAsync(new ChatRequest { Message = "mi clave es client_secret = \"Valor-Real-12345\"" }, default);

        var sent = host.Runner.Requests.Single().UserMessage;
        Assert.DoesNotContain("Valor-Real-12345", sent);
        Assert.Contains("***MASKED***", sent);
    }

    [Fact]
    public async Task Chat_times_out_with_a_friendly_error_when_the_budget_is_exceeded()
    {
        var host = new TestHost().Build(s => s.Chat.TimeoutSeconds = 1);
        host.Runner.Script(AgentRole.Orchestrator, async (_, _, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return "tarde";
        });

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            host.Get<ChatService>().HandleAsync(new ChatRequest { Message = "hola" }, default));

        Assert.Contains("tiempo máximo", ex.Message);
    }

    [Fact]
    public async Task Deleting_a_conversation_validates_the_id_and_deletes_the_thread()
    {
        var host = new TestHost().Build();
        var chat = host.Get<ChatService>();

        await chat.DeleteConversationAsync("thread_0001", default);
        Assert.Equal(new[] { "thread_0001" }, host.Runner.DeletedThreads);
        await Assert.ThrowsAsync<ArgumentException>(() => chat.DeleteConversationAsync("../x", default));
    }
}

