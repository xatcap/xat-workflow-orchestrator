using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Api.Endpoints;

/// <summary>Rules that the run routes share.</summary>
internal static class RunRules
{
    /// <summary>The name of the rate limit policy for runs.</summary>
    public const string RateLimitPolicy = "runs";

    /// <summary>Checks that the stream option of a request fits the stream mode of the pipeline.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="stream">Shows whether the caller wants a stream.</param>
    /// <returns>An error, or <see langword="null"/> when the request fits the pipeline.</returns>
    public static PipelineError? CheckStreamMode(PipelineDefinition pipeline, bool stream) =>
        (pipeline.Stream, stream) switch
        {
            (StreamMode.Never, true) => new PipelineError(
                "stream.not_supported", "This pipeline does not support a stream. Set stream to false.", ErrorKind.Validation),
            (StreamMode.Required, false) => new PipelineError(
                "stream.required", "This pipeline sends a stream only. Set stream to true.", ErrorKind.Validation),
            _ => null,
        };
}
