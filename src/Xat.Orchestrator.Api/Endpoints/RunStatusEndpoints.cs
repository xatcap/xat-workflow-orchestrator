using Xat.Orchestrator.Api.Contracts;
using Xat.Orchestrator.Application.Jobs;

namespace Xat.Orchestrator.Api.Endpoints;

/// <summary>The routes that read and cancel an asynchronous run.</summary>
internal static class RunStatusEndpoints
{
    /// <summary>Maps the routes.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapRunStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/runs/{id}", async (string id, AsyncRunService runs, HttpContext http) =>
        {
            var run = runs.Find(id);
            if (!run.IsSuccess)
            {
                await ApiErrors.WriteAsync(http, run.Error, null, http.RequestAborted);
                return;
            }

            await http.Response.WriteAsJsonAsync(RunStatusDto.From(run.Value), ApiJson.Options, "application/json", http.RequestAborted);
        });

        // A queued run changes to cancelled at once. A running run stops soon after, so the caller reads the state again.
        app.MapDelete("/v1/runs/{id}", async (string id, AsyncRunService runs, HttpContext http) =>
        {
            var run = runs.Cancel(id);
            if (!run.IsSuccess)
            {
                await ApiErrors.WriteAsync(http, run.Error, null, http.RequestAborted);
                return;
            }

            await http.Response.WriteAsJsonAsync(RunStatusDto.From(run.Value), ApiJson.Options, "application/json", http.RequestAborted);
        });

        return app;
    }
}
