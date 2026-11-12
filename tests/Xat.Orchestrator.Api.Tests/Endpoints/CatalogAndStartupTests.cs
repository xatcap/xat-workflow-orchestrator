using System.Net;
using System.Text.Json;
using Xat.Orchestrator.Api.Tests.Support;

namespace Xat.Orchestrator.Api.Tests.Endpoints;

/// <summary>Tests the routes that describe the orchestrator, and the start of the service.</summary>
public class CatalogAndStartupTests
{
    /// <summary>The pipeline list shows each pipeline with its input schema, so a caller finds new pipelines with no code change.</summary>
    [Fact]
    public async Task Pipelines_AreListedWithTheirInputSchema()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/pipelines", TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        var names = body.GetProperty("pipelines").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Equal(["chat", "custom-prompt", "rag-qa", "rag-sequential"], names);
        var ragQa = body.GetProperty("pipelines").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "rag-qa");
        Assert.Equal("prompt", ragQa.GetProperty("input_schema").GetProperty("required")[0].GetString());
        Assert.Equal("allowed", ragQa.GetProperty("stream").GetString());
        Assert.Contains("rag.retrieve", ragQa.GetProperty("steps").EnumerateArray().Select(s => s.GetString()));
    }

    /// <summary>One pipeline can be read by name. An unknown name gives the error form.</summary>
    [Fact]
    public async Task Pipeline_ByName_OrNotFound()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var found = await client.GetAsync("/v1/pipelines/chat", TestContext.Current.CancellationToken);
        using var missing = await client.GetAsync("/v1/pipelines/nope", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("chat", (await ApiHost.ReadJsonAsync(found)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>The step list shows the step types that pipeline files can use.</summary>
    [Fact]
    public async Task Steps_AreListed()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        var body = await ApiHost.ReadJsonAsync(await client.GetAsync("/v1/steps", TestContext.Current.CancellationToken));

        var llm = body.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "llm.generate");
        Assert.Contains("CallsLlm", llm.GetProperty("traits").EnumerateArray().Select(t => t.GetString()));
    }

    /// <summary>The contexts route shows each retrieval alias with its xat-rag context and its number of chunks.</summary>
    [Fact]
    public async Task Contexts_ShowTheAliases()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        var body = await ApiHost.ReadJsonAsync(await client.GetAsync("/v1/contexts", TestContext.Current.CancellationToken));

        var byAlias = body.GetProperty("contexts").EnumerateArray().ToDictionary(c => c.GetProperty("alias").GetString()!, c => c);
        Assert.Equal("ask-docs", byAlias["docs"].GetProperty("context").GetString());
        Assert.True(byAlias["docs"].GetProperty("chunks").GetInt32() > 0);
    }

    /// <summary>When the upstream services are down, ready says degraded, with status 200, because the orchestrator itself runs.</summary>
    [Fact]
    public async Task Ready_IsDegraded_WhenUpstreamsAreDown()
    {
        await using var factory = ApiHost.Create(settings: new Dictionary<string, string?>
        {
            ["Upstreams:XatRag:BaseUrl"] = "http://127.0.0.1:1",
            ["Upstreams:XatLlmEngine:BaseUrl"] = "http://127.0.0.1:1",
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("degraded", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("upstreams").GetArrayLength());
        Assert.All(body.GetProperty("upstreams").EnumerateArray(), upstream => Assert.Equal("degraded", upstream.GetProperty("status").GetString()));
    }

    /// <summary>A pipeline file with a mistake stops the start. The service never runs with a wrong pipeline.</summary>
    [Fact]
    public async Task BadPipelineFile_StopsTheStart()
    {
        var folder = Directory.CreateTempSubdirectory("xat-bad-pipelines-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "bad.yaml"), "name: bad\nsteps:\n  - uses: not.a.step\n", TestContext.Current.CancellationToken);
            await using var factory = ApiHost.Create(settings: new Dictionary<string, string?> { ["Pipelines:Folder"] = folder });

            Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A setting that is not valid stops the start. The check covers the model and retrieval settings.</summary>
    [Fact]
    public async Task BadModelSetting_StopsTheStart()
    {
        await using var factory = ApiHost.Create(settings: new Dictionary<string, string?> { ["Llm:DefaultModel"] = "missing" });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    /// <summary>A number out of its range in an Api or ingestion setting stops the start too.</summary>
    [Theory]
    [InlineData("Api:MaxConcurrentRuns", "0")]
    [InlineData("Ingestion:ActivityTimeoutSeconds", "0")]
    [InlineData("Upstreams:XatRag:RetryCount", "9")]
    [InlineData("Api:DefaultPipeline", "missing")]
    public async Task BadApiSetting_StopsTheStart(string key, string value)
    {
        await using var factory = ApiHost.Create(settings: new Dictionary<string, string?> { [key] = value });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    /// <summary>The JSON of every response uses snake_case names, like the upstream services.</summary>
    [Fact]
    public async Task Json_UsesSnakeCase()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        var text = await (await client.GetAsync("/v1/pipelines", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(text);
        Assert.True(document.RootElement.GetProperty("pipelines")[0].TryGetProperty("input_schema", out _));
        Assert.DoesNotContain("inputSchema", text, StringComparison.Ordinal);
    }
}
