using Xat.Orchestrator.Api.Errors;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Api.Hosting;

/// <summary>
/// Refuses every request that has an <c>Origin</c> header. A browser adds this header to a request that a web page makes.
/// The orchestrator has no authentication, so a web page in the browser of the user could otherwise call it, for example with
/// a simple cross-site POST that needs no preflight and wipes the index of a RAG. xat-rag has the same rule for the same reason.
/// Programs, such as curl and HTTP client libraries for servers, send no <c>Origin</c> header, so they are not affected.
/// </summary>
internal sealed class BrowserOriginGuard(RequestDelegate next)
{
    /// <summary>Checks the request and calls the next step when it has no <c>Origin</c> header.</summary>
    /// <param name="http">The HTTP context.</param>
    /// <returns>A task that completes when the request is done.</returns>
    public Task InvokeAsync(HttpContext http)
    {
        if (!http.Request.Headers.ContainsKey("Origin"))
        {
            return next(http);
        }

        return ApiErrors.WriteAsync(
            http,
            new PipelineError(
                "request.origin_not_allowed",
                "A request that has an Origin header is not allowed, because it comes from a web page. Call the orchestrator from a program.",
                ErrorKind.Validation),
            null,
            http.RequestAborted,
            StatusCodes.Status403Forbidden);
    }
}
