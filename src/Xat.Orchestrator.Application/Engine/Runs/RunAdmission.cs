using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Engine.Runs;

/// <summary>The rules that decide whether a request for a stream fits the stream mode of a pipeline. Both run modes use them.</summary>
public static class RunAdmission
{
    /// <summary>Checks a run that answers in the request: with one complete answer or with a stream.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="stream">Shows whether the caller wants a stream.</param>
    /// <returns>An error, or <see langword="null"/> when the request fits the pipeline.</returns>
    public static PipelineError? CheckSync(PipelineDefinition pipeline, bool stream)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return (pipeline.Stream, stream) switch
        {
            (StreamMode.Never, true) => new PipelineError(
                "stream.not_supported", "This pipeline does not support a stream. Set stream to false.", ErrorKind.Validation),
            (StreamMode.Required, false) => new PipelineError(
                "stream.required", "This pipeline sends a stream only. Set stream to true.", ErrorKind.Validation),
            _ => null,
        };
    }

    /// <summary>Checks an asynchronous run. Such a run never streams.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="stream">Shows whether the caller asked for a stream.</param>
    /// <returns>An error, or <see langword="null"/> when the run can be asynchronous.</returns>
    public static PipelineError? CheckAsync(PipelineDefinition pipeline, bool stream)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (stream)
        {
            return new PipelineError(
                "stream.not_supported_async", "An asynchronous run does not stream. Set stream to false.", ErrorKind.Validation);
        }

        return pipeline.Stream == StreamMode.Required
            ? new PipelineError(
                "stream.not_supported_async", "This pipeline sends a stream only, so it cannot run in asynchronous mode.", ErrorKind.Validation)
            : null;
    }
}
