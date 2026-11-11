using System.Net;
using System.Text.Json;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>
/// Turns the errors of an upstream service into <see cref="PipelineError"/> values. Both upstream services use the body
/// <c>{"error":{"message","type"}</c> and send <c>Retry-After</c> with 429 and 503. The anti-corruption rule applies:
/// no upstream type or status code leaves the adapter.
/// </summary>
internal static class UpstreamErrors
{
    private const int MaxMessageLength = 300;

    /// <summary>Reads the error of a response that is not a success.</summary>
    /// <param name="response">The response.</param>
    /// <param name="upstream">The name of the upstream, for example <c>xat-rag</c>.</param>
    /// <param name="ct">The token that cancels the read.</param>
    /// <returns>The error.</returns>
    public static async Task<PipelineError> FromResponseAsync(HttpResponseMessage response, string upstream, CancellationToken ct)
    {
        string? type = null;
        string? message = null;
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                type = error.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
                message = error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() : null;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // The body is not the error format. This happens for a rejected host name. Use the status code.
        }

        return Map(response.StatusCode, type, message, RetryAfterOf(response), upstream);
    }

    /// <summary>Maps the status code and the error type of an upstream to an error.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="type">The error type from the body, or <see langword="null"/>.</param>
    /// <param name="message">The message from the body, or <see langword="null"/>.</param>
    /// <param name="retryAfter">The value of the <c>Retry-After</c> header, or <see langword="null"/>.</param>
    /// <param name="upstream">The name of the upstream.</param>
    /// <returns>The error.</returns>
    public static PipelineError Map(HttpStatusCode status, string? type, string? message, TimeSpan? retryAfter, string upstream)
    {
        var kind = status switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType => ErrorKind.Validation,
            HttpStatusCode.NotFound => ErrorKind.NotFound,
            HttpStatusCode.Conflict => ErrorKind.Conflict,
            HttpStatusCode.TooManyRequests => ErrorKind.RateLimited,
            HttpStatusCode.ServiceUnavailable when type == "overloaded_error" => ErrorKind.Overloaded,
            HttpStatusCode.ServiceUnavailable => ErrorKind.UpstreamUnavailable,
            HttpStatusCode.GatewayTimeout => ErrorKind.Timeout,
            _ => ErrorKind.UpstreamFailed,
        };

        var code = $"{upstream}.{type ?? "http_" + (int)status}";
        var text = string.IsNullOrWhiteSpace(message)
            ? $"{upstream} answered with status {(int)status}."
            : $"{upstream} reported an error. {Truncate(message)}";
        return new PipelineError(code, text, kind, retryAfter, upstream);
    }

    /// <summary>Maps an exception from a call to an upstream. The method handles only failures of the call, not bugs.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="upstream">The name of the upstream.</param>
    /// <param name="callerToken">The token of the caller. A cancel by this token is not an upstream failure.</param>
    /// <param name="error">The error, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the exception is a failure of the upstream call.</returns>
    public static bool TryMap(Exception exception, string upstream, CancellationToken callerToken, out PipelineError error)
    {
        switch (exception)
        {
            case OperationCanceledException when callerToken.IsCancellationRequested:
                error = null!;
                return false;
            case Polly.Timeout.TimeoutRejectedException:
            case TimeoutException:
            case OperationCanceledException:
                error = new PipelineError($"{upstream}.timeout", $"{upstream} did not answer in time.", ErrorKind.Timeout, Source: upstream);
                return true;
            case HttpRequestException:
                error = new PipelineError(
                    $"{upstream}.unreachable", $"{upstream} is not reachable.", ErrorKind.UpstreamUnavailable, Source: upstream);
                return true;
            case JsonException:
                error = new PipelineError(
                    $"{upstream}.invalid_response", $"{upstream} sent an answer that the orchestrator cannot read.", ErrorKind.UpstreamFailed, Source: upstream);
                return true;
            default:
                error = null!;
                return false;
        }
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private static string Truncate(string text) =>
        text.Length <= MaxMessageLength ? text : text[..MaxMessageLength];
}
