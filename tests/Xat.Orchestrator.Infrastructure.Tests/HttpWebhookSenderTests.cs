using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Infrastructure.Tests;

/// <summary>Tests the HTTP webhook sender against a fake receiver: retries, time limits and redirects.</summary>
public sealed class HttpWebhookSenderTests : IDisposable
{
    private readonly Harness _harness = new(new Dictionary<string, string?>
    {
        ["Webhooks:RetryCount"] = "2",
        ["Webhooks:RetryDelayMilliseconds"] = "10",
        ["Webhooks:TimeoutSeconds"] = "1",
    });

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    private IWebhookSender Sender => _harness.Get<IWebhookSender>();

    private Uri Target => new($"{_harness.Rag.Url}/hook");

    private Task<WebhookDelivery> SendAsync() => Sender.DeliverAsync(Target, "run_1", """{"event":"run.finished"}""", TestContext.Current.CancellationToken);

    private void Respond(int status) =>
        _harness.Rag.Given(Request.Create().WithPath("/hook").UsingPost()).RespondWith(Response.Create().WithStatusCode(status));

    /// <summary>A good delivery has one try. The request has the JSON body and the headers with the run ID and the event name.</summary>
    [Fact]
    public async Task Delivers_WithBodyAndHeaders()
    {
        Respond(200);

        var delivery = await SendAsync();

        Assert.Equal((WebhookState.Delivered, 1, null), (delivery.State, delivery.Attempts, delivery.Error));
        var entry = Assert.Single(_harness.Rag.LogEntries);
        Assert.Equal("""{"event":"run.finished"}""", entry.RequestMessage!.Body);
        Assert.Equal("run_1", entry.RequestMessage.Headers!["X-Run-Id"].ToString());
        Assert.Equal("run.finished", entry.RequestMessage.Headers["X-Webhook-Event"].ToString());
        Assert.StartsWith("application/json", entry.RequestMessage.Headers["Content-Type"].ToString(), StringComparison.Ordinal);
    }

    /// <summary>A 500 is tried again. The second try gives the delivery, and the result shows two tries.</summary>
    [Fact]
    public async Task Retries_AfterAServerError()
    {
        _harness.Rag.Given(Request.Create().WithPath("/hook").UsingPost()).InScenario("hook").WillSetStateTo("second")
            .RespondWith(Response.Create().WithStatusCode(500));
        _harness.Rag.Given(Request.Create().WithPath("/hook").UsingPost()).InScenario("hook").WhenStateIs("second")
            .RespondWith(Response.Create().WithStatusCode(200));

        var delivery = await SendAsync();

        Assert.Equal((WebhookState.Delivered, 2), (delivery.State, delivery.Attempts));
    }

    /// <summary>When every try fails with a status that a retry can change, the delivery fails after the first try and all retries.</summary>
    [Fact]
    public async Task GivesUp_AfterAllRetries()
    {
        Respond(503);

        var delivery = await SendAsync();

        Assert.Equal((WebhookState.Failed, 3), (delivery.State, delivery.Attempts));
        Assert.Contains("503", delivery.Error, StringComparison.Ordinal);
        Assert.Equal(3, _harness.Rag.LogEntries.Count);
    }

    /// <summary>A status that a retry does not change, for example 400, ends the delivery at once.</summary>
    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(410)]
    public async Task DoesNotRetry_AClientError(int status)
    {
        Respond(status);

        var delivery = await SendAsync();

        Assert.Equal((WebhookState.Failed, 1), (delivery.State, delivery.Attempts));
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), delivery.Error, StringComparison.Ordinal);
        Assert.Single(_harness.Rag.LogEntries);
    }

    /// <summary>The sender does not follow a redirect, because the new address could be an address that the policy refused.</summary>
    [Fact]
    public async Task DoesNotFollowRedirects()
    {
        _harness.Rag.Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", $"{_harness.Engine.Url}/internal"));

        var delivery = await SendAsync();

        Assert.Equal(WebhookState.Failed, delivery.State);
        Assert.Empty(_harness.Engine.LogEntries);
    }

    /// <summary>A receiver that does not answer in time is a failed delivery with a clear reason, after the retries.</summary>
    [Fact]
    public async Task SlowReceiver_GivesATimeoutReason()
    {
        _harness.Rag.Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(3)));

        var delivery = await SendAsync();

        Assert.Equal(WebhookState.Failed, delivery.State);
        Assert.Contains("did not answer in time", delivery.Error, StringComparison.Ordinal);
    }

    /// <summary>A receiver that is down is a failed delivery with a clear reason, and the sender does not throw.</summary>
    [Fact]
    public async Task ReceiverDown_GivesAnUnreachableReason()
    {
        _harness.Rag.Stop();

        var delivery = await SendAsync();

        Assert.Equal(WebhookState.Failed, delivery.State);
        Assert.Contains("not reachable", delivery.Error, StringComparison.Ordinal);
    }

    /// <summary>When the service stops, the delivery stops and the cancel goes to the caller.</summary>
    [Fact]
    public async Task Cancel_StopsTheDelivery()
    {
        _harness.Rag.Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender.DeliverAsync(Target, "run_1", "{}", cts.Token));
    }
}
