using System.Threading.Channels;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>A sink that drops every event. Runs of callers that do not stream use it.</summary>
public sealed class NullRunEventSink : IRunEventSink
{
    /// <summary>Gets the only instance.</summary>
    public static NullRunEventSink Instance { get; } = new();

    /// <inheritdoc/>
    public bool Streaming => false;

    /// <inheritdoc/>
    public ValueTask EmitAsync(RunEvent runEvent, CancellationToken ct) => ValueTask.CompletedTask;
}
