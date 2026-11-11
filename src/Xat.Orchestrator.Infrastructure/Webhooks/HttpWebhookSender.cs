using System.Text;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Runs;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Webhooks;

/// <summary>
/// Sends a webhook with an HTTP POST. A Polly pipeline retries a failed try after a connection error, a time-out, or the status
/// 408, 429 or 5xx. A status that a retry does not change, for example 400, ends the delivery at once. The sender never follows redirects.
/// </summary>
internal sealed class HttpWebhookSender : IWebhookSender
{
    private readonly IHttpClientFactory _factory;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Initializes a new instance of the <see cref="HttpWebhookSender"/> class.</summary>
    /// <param name="factory">The factory of HTTP clients.</param>
    /// <param name="options">The webhook settings.</param>
    public HttpWebhookSender(IHttpClientFactory factory, IOptions<WebhookOptions> options)
    {
        _factory = factory;
        var settings = options.Value;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = settings.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(settings.RetryDelayMilliseconds),
                MaxDelay = TimeSpan.FromSeconds(30),
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<WebhookRetryableException>(),
            })
            .AddTimeout(TimeSpan.FromSeconds(settings.TimeoutSeconds))
            .Build();
    }

    /// <inheritdoc/>
    public async Task<WebhookDelivery> DeliverAsync(Uri url, string runId, string jsonBody, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        var attempts = 0;
        try
        {
            await _pipeline.ExecuteAsync(
                async token =>
                {
                    attempts++;
                    using var client = _factory.CreateClient(DependencyInjection.WebhookClient);
                    using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                    request.Headers.Add("X-Run-Id", runId);
                    request.Headers.Add("X-Webhook-Event", "run.finished");

                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    var status = (int)response.StatusCode;
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }

                    throw status is 408 or 429 or >= 500 ? new WebhookRetryableException(status) : new WebhookRejectedException(status);
                },
                ct);

            return new WebhookDelivery(WebhookState.Delivered, attempts, null);
        }
        catch (WebhookRejectedException exception)
        {
            return Failed(attempts, $"The receiver refused the webhook with status {exception.Status}.");
        }
        catch (WebhookRetryableException exception)
        {
            return Failed(attempts, $"The receiver still answered with status {exception.Status} after the retries.");
        }
        catch (HttpRequestException)
        {
            return Failed(attempts, "The receiver is not reachable.");
        }
        catch (TimeoutRejectedException)
        {
            return Failed(attempts, "The receiver did not answer in time.");
        }
    }

    private static WebhookDelivery Failed(int attempts, string error) => new(WebhookState.Failed, attempts, error);
}
