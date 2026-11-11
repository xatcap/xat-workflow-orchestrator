using Microsoft.AspNetCore.Diagnostics;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Api.Errors;

/// <summary>
/// The one place that handles unexpected exceptions. The response has the normal error form and never shows the exception.
/// Expected failures are values (<see cref="Result"/>), so an exception here is a bug.
/// </summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        PipelineError error;
        int? status = null;
        if (exception is BadHttpRequestException badRequest)
        {
            // A body above the size limit is 413. Other bad requests are 400.
            status = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest;
            error = status == StatusCodes.Status413PayloadTooLarge
                ? new PipelineError("request.too_large", "The request body is larger than the limit.", ErrorKind.Validation)
                : new PipelineError("request.invalid", "The request is not valid.", ErrorKind.Validation);
        }
        else
        {
            ExceptionLog.Unhandled(logger, exception);
            error = new PipelineError("internal.error", "The request failed because of an internal error.", ErrorKind.Internal);
        }

        await ApiErrors.WriteAsync(httpContext, error, null, cancellationToken, status);
        return true;
    }
}
