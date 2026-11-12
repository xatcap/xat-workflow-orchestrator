using System.Threading.Channels;

namespace Xat.Orchestrator.Application.Engine.Events;

/// <summary>A sink that writes the events to a channel. The caller reads the channel.</summary>
/// <param name="writer">The writer of the channel.</param>
public sealed class ChannelRunEventSink(ChannelWriter<RunEvent> writer) : IRunEventSink
{
    private long _emitted;

    /// <inheritdoc/>
    public bool Streaming => true;

    /// <inheritdoc/>
    public long EmittedCount => Interlocked.Read(ref _emitted);

    /// <inheritdoc/>
    public async ValueTask EmitAsync(RunEvent runEvent, CancellationToken ct)
    {
        await writer.WriteAsync(runEvent, ct);
        Interlocked.Increment(ref _emitted);
    }
}
