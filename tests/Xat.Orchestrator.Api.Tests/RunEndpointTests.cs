using System.Net;
using System.Text.Json;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>Tests the routes that run a pipeline, through the real HTTP pipeline with fake upstream ports.</summary>
public class RunEndpointTests
{
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json) =>
        client.PostAsync(path, ApiHost.Json(json), TestContext.Current.CancellationToken);

    /// <summary>A normal run gives the answer, the citation that maps to the chunk, the usage and the run ID.</summary>
    [Fact]
    public async Task RagQa_Sync_ReturnsTheAnswerWithCitations()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Which port?"}}""");
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("rag-qa", body.GetProperty("pipeline").GetString());
        Assert.Equal("answered", body.GetProperty("status").GetString());
        Assert.Equal("The orders service uses port 8080 [1].", body.GetProperty("answer").GetProperty("text").GetString());
        var citation = body.GetProperty("answer").GetProperty("citations")[0];
        Assert.Equal("ask-all-1", citation.GetProperty("chunk_id").GetString());
        Assert.Equal("Orders", citation.GetProperty("title").GetString());
        Assert.Equal(100, body.GetProperty("usage").GetProperty("prompt_tokens").GetInt32());
        Assert.Equal(body.GetProperty("run_id").GetString(), response.Headers.GetValues("X-Run-Id").Single());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("debug").ValueKind);
    }

    /// <summary>The debug option adds the time of each step.</summary>
    [Fact]
    public async Task Debug_AddsTheStepTimes()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Q"},"debug":true}""");
        var body = await ApiHost.ReadJsonAsync(response);

        var steps = body.GetProperty("debug").GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("id").GetString()).ToList();
        Assert.Equal(["system", "user", "retrieve", "no-sources", "assemble", "generate", "citations"], steps);
    }

    /// <summary>The ask route runs the default pipeline.</summary>
    [Fact]
    public async Task Ask_RunsTheDefaultPipeline()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/ask", """{"input":{"prompt":"Q"}}""");
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal("rag-qa", body.GetProperty("pipeline").GetString());
    }

    /// <summary>A stream sends the events in order and ends with the same answer as the sync route.</summary>
    [Fact]
    public async Task RagQa_Stream_SendsServerSentEvents()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Which port?"},"stream":true}""");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var events = ApiHost.ParseSse(text);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("run.started", events[0].Name);
        Assert.Contains(events, e => e.Name == "step.started" && e.Data.GetProperty("step_id").GetString() == "retrieve");
        var answer = string.Concat(events.Where(e => e.Name == "answer.delta").Select(e => e.Data.GetProperty("text").GetString()));
        Assert.Equal("The orders service uses port 8080 [1].", answer);
        Assert.Equal("ask-all-1", events.Single(e => e.Name == "answer.citations").Data.GetProperty("citations")[0].GetProperty("chunk_id").GetString());
        var last = events[^1];
        Assert.Equal("run.completed", last.Name);
        Assert.Equal(answer, last.Data.GetProperty("answer").GetProperty("text").GetString());
    }

    /// <summary>A failure in a streamed run is the last event, in the same error form.</summary>
    [Fact]
    public async Task Stream_Failure_EndsWithARunErrorEvent()
    {
        var rag = new FakeRetrievalClient
        {
            OnRetrieve = _ => Result<RetrievedSet>.Fail(new PipelineError("xat-rag.timeout", "xat-rag did not answer in time.", ErrorKind.Timeout, Source: "xat-rag")),
        };
        await using var factory = ApiHost.Create(rag);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Q"},"stream":true}""");
        var events = ApiHost.ParseSse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var last = events[^1];
        Assert.Equal("run.error", last.Name);
        Assert.Equal("timeout_error", last.Data.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal("xat-rag.timeout", last.Data.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(events, e => e.Name == "run.completed");
    }

    /// <summary>When retrieval finds nothing, the answer says so, the status is no_answer, and the LLM is not called.</summary>
    [Fact]
    public async Task NoSources_GivesNoAnswer_WithoutCallingTheLlm()
    {
        var llm = new FakeLlmClient();
        var rag = new FakeRetrievalClient { OnRetrieve = _ => Result<RetrievedSet>.Ok(new RetrievedSet([], 0)) };
        await using var factory = ApiHost.Create(rag, llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Q"}}""");
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal("no_answer", body.GetProperty("status").GetString());
        Assert.Empty(llm.Requests);
    }

    /// <summary>The plain chat pipeline sends the prompt to the LLM with no retrieval.</summary>
    [Fact]
    public async Task Chat_DoesNoRetrieval()
    {
        var rag = new FakeRetrievalClient();
        var llm = new FakeLlmClient { Answer = "Hello!" };
        await using var factory = ApiHost.Create(rag, llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/chat/runs", """{"input":{"prompt":"Hi","system_prompt":"Be nice."}}""");
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal("Hello!", body.GetProperty("answer").GetProperty("text").GetString());
        Assert.Empty(rag.Queries);
        Assert.Equal(["Be nice.", "Hi"], llm.Requests[0].Messages.Select(message => message.Content));
    }

    /// <summary>
    /// The seven-step flow: the system prompt, the prompt and the context come from the API call, the chunks come from the RAG,
    /// and the extra prompt comes from a template. The model receives all parts in the order of the pipeline.
    /// </summary>
    [Fact]
    public async Task CustomPrompt_BuildsTheMessagesFromAllParts()
    {
        var llm = new FakeLlmClient();
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(
            client,
            "/v1/pipelines/custom-prompt/runs",
            """{"input":{"system_prompt":"You are a support agent.","prompt":"Which port?","context":"Customer uses v2.","language":"German"}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = llm.Requests[0].Messages;
        Assert.Contains("You are a support agent.", messages[0].Content);
        var user = messages[1].Content;
        var contextAt = user.IndexOf("<context name=\"caller-context\">", StringComparison.Ordinal);
        var sourcesAt = user.IndexOf("<sources name=\"kb\">", StringComparison.Ordinal);
        var extraAt = user.IndexOf("Answer in at most five sentences.", StringComparison.Ordinal);
        Assert.True(contextAt >= 0 && contextAt < sourcesAt && sourcesAt < extraAt, "The parts are not in the order of the pipeline.");
        Assert.Contains("Customer uses v2.", user);
        Assert.Contains("Write the answer in German.", user);
        Assert.EndsWith("Which port?", user);
    }

    /// <summary>The sequential pipeline asks three RAGs in order, then merges the chunks.</summary>
    [Fact]
    public async Task RagSequential_AsksThreeRagsInOrder_AndMerges()
    {
        var rag = new FakeRetrievalClient();
        var llm = new FakeLlmClient();
        await using var factory = ApiHost.Create(rag, llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-sequential/runs", """{"input":{"prompt":"Which port?"}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["ask-docs", "ask-jira", "ask-diffs"], rag.Queries.Select(query => query.Context));
        var user = llm.Requests[0].Messages[^1].Content;
        Assert.Contains("<source n=\"1\"", user);
        Assert.Contains("<source n=\"3\"", user);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(user, "<sources name="));
    }

    /// <summary>Each kind of bad request gives the error form with the right status.</summary>
    [Theory]
    [InlineData("/v1/pipelines/rag-qa/runs", """{"input":{}}""", 400, "invalid_request_error", "input.invalid")]
    [InlineData("/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Q","extra":1}}""", 400, "invalid_request_error", "input.invalid")]
    [InlineData("/v1/pipelines/rag-qa/runs", """{not json""", 400, "invalid_request_error", "request.invalid_json")]
    [InlineData("/v1/pipelines/rag-qa/runs", """null""", 400, "invalid_request_error", "request.empty")]
    [InlineData("/v1/pipelines/rag-qa/runs", "", 400, "invalid_request_error", "request.empty")]
    [InlineData("/v1/pipelines/nope/runs", """{"input":{"prompt":"Q"}}""", 404, "not_found_error", "pipeline.not_found")]
    public async Task BadRequests_GiveTheErrorForm(string path, string body, int status, string type, string code)
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, path, body);
        var json = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(type, json.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("error").GetProperty("message").GetString()));
    }

    /// <summary>A body that is not JSON is refused. This also stops a simple cross-site form post from a web page.</summary>
    [Fact]
    public async Task WrongContentType_IsRefused()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();
        using var content = new StringContent("""{"input":{"prompt":"Q"}}""", System.Text.Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync("/v1/pipelines/rag-qa/runs", content, TestContext.Current.CancellationToken);
        var json = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.content_type", json.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>The error of an upstream keeps its class, and the retry time becomes the Retry-After header.</summary>
    [Fact]
    public async Task UpstreamOverloaded_Gives503_WithRetryAfter()
    {
        var rag = new FakeRetrievalClient
        {
            OnRetrieve = _ => Result<RetrievedSet>.Fail(new PipelineError("xat-rag.overloaded_error", "busy", ErrorKind.Overloaded, TimeSpan.FromSeconds(4), "xat-rag")),
        };
        await using var factory = ApiHost.Create(rag);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Q"}}""");
        var json = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("4", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal("overloaded_error", json.GetProperty("error").GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("error").GetProperty("run_id").GetString()));
    }

    /// <summary>A failure inside the orchestrator is a 500 with a general message. The exception text never reaches the caller.</summary>
    [Fact]
    public async Task InternalFailure_Gives500_WithoutDetails()
    {
        var llm = new FakeLlmClient { OnComplete = (_, _) => throw new InvalidOperationException("secret internal detail") };
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Q"}}""");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("internal_error", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret internal detail", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The orchestrator accepts as many runs as the setting allows. A run above that and above the queue gets 503 with Retry-After
    /// and a clear code, and the runs in progress finish normally.
    /// </summary>
    [Fact]
    public async Task AdmissionControl_RefusesRunsAboveTheLimit()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var llm = new FakeLlmClient
        {
            OnComplete = async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return Result<Completion>.Ok(new Completion("Done [1].", "stop", null));
            },
        };
        await using var factory = ApiHost.Create(llm: llm, settings: new Dictionary<string, string?>
        {
            ["Api:MaxConcurrentRuns"] = "1",
            ["Api:QueueLimit"] = "0",
        });
        using var client = factory.CreateClient();

        var first = PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"first"}}""");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using var second = await PostAsync(client, "/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"second"}}""");
        var json = await ApiHost.ReadJsonAsync(second);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("1", second.Headers.GetValues("Retry-After").Single());
        Assert.Equal("admission.full", json.GetProperty("error").GetProperty("code").GetString());

        release.SetResult();
        using var firstResponse = await first;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    /// <summary>When the caller disconnects, the cancel reaches the call to the LLM, so the model slot is free again.</summary>
    [Fact]
    public async Task CallerDisconnect_CancelsTheUpstreamCall()
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

                return Result<Completion>.Ok(new Completion("never", null, null));
            },
        };
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource();

        var request = client.PostAsync("/v1/pipelines/rag-qa/runs", ApiHost.Json("""{"input":{"prompt":"Q"}}"""), cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
