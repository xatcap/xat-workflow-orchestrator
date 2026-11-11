using System.Threading.Channels;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.Events;

/// <summary>An event that a run emits. A streaming caller receives the events as they happen.</summary>
public abstract record RunEvent
{
    private RunEvent()
    {
    }

    /// <summary>The run started.</summary>
    /// <param name="RunId">The ID of the run.</param>
    /// <param name="Pipeline">The name of the pipeline.</param>
    /// <param name="Version">The version of the pipeline.</param>
    public sealed record Started(string RunId, string Pipeline, int Version) : RunEvent;

    /// <summary>A step started.</summary>
    /// <param name="StepId">The ID of the step.</param>
    /// <param name="Uses">The registered name of the step type.</param>
    public sealed record StepStarted(string StepId, string Uses) : RunEvent;

    /// <summary>The model wrote a new part of the answer.</summary>
    /// <param name="Text">The new text.</param>
    public sealed record Delta(string Text) : RunEvent;

    /// <summary>The answer cites sources.</summary>
    /// <param name="Items">The cited sources.</param>
    public sealed record Citations(IReadOnlyList<Citation> Items) : RunEvent;

    /// <summary>The run ended with success. This is the last event.</summary>
    /// <param name="Result">The result of the run.</param>
    public sealed record Completed(RunResult Result) : RunEvent;

    /// <summary>The run ended with a failure. This is the last event.</summary>
    /// <param name="RunId">The ID of the run.</param>
    /// <param name="Error">The error.</param>
    public sealed record Failed(string RunId, PipelineError Error) : RunEvent;
}
