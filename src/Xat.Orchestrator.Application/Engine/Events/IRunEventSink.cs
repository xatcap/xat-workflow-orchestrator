namespace Xat.Orchestrator.Application.Engine.Events;

/// <summary>Receives the events of a run.</summary>
public interface IRunEventSink
{
    /// <summary>Gets a value that shows whether the caller reads a stream. Steps that can stream must then send deltas.</summary>
    bool Streaming { get; }

    /// <summary>
    /// Gets the number of events that the sink took. The engine uses it to see whether a step already sent a part of its result
    /// to the caller. Such a step must not run again.
    /// </summary>
    long EmittedCount { get; }

    /// <summary>Sends an event.</summary>
    /// <param name="runEvent">The event.</param>
    /// <param name="ct">The token that cancels the send.</param>
    /// <returns>A task that completes when the sink has taken the event.</returns>
    ValueTask EmitAsync(RunEvent runEvent, CancellationToken ct);
}
