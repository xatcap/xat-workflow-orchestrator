using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xat.Orchestrator.Api.Contracts;

namespace Xat.Orchestrator.Api.Endpoints;

/// <summary>The health routes. Ready reports <c>degraded</c> when an upstream service is down, because the orchestrator itself still runs.</summary>
internal static class HealthEndpoints
{
    /// <summary>Maps the health routes.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Json(new { Status = "live" }, ApiJson.Options));

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteAsync,
        });

        return app;
    }

    private static Task WriteAsync(HttpContext http, HealthReport report)
    {
        var status = report.Status switch
        {
            HealthStatus.Healthy => "ready",
            HealthStatus.Degraded => "degraded",
            _ => "unavailable",
        };

        var body = new
        {
            Status = status,
            Upstreams = report.Entries.Select(entry => new
            {
                Name = entry.Key,
                Status = entry.Value.Status.ToString().ToLowerInvariant(),
                Description = entry.Value.Description,
            }).ToList(),
        };

        return http.Response.WriteAsJsonAsync(body, ApiJson.Options, "application/json", http.RequestAborted);
    }
}
