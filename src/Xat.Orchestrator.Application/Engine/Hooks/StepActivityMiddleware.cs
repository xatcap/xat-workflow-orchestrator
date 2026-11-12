using System.Diagnostics;
using Xat.Orchestrator.Application.Engine.Execution;

namespace Xat.Orchestrator.Application.Engine.Hooks;

/// <summary>
/// Makes one trace span for each step, also when the step runs more than once. The spans have the IDs and the names of the run,
/// the pipeline and the step, and the error code of a failure. They never have the data of the run.
/// The orchestrator sends the spans to no exporter. A tool that listens on <see cref="SourceName"/> can collect them.
/// </summary>
internal sealed class StepActivityMiddleware : IStepMiddleware
{
    /// <summary>The name of the activity source. A listener adds this name to collect the spans.</summary>
    public const string SourceName = "Xat.Orchestrator.Pipelines";

    private static readonly ActivitySource _source = new(SourceName);

    /// <inheritdoc/>
    public async ValueTask<StepOutcome> InvokeAsync(StepContext context, StepInvocation nextStep, CancellationToken ct)
    {
        using var activity = _source.StartActivity("pipeline.step");
        activity?.SetTag("pipeline.run.id", context.Run.RunId);
        activity?.SetTag("pipeline.name", context.Run.Pipeline.Name);
        activity?.SetTag("pipeline.step.id", context.Step.Id);
        activity?.SetTag("pipeline.step.uses", context.Step.Uses);

        var outcome = await nextStep(ct);
        if (outcome is StepOutcome.FailedOutcome failed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, failed.Error.Code);
            activity?.SetTag("error.code", failed.Error.Code);
        }

        return outcome;
    }
}
