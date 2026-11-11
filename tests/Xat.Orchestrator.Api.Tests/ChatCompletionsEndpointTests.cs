using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>Tests the OpenAI-compatible route <c>POST /v1/chat/completions</c> through the real HTTP pipeline.</summary>
public class ChatCompletionsEndpointTests
{
    private const string Route = "/v1/chat/completions";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string json) =>
        client.PostAsync(Route, ApiHost.Json(json), TestContext.Current.CancellationToken);

    /// <summary>Reads the data lines of a stream. Each item is the text after <c>data: </c>.</summary>
    private static List<string> ReadData(string text) =>
        [.. text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(block => block.Trim()).Where(block => block.StartsWith("data: ", StringComparison.Ordinal)).Select(block => block[6..])];

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>A normal request gives the OpenAI answer form, with the usage, and the extension object with the run ID and the citations.</summary>
    [Fact]
    public async Task NonStreaming_GivesTheOpenAiForm()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """{"model":"pipeline/rag-qa","messages":[{"role":"user","content":"Which port?"}]}""");
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var id = body.GetProperty("id").GetString()!;
        Assert.StartsWith("chatcmpl-run_", id, StringComparison.Ordinal);
        Assert.Equal("chat.completion", body.GetProperty("object").GetString());
        Assert.Equal("pipeline/rag-qa", body.GetProperty("model").GetString());
        Assert.True(body.GetProperty("created").GetInt64() > 1_700_000_000);
        var choice = body.GetProperty("choices")[0];
        Assert.Equal(0, choice.GetProperty("index").GetInt32());
        Assert.Equal("assistant", choice.GetProperty("message").GetProperty("role").GetString());
        Assert.Equal("The orders service uses port 8080 [1].", choice.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal("stop", choice.GetProperty("finish_reason").GetString());
        var usage = body.GetProperty("usage");
        Assert.Equal((100, 20, 120), (usage.GetProperty("prompt_tokens").GetInt32(), usage.GetProperty("completion_tokens").GetInt32(), usage.GetProperty("total_tokens").GetInt32()));
        var xat = body.GetProperty("xat");
        Assert.Equal("chatcmpl-" + xat.GetProperty("run_id").GetString(), id);
        Assert.Equal("answered", xat.GetProperty("status").GetString());
        Assert.Equal("ask-all-1", xat.GetProperty("citations")[0].GetProperty("chunk_id").GetString());
        Assert.Equal(0, xat.GetProperty("warnings").GetArrayLength());
    }

    /// <summary>The OpenAI client libraries send an API key. The orchestrator has no authentication, so it accepts any key and ignores it.</summary>
    [Fact]
    public async Task AuthorizationHeader_IsIgnored()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "any-key-at-all");

        using var response = await PostAsync(client, """{"model":"pipeline/chat","messages":[{"role":"user","content":"Hi"}]}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The system messages become the system prompt, and the last user message becomes the prompt. Earlier turns are not used, and the answer says so.</summary>
    [Fact]
    public async Task Messages_AreMappedToTheInputOfThePipeline_WithNotes()
    {
        var llm = new FakeLlmClient { Answer = "Arr." };
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """
            {"model":"pipeline/chat","temperature":0.9,"max_tokens":50,
             "messages":[{"role":"system","content":"Speak like a pirate."},{"role":"user","content":"Old question"},
                         {"role":"assistant","content":"Old answer"},{"role":"user","content":"Say hello"}]}
            """);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(["Speak like a pirate.", "Say hello"], llm.Requests[0].Messages.Select(message => message.Content));
        var warnings = body.GetProperty("xat").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToList();
        Assert.Contains(warnings, w => w!.Contains("2 earlier message(s) were not used", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w!.Contains("temperature, max_tokens", StringComparison.Ordinal));
    }

    /// <summary>A pipeline without a system_prompt input cannot use system messages. The answer says so.</summary>
    [Fact]
    public async Task SystemMessage_ForAPipelineWithoutSystemPrompt_GivesANote()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """{"model":"pipeline/rag-sequential","messages":[{"role":"system","content":"Be brief."},{"role":"user","content":"Q"}]}""");
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            body.GetProperty("xat").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()),
            w => w!.Contains("system messages were not used", StringComparison.Ordinal));
    }

    /// <summary>The extension field xat.input gives the other inputs of a pipeline. The messages win for prompt.</summary>
    [Fact]
    public async Task ExtensionInput_GivesTheOtherInputs()
    {
        var llm = new FakeLlmClient();
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """
            {"model":"pipeline/custom-prompt","messages":[{"role":"system","content":"You are support."},{"role":"user","content":"Which port?"}],
             "xat":{"input":{"context":"Customer uses v2.","language":"German","prompt":"IGNORED"},"debug":true}}
            """);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = llm.Requests[0].Messages[1].Content;
        Assert.Contains("Customer uses v2.", user);
        Assert.Contains("Write the answer in German.", user);
        Assert.EndsWith("Which port?", user);
        Assert.DoesNotContain("IGNORED", user, StringComparison.Ordinal);
        Assert.True(body.GetProperty("xat").GetProperty("debug").GetProperty("steps").GetArrayLength() > 0);
    }

    /// <summary>Content as a list of text parts, the newer OpenAI form, is accepted.</summary>
    [Fact]
    public async Task ContentParts_AreAccepted()
    {
        var llm = new FakeLlmClient();
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """{"model":"pipeline/chat","messages":[{"role":"user","content":[{"type":"text","text":"Hello"},{"type":"text","text":"there"}]}]}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello\nthere", llm.Requests[0].Messages[^1].Content);
    }

    /// <summary>A stream has the OpenAI pieces: a role piece, the text pieces, a finish piece with the extension object, an optional usage piece, and the end marker.</summary>
    [Fact]
    public async Task Streaming_GivesTheOpenAiPieces()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """{"model":"pipeline/rag-qa","stream":true,"stream_options":{"include_usage":true},"messages":[{"role":"user","content":"Which port?"}]}""");
        var data = ReadData(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("[DONE]", data[^1]);
        var pieces = data[..^1].Select(Parse).ToList();
        Assert.All(pieces, piece => Assert.Equal("chat.completion.chunk", piece.GetProperty("object").GetString()));
        Assert.Equal("assistant", pieces[0].GetProperty("choices")[0].GetProperty("delta").GetProperty("role").GetString());
        var text = string.Concat(pieces.Where(p => p.GetProperty("choices").GetArrayLength() > 0 && p.GetProperty("choices")[0].GetProperty("delta").TryGetProperty("content", out _))
            .Select(p => p.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString()));
        Assert.Equal("The orders service uses port 8080 [1].", text);

        var finish = pieces.Single(p => p.GetProperty("choices").GetArrayLength() > 0 && p.GetProperty("choices")[0].GetProperty("finish_reason").ValueKind == JsonValueKind.String);
        Assert.Equal("stop", finish.GetProperty("choices")[0].GetProperty("finish_reason").GetString());
        Assert.Equal("ask-all-1", finish.GetProperty("xat").GetProperty("citations")[0].GetProperty("chunk_id").GetString());

        var usage = pieces[^1];
        Assert.Equal(0, usage.GetProperty("choices").GetArrayLength());
        Assert.Equal(120, usage.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Single(pieces.Select(p => p.GetProperty("id").GetString()).Distinct());
    }

    /// <summary>A stream without include_usage has no usage piece.</summary>
    [Fact]
    public async Task Streaming_HasNoUsagePiece_WithoutTheOption()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """{"model":"pipeline/rag-qa","stream":true,"messages":[{"role":"user","content":"Q"}]}""");
        var data = ReadData(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(data[..^1], piece => Parse(piece).TryGetProperty("usage", out _));
    }

    /// <summary>When a step ends the run without streaming, for example the no-answer path, the text goes out as one piece and the status is no_answer.</summary>
    [Fact]
    public async Task Streaming_NoAnswer_GivesOneTextPiece()
    {
        var rag = new FakeRetrievalClient { OnRetrieve = _ => Result<RetrievedSet>.Ok(new RetrievedSet([], 0)) };
        await using var factory = ApiHost.Create(rag);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """{"model":"pipeline/rag-qa","stream":true,"messages":[{"role":"user","content":"Q"}]}""");
        var data = ReadData(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var pieces = data[..^1].Select(Parse).ToList();
        var texts = pieces.Where(p => p.GetProperty("choices").GetArrayLength() > 0 && p.GetProperty("choices")[0].GetProperty("delta").TryGetProperty("content", out var c) && c.GetString()!.Length > 0).ToList();
        Assert.Equal("The sources hold no information for this question.", Assert.Single(texts).GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString());
        Assert.Equal("no_answer", pieces.Single(p => p.TryGetProperty("xat", out _)).GetProperty("xat").GetProperty("status").GetString());
    }

    /// <summary>An error before the first piece is a normal HTTP error in the OpenAI error form, with the right status.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorBeforeTheFirstPiece_IsAnHttpError(bool stream)
    {
        var rag = new FakeRetrievalClient
        {
            OnRetrieve = _ => Result<RetrievedSet>.Fail(new PipelineError("xat-rag.overloaded_error", "busy", ErrorKind.Overloaded, TimeSpan.FromSeconds(4), "xat-rag")),
        };
        await using var factory = ApiHost.Create(rag);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, $$"""{"model":"pipeline/rag-qa","stream":{{stream.ToString().ToLowerInvariant()}},"messages":[{"role":"user","content":"Q"}]}""");
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("4", response.Headers.GetValues("Retry-After").Single());
        var error = body.GetProperty("error");
        Assert.Equal("overloaded_error", error.GetProperty("type").GetString());
        Assert.Equal("xat-rag.overloaded_error", error.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("param").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    /// <summary>An error after the first piece is an event with an error body. The status stays 200 and the stream has no end marker.</summary>
    [Fact]
    public async Task ErrorInTheMiddleOfAStream_IsAnErrorEvent_WithoutAnEndMarker()
    {
        var llm = new FakeLlmClient { StreamFailure = new PipelineError("xat-llm-engine.upstream_error", "The model crashed.", ErrorKind.UpstreamFailed, Source: "xat-llm-engine") };
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, """{"model":"pipeline/rag-qa","stream":true,"messages":[{"role":"user","content":"Q"}]}""");
        var data = ReadData(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("[DONE]", data);
        var last = Parse(data[^1]);
        Assert.Equal("upstream_error", last.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal("xat-llm-engine.upstream_error", last.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>Each unsupported or wrong request gives a clear error in the OpenAI form.</summary>
    [Theory]
    [InlineData("""{"messages":[{"role":"user","content":"Q"}]}""", 400, "request.model_missing")]
    [InlineData("""{"model":"gpt-4o","messages":[{"role":"user","content":"Q"}]}""", 400, "request.model_invalid")]
    [InlineData("""{"model":"pipeline/nope","messages":[{"role":"user","content":"Q"}]}""", 404, "model.not_found")]
    [InlineData("""{"model":"pipeline/chat"}""", 400, "request.messages_missing")]
    [InlineData("""{"model":"pipeline/chat","messages":[]}""", 400, "request.messages_missing")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"system","content":"Only a system message"}]}""", 400, "request.no_user_message")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"tool","content":"x"}]}""", 400, "request.unsupported_role")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"content":"no role"}]}""", 400, "request.message_invalid")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"user","content":[{"type":"image_url","image_url":{"url":"x"}}]}]}""", 400, "request.unsupported_content")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"user","content":[{"type":1,"text":"x"}]}]}""", 400, "request.unsupported_content")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"user","content":"Q"}],"stream":"yes"}""", 400, "request.invalid_field")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"user","content":"Q"}],"stream_options":true}""", 400, "request.invalid_field")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"user","content":"Q"}],"tools":[{"type":"function"}]}""", 400, "request.unsupported_field")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"user","content":"Q"}],"n":2}""", 400, "request.unsupported_field")]
    [InlineData("""{"model":"pipeline/chat","messages":[{"role":"user","content":"Q"}],"response_format":{"type":"json_object"}}""", 400, "request.unsupported_field")]
    [InlineData("""[1,2]""", 400, "request.invalid_json")]
    [InlineData("""{broken""", 400, "request.invalid_json")]
    public async Task WrongRequests_GiveAnOpenAiError(string body, int status, string code)
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, body);
        var json = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
        Assert.True(json.GetProperty("error").TryGetProperty("param", out _));
    }

    /// <summary>A body that is not JSON content is refused.</summary>
    [Fact]
    public async Task WrongContentType_IsRefused()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();
        using var content = new StringContent("""{"model":"pipeline/chat","messages":[{"role":"user","content":"Q"}]}""", System.Text.Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(Route, content, TestContext.Current.CancellationToken);
        var json = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.content_type", json.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>The model list has the OpenAI fields, so an OpenAI client can find the pipelines that it can call.</summary>
    [Fact]
    public async Task ModelList_HasTheOpenAiFields()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        var body = await ApiHost.ReadJsonAsync(await client.GetAsync("/v1/models", TestContext.Current.CancellationToken));

        Assert.Equal("list", body.GetProperty("object").GetString());
        var ids = body.GetProperty("data").EnumerateArray().Select(model => model.GetProperty("id").GetString()).ToList();
        Assert.Equal(["pipeline/chat", "pipeline/custom-prompt", "pipeline/rag-qa", "pipeline/rag-sequential"], ids);
        Assert.All(body.GetProperty("data").EnumerateArray(), model => Assert.Equal("model", model.GetProperty("object").GetString()));
        Assert.Equal("default", body.GetProperty("default_model").GetString());
    }

    /// <summary>When the caller disconnects during a stream, the cancel reaches the call to the LLM.</summary>
    [Fact]
    public async Task Disconnect_DuringTheRun_CancelsTheUpstreamCall()
    {
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        var llm = new FakeLlmClient
        {
            OnComplete = async (_, ct) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }

                return Result<Xat.Orchestrator.Application.Abstractions.Completion>.Ok(new Xat.Orchestrator.Application.Abstractions.Completion("never", null, null));
            },
        };
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource();

        var request = client.PostAsync(Route, ApiHost.Json("""{"model":"pipeline/rag-qa","messages":[{"role":"user","content":"Q"}]}"""), cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
