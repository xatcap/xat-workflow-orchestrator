using Microsoft.Extensions.DependencyInjection;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Application.Jobs;
using Xat.Orchestrator.Application.Tests.Fakes;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Tests.Jobs;

/// <summary>Tests the service of asynchronous runs with fake ports. A test starts its own workers.</summary>
public sealed class AsyncRunServiceTests : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _workers = [];
    private readonly FakeRunStore _store = new();
    private readonly FakeWebhookSender _webhooks = new();
    private readonly FakePayloadWriter _writer = new();

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Cancel();
        Task.WhenAll(_workers).GetAwaiter().GetResult();
        _stop.Dispose();
    }

    private ServiceProvider Build(FakeRetrievalClient? rag = null, FakeLlmClient? llm = null, int queueLimit = 16)
    {
        return TestHost.Build(rag, llm, configure: services =>
        {
            services.AddSingleton<IRunStore>(_store);
            services.AddSingleton<IWebhookSender>(_webhooks);
            services.AddSingleton<IWebhookPolicy, FakeWebhookPolicy>();
            services.AddSingleton<IWebhookPayloadWriter>(_writer);
            services.Configure<JobsOptions>(options => options.QueueLimit = queueLimit);
        });
    }

    private void StartWorkers(AsyncRunService service, int count = 1, int deliveryWorkers = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _workers.Add(Task.Run(() => service.RunWorkerAsync(_stop.Token)));
        }

        for (var i = 0; i < deliveryWorkers; i++)
        {
            _workers.Add(Task.Run(() => service.RunDeliveryWorkerAsync(_stop.Token)));
        }
    }

    private static async Task<RunRecord> WaitForAsync(AsyncRunService service, string runId, Func<RunRecord, bool> condition)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var found = service.Find(runId);
            if (found.IsSuccess && condition(found.Value!))
            {
                return found.Value!;
            }

            await Task.Delay(10, limit.Token);
        }
    }

    private static Task<RunRecord> WaitFinishedAsync(AsyncRunService service, string runId) =>
        WaitForAsync(service, runId, record => record.IsFinished);

    /// <summary>
    /// A cancelled queued run leaves the queue at once, so it frees its place for a new run. Its webhook goes out once,
    /// and a worker never executes it.
    /// </summary>
    [Fact]
    public async Task CancelledQueuedRun_FreesItsPlace_AndSendsOneWebhook()
    {
        var rag = new FakeRetrievalClient();
        using var services = Build(rag, queueLimit: 2);
        var service = services.GetRequiredService<AsyncRunService>();
        var first = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"one"}"""), "https://hooks.example/x", debug: false).Value!;
        service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"two"}"""), null, debug: false);
        Assert.Equal("jobs.queue_full", service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"three"}"""), null, debug: false).Error!.Code);

        var cancelled = service.Cancel(first.RunId);
        var accepted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"four"}"""), null, debug: false);
        StartWorkers(service);

        Assert.Equal(RunState.Cancelled, cancelled.Value!.State);
        Assert.True(accepted.IsSuccess);
        await WaitForAsync(service, first.RunId, record => record.Webhook.State == WebhookState.Delivered);
        await WaitFinishedAsync(service, accepted.Value!.RunId);
        Assert.Single(_webhooks.Calls, call => call.RunId == first.RunId);
        Assert.DoesNotContain(rag.Queries, query => query.Question == "one");
    }

    /// <summary>A run that still executes when the service stops ends as cancelled with the code run.service_stopped.</summary>
    [Fact]
    public async Task RunDuringStop_EndsWithServiceStopped()
    {
        var started = new TaskCompletionSource();
        var llm = new FakeLlmClient
        {
            OnComplete = async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Result<Completion>.Fail(new PipelineError("never", "Never.", ErrorKind.Internal));
            },
        };
        using var services = Build(llm: llm);
        var service = services.GetRequiredService<AsyncRunService>();
        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), null, debug: false).Value!;
        StartWorkers(service);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        await _stop.CancelAsync();
        var finished = await WaitFinishedAsync(service, submitted.RunId);

        Assert.Equal(RunState.Cancelled, finished.State);
        Assert.Equal("run.service_stopped", finished.Error!.Code);
    }

    /// <summary>An asynchronous run never streams, so a request for a stream is refused at submit.</summary>
    [Fact]
    public void StreamRequest_IsRefusedAtSubmit()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), null, debug: false, stream: true);

        Assert.Equal("stream.not_supported_async", submitted.Error!.Code);
    }

    /// <summary>A submitted run is queued at first. A worker runs it, and the record then holds the answer and the times.</summary>
    [Fact]
    public async Task SubmittedRun_IsQueued_ThenSucceeds()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Which port?"}"""), null, debug: false);

        Assert.True(submitted.IsSuccess);
        Assert.Equal(RunState.Queued, submitted.Value!.State);
        Assert.Null(submitted.Value.StartedAt);
        StartWorkers(service);
        var finished = await WaitFinishedAsync(service, submitted.Value.RunId);

        Assert.Equal(RunState.Succeeded, finished.State);
        Assert.Equal("The orders service uses port 8080 [1].", finished.Result!.Answer.Text);
        Assert.NotNull(finished.StartedAt);
        Assert.True(finished.CompletedAt >= finished.StartedAt);
        Assert.Equal(WebhookState.None, finished.Webhook.State);
    }

    /// <summary>A run that fails keeps its error in the record.</summary>
    [Fact]
    public async Task FailedRun_KeepsTheError()
    {
        var rag = new FakeRetrievalClient { OnRetrieve = _ => Result<RetrievedSet>.Fail(new PipelineError("xat-rag.timeout", "slow", ErrorKind.Timeout, Source: "xat-rag")) };
        using var services = Build(rag);
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service);

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), null, debug: false);
        var finished = await WaitFinishedAsync(service, submitted.Value!.RunId);

        Assert.Equal(RunState.Failed, finished.State);
        Assert.Equal("xat-rag.timeout", finished.Error!.Code);
        Assert.Null(finished.Result);
    }

    /// <summary>The service checks the input at submit, so the caller gets the error at once and no run is stored.</summary>
    [Fact]
    public void InvalidInput_IsRefusedAtSubmit()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"nothing":1}"""), null, debug: false);

        Assert.Equal("input.invalid", submitted.Error!.Code);
        Assert.Empty(_store.All);
    }

    /// <summary>A webhook goes out once, when the run ends. The stored run shows only the scheme, the host and the port, because the path can hold a secret.</summary>
    [Fact]
    public async Task Webhook_IsSentOnce_AndTheStoredAddressHasNoQuery()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service);

        var submitted = service.Submit(
            TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), "https://hooks.example:8443/path/SECRET-IN-PATH?token=SECRET", debug: false);
        var finished = await WaitForAsync(service, submitted.Value!.RunId, record => record.Webhook.State == WebhookState.Delivered);

        Assert.Equal("https://hooks.example:8443", finished.WebhookTarget);
        var call = Assert.Single(_webhooks.Calls);
        Assert.Equal("https://hooks.example:8443/path/SECRET-IN-PATH?token=SECRET", call.Url.ToString());
        Assert.Equal($"{finished.RunId}|Succeeded|Pending", call.Body);
        Assert.Equal(1, finished.Webhook.Attempts);
    }

    /// <summary>A webhook that fails is recorded as failed. The run keeps its result.</summary>
    [Fact]
    public async Task FailedWebhook_IsRecorded_AndTheRunStaysSucceeded()
    {
        _webhooks.Result = new WebhookDelivery(WebhookState.Failed, 4, "The receiver is not reachable.");
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service);

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), "https://hooks.example/x", debug: false);
        var finished = await WaitForAsync(service, submitted.Value!.RunId, record => record.Webhook.State == WebhookState.Failed);

        Assert.Equal(RunState.Succeeded, finished.State);
        Assert.Equal((4, "The receiver is not reachable."), (finished.Webhook.Attempts, finished.Webhook.Error));
    }

    /// <summary>The webhook policy refuses an address at submit. The error never repeats the address and no run is stored.</summary>
    [Fact]
    public void WebhookAddressRefusedByThePolicy_IsRefusedAtSubmit()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), "https://blocked.test/secret-path", debug: false);

        Assert.Equal("webhook.host_not_allowed", submitted.Error!.Code);
        Assert.DoesNotContain("secret-path", submitted.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_store.All);
    }

    /// <summary>
    /// A receiver that hangs must not block the runs. The delivery has its own worker, so all runs still finish
    /// while the first webhook waits for its receiver.
    /// </summary>
    [Fact]
    public async Task SlowWebhookReceiver_DoesNotBlockTheRuns()
    {
        var gate = new TaskCompletionSource();
        _webhooks.Gate = gate.Task;
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service, count: 1, deliveryWorkers: 1);

        var ids = Enumerable.Range(0, 4)
            .Select(i => service.Submit(TestData.RagQa(), TestData.Json($$"""{"prompt":"Q{{i}}"}"""), "https://hooks.example/x", debug: false).Value!.RunId)
            .ToList();
        var finished = await Task.WhenAll(ids.Select(id => WaitFinishedAsync(service, id)));

        Assert.All(finished, record => Assert.Equal(RunState.Succeeded, record.State));
        Assert.All(ids, id => Assert.Equal(WebhookState.Pending, service.Find(id).Value!.Webhook.State));
        gate.SetResult();
        await WaitForAsync(service, ids[^1], record => record.Webhook.State == WebhookState.Delivered);
    }

    /// <summary>A failure outside the sender, here in the payload writer, ends the webhook as failed. It never stays pending.</summary>
    [Fact]
    public async Task FailureOutsideTheSender_EndsTheWebhookAsFailed()
    {
        _writer.Throw = true;
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service);

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), "https://hooks.example/x", debug: false);
        var finished = await WaitForAsync(service, submitted.Value!.RunId, record => record.Webhook.State == WebhookState.Failed);

        Assert.Equal(RunState.Succeeded, finished.State);
        Assert.Contains("internal error", finished.Webhook.Error, StringComparison.Ordinal);
        Assert.Empty(_webhooks.Calls);
    }

    /// <summary>A pipeline that always streams cannot run in asynchronous mode. The service refuses it at submit.</summary>
    [Fact]
    public void PipelineThatAlwaysStreams_IsRefusedInAsyncMode()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();
        var pipeline = TestData.Pipeline(
            "streams",
            [TestData.Step("g", "llm.generate")],
            stream: Xat.Orchestrator.Domain.Pipelines.StreamMode.Required);

        var submitted = service.Submit(pipeline, TestData.Json("{}"), null, debug: false);

        Assert.Equal("stream.not_supported_async", submitted.Error!.Code);
        Assert.Empty(_store.All);
    }

    /// <summary>A queued run can be cancelled before any worker takes it. The worker then skips it and the webhook still reports the end.</summary>
    [Fact]
    public async Task CancelQueuedRun_SkipsTheRun_AndStillSendsTheWebhook()
    {
        var llm = new FakeLlmClient();
        using var services = Build(llm: llm);
        var service = services.GetRequiredService<AsyncRunService>();
        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), "https://hooks.example/x", debug: false);

        var cancelled = service.Cancel(submitted.Value!.RunId);

        Assert.Equal(RunState.Cancelled, cancelled.Value!.State);
        Assert.Equal("run.cancelled", cancelled.Value.Error!.Code);
        StartWorkers(service);
        var finished = await WaitForAsync(service, submitted.Value.RunId, record => record.Webhook.State == WebhookState.Delivered);

        Assert.Equal(RunState.Cancelled, finished.State);
        Assert.Empty(llm.Requests);
        Assert.Equal($"{finished.RunId}|Cancelled|Pending", Assert.Single(_webhooks.Calls).Body);
    }

    /// <summary>A running run stops when the caller cancels it, and the cancel reaches the call to the LLM.</summary>
    [Fact]
    public async Task CancelRunningRun_StopsTheUpstreamCall()
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
        using var services = Build(llm: llm);
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service);
        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), null, debug: false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        service.Cancel(submitted.Value!.RunId);
        var finished = await WaitFinishedAsync(service, submitted.Value.RunId);

        await sawCancel.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(RunState.Cancelled, finished.State);
        Assert.Equal(ErrorKind.Cancelled, finished.Error!.Kind);
    }

    /// <summary>A cancel of a finished run changes nothing.</summary>
    [Fact]
    public async Task CancelFinishedRun_ChangesNothing()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service);
        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), null, debug: false);
        var finished = await WaitFinishedAsync(service, submitted.Value!.RunId);

        var again = service.Cancel(finished.RunId);

        Assert.Equal(RunState.Succeeded, again.Value!.State);
    }

    /// <summary>A full queue refuses the run with an overloaded error and a retry time, and the refused run is not stored.</summary>
    [Fact]
    public void FullQueue_RefusesTheRun_WithRetryAfter()
    {
        using var services = Build(queueLimit: 1);
        var service = services.GetRequiredService<AsyncRunService>();
        var first = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"one"}"""), null, debug: false);

        var second = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"two"}"""), null, debug: false);

        Assert.True(first.IsSuccess);
        Assert.Equal(ErrorKind.Overloaded, second.Error!.Kind);
        Assert.Equal("jobs.queue_full", second.Error.Code);
        Assert.Equal(TimeSpan.FromSeconds(5), second.Error.RetryAfter);
        Assert.Single(_store.All);
    }

    /// <summary>An unknown run gives a not-found error for read and for cancel.</summary>
    [Fact]
    public void UnknownRun_IsNotFound()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();

        Assert.Equal(ErrorKind.NotFound, service.Find("run_none").Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, service.Cancel("run_none").Error!.Kind);
    }

    /// <summary>Several workers take many runs, and every run ends.</summary>
    [Fact]
    public async Task ManyRuns_AreAllProcessed_ByManyWorkers()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();
        StartWorkers(service, count: 3);

        var ids = Enumerable.Range(0, 12)
            .Select(i => service.Submit(TestData.RagQa(), TestData.Json($$"""{"prompt":"Q{{i}}"}"""), null, debug: false).Value!.RunId)
            .ToList();
        var finished = await Task.WhenAll(ids.Select(id => WaitFinishedAsync(service, id)));

        Assert.All(finished, record => Assert.Equal(RunState.Succeeded, record.State));
    }

    /// <summary>The debug flag goes into the record, so the API can show the step times.</summary>
    [Fact]
    public void DebugFlag_IsStored()
    {
        using var services = Build();
        var service = services.GetRequiredService<AsyncRunService>();

        var submitted = service.Submit(TestData.RagQa(), TestData.Json("""{"prompt":"Q"}"""), null, debug: true);

        Assert.True(submitted.Value!.Debug);
    }
}
