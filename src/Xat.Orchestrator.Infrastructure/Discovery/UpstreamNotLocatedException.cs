using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>
/// Shows that the address of an upstream is not known. The type is a connection error, so the retry handler tries again
/// and reads the ready file again. <see cref="UpstreamErrors.TryMap"/> turns it into the error of the locator.
/// </summary>
/// <param name="error">The error of the locator.</param>
internal sealed class UpstreamNotLocatedException(PipelineError error)
    : HttpRequestException(HttpRequestError.ConnectionError, error.Message)
{
    /// <summary>Gets the error of the locator.</summary>
    public PipelineError Error { get; } = error;
}
