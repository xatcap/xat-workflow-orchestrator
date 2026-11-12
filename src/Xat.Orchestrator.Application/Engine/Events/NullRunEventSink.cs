namespace Xat.Orchestrator.Application.Engine.Events;

/// <summary>A sink that drops every event. Runs of callers that do not stream use it.</summary>
public sealed class NullRunEventSink : IRunEventSink
{
    /// <summary>Gets the only instance.</summary>
    public static NullRunEventSink Instance { get; } = new();

    /// <inheritdoc/>
    public bool Streaming => false;

    /// <inheritdoc/>
    /// <remarks>Nobody sees the dropped events, so the count is always 0.</remarks>
    public long EmittedCount => 0;

    /// <inheritdoc/>
    public ValueTask EmitAsync(RunEvent runEvent, CancellationToken ct) => ValueTask.CompletedTask;
}
