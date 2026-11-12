using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Application.Abstractions.Runs;

namespace Xat.Orchestrator.Api.Tests.Support;

/// <summary>Builds a test host with the real routes, the real pipelines and fake upstream ports.</summary>
internal static class ApiHost
{
    /// <summary>Finds the root folder of the repository, where the pipeline files are.</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XatOrchestrator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The solution folder was not found.");
    }

    public static WebApplicationFactory<Program> Create(
        FakeRetrievalClient? rag = null,
        FakeLlmClient? llm = null,
        Dictionary<string, string?>? settings = null,
        FakeWebhookSender? webhooks = null,
        bool kestrel = false)
    {
        rag ??= new FakeRetrievalClient();
        llm ??= new FakeLlmClient();
        var configured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings ?? [])
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRetrievalClient>();
                services.RemoveAll<ILlmClient>();
                services.AddSingleton<IRetrievalClient>(rag);
                services.AddSingleton<ILlmClient>(llm);
                if (webhooks is not null)
                {
                    services.RemoveAll<IWebhookSender>();
                    services.AddSingleton<IWebhookSender>(webhooks);
                }
            });
        });

        if (kestrel)
        {
            // A real Kestrel server keeps the original text of the request path. The in-process test server does not.
            // The setting must be on the factory that starts the server, and the port 0 lets the system choose a free port.
            configured.UseKestrel(0);
            configured.StartServer();
        }

        return configured;
    }

    public static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>Reads a stream of server-sent events into a list of name and data pairs.</summary>
    public static List<(string Name, JsonElement Data)> ParseSse(string text)
    {
        var events = new List<(string, JsonElement)>();
        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            var name = lines.First(line => line.StartsWith("event: ", StringComparison.Ordinal))[7..];
            var data = lines.First(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..];
            using var document = JsonDocument.Parse(data);
            events.Add((name, document.RootElement.Clone()));
        }

        return events;
    }
}
