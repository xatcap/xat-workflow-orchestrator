using System.Threading.Channels;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.Events;

/// <summary>Receives the events of a run.</summary>
public interface IRunEventSink
{
    /// <summary>Gets a value that shows whether the caller reads a stream. Steps that can stream must then send deltas.</summary>
    bool Streaming { get; }

    /// <summary>Sends an event.</summary>
    /// <param name="runEvent">The event.</param>
    /// <param name="ct">The token that cancels the send.</param>
    /// <returns>A task that completes when the sink has taken the event.</returns>
    ValueTask EmitAsync(RunEvent runEvent, CancellationToken ct);
}
