using Xat.Orchestrator.Application.Engine.Events;
using Xat.Orchestrator.Domain.Pipelines;

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

    /// <summary>Gets the time limit of the run.</summary>
    public required TimeSpan RunTimeout { get; init; }

    /// <summary>Gets the time stamp of the clock when the run started.</summary>
    public required long StartTimestamp { get; init; }

    /// <summary>Gets the time that the run has used.</summary>
    /// <returns>The elapsed time.</returns>
    public TimeSpan Elapsed() => Clock.GetElapsedTime(StartTimestamp);

    /// <summary>Gets the time that is left before the run passes its time limit.</summary>
    /// <returns>The time that is left, or zero.</returns>
    public TimeSpan Remaining()
    {
        var left = RunTimeout - Elapsed();
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
