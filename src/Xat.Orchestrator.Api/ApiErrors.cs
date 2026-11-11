using System.Text.Json;
using Xat.Orchestrator.Api.Contracts;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Api;

/// <summary>Turns a <see cref="PipelineError"/> into an HTTP response. All routes use this class, so every error has the same form.</summary>
internal static class ApiErrors
{
    /// <summary>Gets the HTTP status and the error type for a kind of error.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The status code and the type name.</returns>
    public static (int Status, string Type) Describe(ErrorKind kind) =>
        kind switch
        {
            ErrorKind.Validation => (StatusCodes.Status400BadRequest, "invalid_request_error"),
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "not_found_error"),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "conflict_error"),
            ErrorKind.Overloaded => (StatusCodes.Status503ServiceUnavailable, "overloaded_error"),
            ErrorKind.RateLimited => (StatusCodes.Status429TooManyRequests, "rate_limit_error"),
            ErrorKind.Timeout => (StatusCodes.Status504GatewayTimeout, "timeout_error"),
            ErrorKind.UpstreamUnavailable => (StatusCodes.Status503ServiceUnavailable, "service_unavailable_error"),
            ErrorKind.UpstreamFailed => (StatusCodes.Status502BadGateway, "upstream_error"),
            ErrorKind.Cancelled => (499, "cancelled_error"),
            ErrorKind.Internal => (StatusCodes.Status500InternalServerError, "internal_error"),
            _ => (StatusCodes.Status500InternalServerError, "internal_error"),
        };

    /// <summary>Makes the body of an error response.</summary>
    /// <param name="error">The error.</param>
    /// <param name="runId">The ID of the run, or <see langword="null"/>.</param>
    /// <returns>The body.</returns>
    public static ErrorEnvelope ToEnvelope(PipelineError error, string? runId) =>
        new(new ErrorBody(Describe(error.Kind).Type, error.Message, error.Code, runId));

    /// <summary>Writes an error response. The response has a <c>Retry-After</c> header when the caller can try again.</summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="error">The error.</param>
    /// <param name="runId">The ID of the run, or <see langword="null"/>.</param>
    /// <param name="ct">The token that cancels the write.</param>
    /// <param name="statusOverride">An HTTP status that replaces the status of the error kind, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static Task WriteAsync(HttpContext http, PipelineError error, string? runId, CancellationToken ct, int? statusOverride = null)
    {
        ApplyStatus(http, error, statusOverride);
        return http.Response.WriteAsJsonAsync(ToEnvelope(error, runId), ApiJson.Options, "application/json", ct);
    }

    /// <summary>Writes an error response in the OpenAI form <c>{"error":{"message","type","param","code"}}</c>. The status and the <c>Retry-After</c> rules are the same as for <see cref="WriteAsync"/>.</summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="error">The error.</param>
    /// <param name="ct">The token that cancels the write.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static Task WriteOpenAiAsync(HttpContext http, PipelineError error, CancellationToken ct)
    {
        ApplyStatus(http, error, null);
        http.Response.ContentType = "application/json";
        return http.Response.WriteAsync(OpenAi.ChatCompletionJson.Error(error).ToJsonString(), System.Text.Encoding.UTF8, ct);
    }

    private static void ApplyStatus(HttpContext http, PipelineError error, int? statusOverride)
    {
        var status = statusOverride ?? Describe(error.Kind).Status;
        http.Response.StatusCode = status;

        // A 503 always tells the caller when to try again, like the upstream services do. The default is 5 seconds.
        var retryTime = error.RetryAfter ?? (status == StatusCodes.Status503ServiceUnavailable ? TimeSpan.FromSeconds(5) : null);
        if (retryTime is { } retryAfter && status is StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable)
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Serializes a value with the settings of the API.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), ApiJson.Options);
}
