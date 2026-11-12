using System.Text.Json;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Engine.Runs;

/// <summary>Starts a run that answers in the request, with one complete answer or with a stream.</summary>
/// <param name="catalog">The pipelines.</param>
/// <param name="runner">The pipeline runner.</param>
public sealed class RunStarter(IPipelineCatalog catalog, PipelineRunner runner)
{
    /// <summary>Finds a pipeline by its name.</summary>
    /// <param name="name">The name of the pipeline.</param>
    /// <returns>The pipeline, or a not-found error.</returns>
    public Result<PipelineDefinition> Find(string name) => catalog.Find(name);

    /// <summary>Checks the stream mode and starts the run. The run goes on in the background.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="input">The input of the caller.</param>
    /// <param name="stream">Shows whether the caller wants a stream.</param>
    /// <param name="ct">The token that cancels the run.</param>
    /// <returns>The handle of the run, or an error when the request does not fit the pipeline.</returns>
    public Result<RunHandle> Start(PipelineDefinition pipeline, JsonElement input, bool stream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return RunAdmission.CheckSync(pipeline, stream) is { } error
            ? Result<RunHandle>.Fail(error)
            : Result<RunHandle>.Ok(runner.Start(pipeline, input, new RunRequest(stream), ct));
    }
}
