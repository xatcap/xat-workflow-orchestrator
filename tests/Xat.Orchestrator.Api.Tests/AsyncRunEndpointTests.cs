using System.Net;
using System.Text.Json;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>Tests the asynchronous runs and the webhooks through the real routes.</summary>
public class AsyncRunEndpointTests
{
    private const string Pipeline = "/v1/pipelines/rag-qa/runs?mode=async";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json) =>
        client.PostAsync(path, ApiHost.Json(json), TestContext.Current.CancellationToken);

    private static async Task<JsonElement> PollAsync(HttpClient client, string runId, Func<JsonElement, bool> condition)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            using var response = await client.GetAsync($"/v1/runs/{runId}", limit.Token);
            var body = await ApiHost.ReadJsonAsync(response);
            if (condition(body))
            {
                return body;
            }

            await Task.Delay(20, limit.Token);
        }
    }

    private static Dictionary<string, string?> HookSettings(params string[] more)
    {
        var settings = new Dictionary<string, string?> { ["Webhooks:AllowedHosts:0"] = "hooks.test" };
        for (var i = 0; i < more.Length; i += 2)
        {
            settings[more[i]] = more[i + 1];
        }

        return settings;
    }

    /// <summary>A submit gives 202 with the run ID, the Location header and the path of the state. The caller polls and gets the result.</summary>
    [Fact]
    public async Task Submit_Gives202_AndThePollGivesTheResult()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Pipeline, """{"input":{"prompt":"Which port?"}}""");
        var accepted = await ApiHost.ReadJsonAsync(response);
        var runId = accepted.GetProperty("run_id").GetString()!;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("queued", accepted.GetProperty("status").GetString());
        Assert.Equal($"/v1/runs/{runId}", accepted.GetProperty("status_url").GetString());
        Assert.Equal($"/v1/runs/{runId}", response.Headers.Location!.ToString());
        var done = await PollAsync(client, runId, body => body.GetProperty("status").GetString() == "succeeded");

        Assert.Equal("rag-qa", done.GetProperty("pipeline").GetString());
        var result = done.GetProperty("result");
        Assert.Equal("answered", result.GetProperty("status").GetString());
        Assert.Equal("The orders service uses port 8080 [1].", result.GetProperty("answer").GetProperty("text").GetString());
        Assert.Equal("ask-all-1", result.GetProperty("answer").GetProperty("citations")[0].GetProperty("chunk_id").GetString());
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("started_at").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("completed_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, done.GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.Null, done.GetProperty("webhook").ValueKind);
    }

    /// <summary>The debug option reaches the stored result.</summary>
    [Fact]
    public async Task Debug_ReachesTheStoredResult()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Pipeline, """{"input":{"prompt":"Q"},"debug":true}""");
        var runId = (await ApiHost.ReadJsonAsync(response)).GetProperty("run_id").GetString()!;
        var done = await PollAsync(client, runId, body => body.GetProperty("status").GetString() == "succeeded");

        Assert.Equal(7, done.GetProperty("result").GetProperty("debug").GetProperty("steps").GetArrayLength());
    }

    /// <summary>A failed run shows the error in the same form as every other error.</summary>
    [Fact]
    public async Task FailedRun_ShowsTheError()
    {
        var rag = new FakeRetrievalClient
        {
            OnRetrieve = _ => Result<RetrievedSet>.Fail(new PipelineError("xat-rag.timeout", "xat-rag did not answer in time.", ErrorKind.Timeout, Source: "xat-rag")),
        };
        await using var factory = ApiHost.Create(rag);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Pipeline, """{"input":{"prompt":"Q"}}""");
        var runId = (await ApiHost.ReadJsonAsync(response)).GetProperty("run_id").GetString()!;
        var done = await PollAsync(client, runId, body => body.GetProperty("status").GetString() == "failed");

        Assert.Equal("timeout_error", done.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal("xat-rag.timeout", done.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(runId, done.GetProperty("error").GetProperty("run_id").GetString());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("result").ValueKind);
    }

    /// <summary>The webhook gets the run in the same form as the poll. The poll then shows the delivery, with the address without its query.</summary>
    [Fact]
    public async Task Webhook_GetsTheRun_AndThePollShowsTheDelivery()
    {
        var sender = new FakeWebhookSender { Result = new WebhookDelivery(WebhookState.Delivered, 2, null) };
        await using var factory = ApiHost.Create(webhooks: sender, settings: HookSettings());
        using var client = factory.CreateClient();

        using var response = await PostAsync(
            client, Pipeline, """{"input":{"prompt":"Q"},"webhook":"https://hooks.test/in/SECRET-PATH?token=SECRET"}""");
        var runId = (await ApiHost.ReadJsonAsync(response)).GetProperty("run_id").GetString()!;
        var done = await PollAsync(client, runId, body => body.GetProperty("webhook").ValueKind == JsonValueKind.Object
            && body.GetProperty("webhook").GetProperty("status").GetString() == "delivered");

        var call = Assert.Single(sender.Calls);
        Assert.Equal("https://hooks.test/in/SECRET-PATH?token=SECRET", call.Url.ToString());
        Assert.Equal(runId, call.RunId);
        using var payload = JsonDocument.Parse(call.Body);
        Assert.Equal("run.finished", payload.RootElement.GetProperty("event").GetString());
        var run = payload.RootElement.GetProperty("run");
        Assert.Equal(runId, run.GetProperty("run_id").GetString());
        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal("The orders service uses port 8080 [1].", run.GetProperty("result").GetProperty("answer").GetProperty("text").GetString());
        Assert.Equal("pending", run.GetProperty("webhook").GetProperty("status").GetString());

        var webhook = done.GetProperty("webhook");
        Assert.Equal("https://hooks.test", webhook.GetProperty("url").GetString());
        Assert.Equal(2, webhook.GetProperty("attempts").GetInt32());
        Assert.DoesNotContain("SECRET", done.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>A failed webhook shows the reason in the poll. The run keeps its result.</summary>
    [Fact]
    public async Task FailedWebhook_ShowsTheReason()
    {
        var sender = new FakeWebhookSender { Result = new WebhookDelivery(WebhookState.Failed, 4, "The receiver is not reachable.") };
        await using var factory = ApiHost.Create(webhooks: sender, settings: HookSettings());
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Pipeline, """{"input":{"prompt":"Q"},"webhook":"https://hooks.test/in"}""");
        var runId = (await ApiHost.ReadJsonAsync(response)).GetProperty("run_id").GetString()!;
        var done = await PollAsync(client, runId, body => body.GetProperty("webhook").ValueKind == JsonValueKind.Object
            && body.GetProperty("webhook").GetProperty("status").GetString() == "failed");

        Assert.Equal("succeeded", done.GetProperty("status").GetString());
        Assert.Equal("The receiver is not reachable.", done.GetProperty("webhook").GetProperty("error").GetString());
    }

    /// <summary>
    /// By default only a loopback host can receive a webhook. Another host gets 400 at submit, and the message does not repeat the address.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/secret-path", "webhook.host_not_allowed")]
    [InlineData("http://169.254.169.254/latest", "webhook.host_not_allowed")]
    [InlineData("ftp://127.0.0.1/x", "webhook.invalid_url")]
    [InlineData("http://user:pw@127.0.0.1/x", "webhook.invalid_url")]
    public async Task WebhookAddress_IsCheckedAtSubmit(string url, string code)
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Pipeline, $$"""{"input":{"prompt":"Q"},"webhook":"{{url}}"}""");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(code, text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-path", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pw@", text, StringComparison.Ordinal);
    }

    /// <summary>A loopback webhook is allowed with no setting.</summary>
    [Fact]
    public async Task LoopbackWebhook_IsAllowedByDefault()
    {
        await using var factory = ApiHost.Create(webhooks: new FakeWebhookSender());
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Pipeline, """{"input":{"prompt":"Q"},"webhook":"http://127.0.0.1:9/hook"}""");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>The setting star allows every webhook host.</summary>
    [Fact]
    public async Task StarSetting_AllowsAnyHost()
    {
        await using var factory = ApiHost.Create(
            webhooks: new FakeWebhookSender(), settings: new Dictionary<string, string?> { ["Webhooks:AllowedHosts:0"] = "*" });
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, Pipeline, """{"input":{"prompt":"Q"},"webhook":"https://anywhere.example/hook"}""");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>Each wrong combination of the options gives a clear 400.</summary>
    [Theory]
    [InlineData("/v1/pipelines/rag-qa/runs?mode=async", """{"input":{"prompt":"Q"},"stream":true}""", "stream.not_supported_async")]
    [InlineData("/v1/pipelines/rag-qa/runs", """{"input":{"prompt":"Q"},"webhook":"http://127.0.0.1:9/h"}""", "webhook.requires_async")]
    [InlineData("/v1/pipelines/rag-qa/runs?mode=background", """{"input":{"prompt":"Q"}}""", "request.mode_invalid")]
    [InlineData("/v1/pipelines/rag-qa/runs?mode=async", """{"input":{}}""", "input.invalid")]
    public async Task WrongOptions_Give400(string path, string body, string code)
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, path, body);
        var json = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, json.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>The option mode=sync is the same as no option.</summary>
    [Fact]
    public async Task ModeSync_RunsLikeTheDefault()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "/v1/pipelines/rag-qa/runs?mode=sync", """{"input":{"prompt":"Q"}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>An unknown run gives 404 for read and for cancel.</summary>
    [Fact]
    public async Task UnknownRun_Gives404()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var read = await client.GetAsync("/v1/runs/run_nothing", TestContext.Current.CancellationToken);
        using var cancel = await client.DeleteAsync("/v1/runs/run_nothing", TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(read);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Equal("run.not_found", body.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>A delete cancels a running run. The cancel reaches the call to the LLM, and the run ends as cancelled with the webhook.</summary>
    [Fact]
    public async Task Delete_CancelsARunningRun()
    {
        var started = new TaskCompletionSource();
        var sawCancel = new TaskCompletionSource();
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
                    sawCancel.TrySetResult();
                    throw;
                }

                return Result<Completion>.Ok(new Completion("never", null, null));
            },
        };
        var sender = new FakeWebhookSender();
        await using var factory = ApiHost.Create(llm: llm, webhooks: sender, settings: HookSettings());
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, Pipeline, """{"input":{"prompt":"Q"},"webhook":"https://hooks.test/in"}""");
        var runId = (await ApiHost.ReadJsonAsync(response)).GetProperty("run_id").GetString()!;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        using var delete = await client.DeleteAsync($"/v1/runs/{runId}", TestContext.Current.CancellationToken);
        var done = await PollAsync(client, runId, body => body.GetProperty("status").GetString() == "cancelled");

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        await sawCancel.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("run.cancelled", done.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("cancelled_error", done.GetProperty("error").GetProperty("type").GetString());
        await PollAsync(client, runId, body => body.GetProperty("webhook").GetProperty("status").GetString() == "delivered");
        using var payload = JsonDocument.Parse(Assert.Single(sender.Calls).Body);
        Assert.Equal("cancelled", payload.RootElement.GetProperty("run").GetProperty("status").GetString());
    }

    /// <summary>When workers and the queue are full, a new submit gets 503 with Retry-After, and the error code says why.</summary>
    [Fact]
    public async Task FullQueue_Gives503_WithRetryAfter()
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
            ["Jobs:Workers"] = "1",
            ["Jobs:QueueLimit"] = "1",
        });
        using var client = factory.CreateClient();

        using var first = await PostAsync(client, Pipeline, """{"input":{"prompt":"one"}}""");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using var second = await PostAsync(client, Pipeline, """{"input":{"prompt":"two"}}""");
        using var third = await PostAsync(client, Pipeline, """{"input":{"prompt":"three"}}""");
        var body = await ApiHost.ReadJsonAsync(third);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, third.StatusCode);
        Assert.Equal("5", third.Headers.GetValues("Retry-After").Single());
        Assert.Equal("jobs.queue_full", body.GetProperty("error").GetProperty("code").GetString());
        release.SetResult();
    }

    /// <summary>The settings of the jobs are checked at start. A wrong value stops the start.</summary>
    [Theory]
    [InlineData("Jobs:Workers", "0")]
    [InlineData("Jobs:QueueLimit", "0")]
    [InlineData("Jobs:WebhookWorkers", "0")]
    [InlineData("Webhooks:RetryCount", "99")]
    [InlineData("Webhooks:TimeoutSeconds", "0")]
    public async Task BadJobSettings_StopTheStart(string key, string value)
    {
        await using var factory = ApiHost.Create(settings: new Dictionary<string, string?> { [key] = value });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
