using System.Diagnostics;
using Xat.Orchestrator.Application.Engine.Execution;

namespace Xat.Orchestrator.Application.Engine.Hooks;

/// <summary>Makes one trace span for each step. The spans use the name of the pipeline and of the step, and never the data of the run.</summary>
internal sealed class StepActivityMiddleware : IStepMiddleware
{
    /// <summary>The name of the activity source. An OpenTelemetry setup adds this name to collect the spans.</summary>
    public const string SourceName = "Xat.Orchestrator.Pipelines";

    private static readonly ActivitySource _source = new(SourceName);

    /// <inheritdoc/>
    public async ValueTask<StepOutcome> InvokeAsync(StepContext context, StepInvocation nextStep, CancellationToken ct)
    {
        using var activity = _source.StartActivity("pipeline.step");
        activity?.SetTag("pipeline.name", context.Run.Pipeline.Name);
        activity?.SetTag("pipeline.step.id", context.Step.Id);
        activity?.SetTag("pipeline.step.uses", context.Step.Uses);

        var outcome = await nextStep(ct);
        if (outcome is StepOutcome.FailedOutcome failed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, failed.Error.Code);
        }

        return outcome;
    }
}
