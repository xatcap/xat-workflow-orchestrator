using System.Threading.Channels;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>A sink that writes the events to a channel. The caller reads the channel.</summary>
/// <param name="writer">The writer of the channel.</param>
public sealed class ChannelRunEventSink(ChannelWriter<RunEvent> writer) : IRunEventSink
{
    /// <inheritdoc/>
    public bool Streaming => true;

    /// <inheritdoc/>
    public ValueTask EmitAsync(RunEvent runEvent, CancellationToken ct) => writer.WriteAsync(runEvent, ct);
}
