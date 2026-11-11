using Xat.Orchestrator.Application.Engine.Events;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.State;

/// <summary>The data of one run that every step can reach.</summary>
public sealed class PipelineContext
{
    /// <summary>Gets the ID of the run.</summary>
    public required string RunId { get; init; }

    /// <summary>Gets the pipeline that the run executes.</summary>
    public required PipelineDefinition Pipeline { get; init; }

    /// <summary>Gets the state of the run.</summary>
    public required PipelineState State { get; init; }

    /// <summary>Gets the sink that receives the events of the run.</summary>
    public required IRunEventSink Events { get; init; }

    /// <summary>Gets the clock. Use it in place of the system time.</summary>
    public required TimeProvider Clock { get; init; }
}
