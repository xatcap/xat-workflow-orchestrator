using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Infrastructure.Tests;

/// <summary>Tests the xat-llm-engine adapter against a fake engine.</summary>
public sealed class XatEngineLlmClientTests : IDisposable
{
    private readonly Harness _harness = new();

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    private ILlmClient Client => _harness.Get<ILlmClient>();

    private static ModelProfile Model(params string[] allowed) =>
        new("default", "local/test-model", 8192, "estimate", 1, true, allowed);

    private static LlmRequest Request(ModelProfile model, double? temperature = null, int? maxTokens = null) =>
        new(model, [new ChatMessage(ChatRole.System, "Be brief."), new ChatMessage(ChatRole.User, "Hello")], temperature, maxTokens);

    private void Respond(int status, string body, string contentType = "application/json", string? retryAfter = null)
    {
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", contentType).WithBody(body);
        if (retryAfter is not null)
        {
            response = response.WithHeader("Retry-After", retryAfter);
        }

        _harness.Engine.Given(Request().WithPath("/v1/chat/completions").UsingPost()).RespondWith(response);
    }

    private static IRequestBuilder Request() => WireMock.RequestBuilders.Request.Create();

    private JsonObject SentBody() => (JsonObject)JsonNode.Parse(_harness.Engine.LogEntries[^1].RequestMessage!.Body!)!;

    private const string CompleteBody = """
        {"id":"x","object":"chat.completion","created":1,"model":"local/test-model",
         "choices":[{"index":0,"message":{"role":"assistant","content":"Hi there."},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}
        """;

    /// <summary>A complete answer maps to the text, the finish reason and the usage.</summary>
    [Fact]
    public async Task Complete_MapsTextFinishReasonAndUsage()
    {
        Respond(200, CompleteBody);

        var result = await Client.CompleteAsync(Request(Model()), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Hi there.", "stop", new TokenUsage(12, 3)), (result.Value!.Text, result.Value.FinishReason, result.Value.Usage));
    }

    /// <summary>The adapter sends only the fields that the model profile allows. The engine refuses any other top-level field.</summary>
    [Fact]
    public async Task Request_ContainsOnlyAllowedFields()
    {
        Respond(200, CompleteBody);

        await Client.CompleteAsync(Request(Model("temperature"), temperature: 0.2, maxTokens: 50), TestContext.Current.CancellationToken);

        var body = SentBody();
        Assert.Equal(["model", "messages", "stream", "temperature"], body.Select(pair => pair.Key));
        Assert.Equal("local/test-model", (string?)body["model"]);
        Assert.False((bool)body["stream"]!);
        Assert.Equal(0.2, (double)body["temperature"]!);
        Assert.Equal(["system", "user"], body["messages"]!.AsArray().Select(message => (string?)message!["role"]));
    }

    /// <summary>A model that lists max_completion_tokens, like OpenAI, gets that field name.</summary>
    [Fact]
    public async Task MaxTokens_UsesMaxCompletionTokens_WhenTheModelListsIt()
    {
        Respond(200, CompleteBody);

        await Client.CompleteAsync(Request(Model("max_completion_tokens"), maxTokens: 50), TestContext.Current.CancellationToken);

        var body = SentBody();
        Assert.Equal(50, (int)body["max_completion_tokens"]!);
        Assert.False(body.ContainsKey("max_tokens"));
    }

    /// <summary>The request is JSON, because the engine refuses another content type.</summary>
    [Fact]
    public async Task Request_HasTheJsonContentType()
    {
        Respond(200, CompleteBody);

        await Client.CompleteAsync(Request(Model()), TestContext.Current.CancellationToken);

        var contentType = _harness.Engine.LogEntries[^1].RequestMessage!.Headers!["Content-Type"].ToString();
        Assert.StartsWith("application/json", contentType, StringComparison.Ordinal);
    }

    /// <summary>The errors of the engine map to the kinds of the orchestrator.</summary>
    [Theory]
    [InlineData(400, "invalid_request_error", ErrorKind.Validation)]
    [InlineData(404, "not_found_error", ErrorKind.NotFound)]
    [InlineData(429, "rate_limit_error", ErrorKind.RateLimited)]
    [InlineData(502, "upstream_error", ErrorKind.UpstreamFailed)]
    [InlineData(503, "overloaded_error", ErrorKind.Overloaded)]
    [InlineData(503, "service_unavailable_error", ErrorKind.UpstreamUnavailable)]
    public async Task Complete_MapsErrors(int status, string type, ErrorKind expected)
    {
        Respond(status, $$$"""{"error":{"message":"A problem.","type":"{{{type}}}"}}""", retryAfter: status is 429 or 503 ? "9" : null);

        var result = await Client.CompleteAsync(Request(Model()), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal($"xat-llm-engine.{type}", result.Error.Code);
        Assert.Equal(status is 429 or 503 ? TimeSpan.FromSeconds(9) : null, result.Error.RetryAfter);
    }

    private static string Sse(params string[] events) => string.Concat(events.Select(e => e.StartsWith(':') ? e + "\n\n" : "data: " + e + "\n\n"));

    private static string Chunk(string? content, string? finish = null) =>
        $$"""{"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"content":{{(content is null ? "null" : "\"" + content + "\"")}}},"finish_reason":{{(finish is null ? "null" : "\"" + finish + "\"")}}}],"usage":null}""";

    private const string UsageChunk = """{"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":4,"total_tokens":14}}""";

    private static async Task<List<Result<CompletionDelta>>> CollectAsync(IAsyncEnumerable<Result<CompletionDelta>> stream)
    {
        var items = new List<Result<CompletionDelta>>();
        await foreach (var item in stream)
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>A stream gives the deltas in order, then the finish reason and the usage. Comments and the end marker are skipped.</summary>
    [Fact]
    public async Task Stream_GivesDeltasFinishAndUsage()
    {
        Respond(
            200,
            Sse(Chunk(string.Empty), ": keep-alive", Chunk("Hello"), Chunk(" world"), Chunk(null, "stop"), UsageChunk, "[DONE]"),
            "text/event-stream");

        var items = await CollectAsync(Client.StreamAsync(Request(Model("stream_options")), TestContext.Current.CancellationToken));

        Assert.All(items, item => Assert.True(item.IsSuccess));
        Assert.Equal("Hello world", string.Concat(items.Select(item => item.Value!.Text)));
        Assert.Contains(items, item => item.Value!.FinishReason == "stop");
        Assert.Equal(new TokenUsage(10, 4), items.Last().Value!.Usage);
        var body = SentBody();
        Assert.True((bool)body["stream"]!);
        Assert.True((bool)body["stream_options"]!["include_usage"]!);
    }

    /// <summary>A failure after the first piece is an event with an error body. The stream ends with a failure item.</summary>
    [Fact]
    public async Task Stream_ErrorEvent_EndsWithAFailure()
    {
        Respond(
            200,
            Sse(Chunk("Hel"), """{"error":{"message":"The model crashed.","type":"upstream_error"}}"""),
            "text/event-stream");

        var items = await CollectAsync(Client.StreamAsync(Request(Model()), TestContext.Current.CancellationToken));

        Assert.True(items[0].IsSuccess);
        var last = items[^1];
        Assert.False(last.IsSuccess);
        Assert.Equal("xat-llm-engine.upstream_error", last.Error!.Code);
    }

    /// <summary>A stream that stops with no finish reason and no end marker is cut off. The adapter reports it.</summary>
    [Fact]
    public async Task Stream_CutOff_EndsWithAFailure()
    {
        Respond(200, Sse(Chunk("Hel"), Chunk("lo")), "text/event-stream");

        var items = await CollectAsync(Client.StreamAsync(Request(Model()), TestContext.Current.CancellationToken));

        Assert.Equal("xat-llm-engine.stream_incomplete", items[^1].Error!.Code);
    }

    /// <summary>A stream that ends after the finish reason is complete, also without the end marker.</summary>
    [Fact]
    public async Task Stream_EndsAfterFinishReason_IsComplete()
    {
        Respond(200, Sse(Chunk("Hi"), Chunk(null, "stop")), "text/event-stream");

        var items = await CollectAsync(Client.StreamAsync(Request(Model()), TestContext.Current.CancellationToken));

        Assert.All(items, item => Assert.True(item.IsSuccess));
    }

    /// <summary>An error before the stream starts is a normal HTTP error. The stream has one failure item.</summary>
    [Fact]
    public async Task Stream_HttpError_GivesOneFailure()
    {
        Respond(503, """{"error":{"message":"queue full","type":"overloaded_error"}}""", retryAfter: "3");

        var items = await CollectAsync(Client.StreamAsync(Request(Model()), TestContext.Current.CancellationToken));

        var item = Assert.Single(items);
        Assert.Equal(ErrorKind.Overloaded, item.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(3), item.Error.RetryAfter);
    }

    /// <summary>The list of models gives the IDs.</summary>
    [Fact]
    public async Task ListModels_GivesTheIds()
    {
        _harness.Engine.Given(Request().WithPath("/v1/models").UsingGet()).RespondWith(
            Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"object":"list","data":[{"id":"local/a","object":"model","owned_by":"local"},{"id":"openai/b","object":"model","owned_by":"openai"}]}"""));

        var result = await Client.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["local/a", "openai/b"], result.Value!);
    }
}
