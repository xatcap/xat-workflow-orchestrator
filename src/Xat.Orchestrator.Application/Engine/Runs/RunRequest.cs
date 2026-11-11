namespace Xat.Orchestrator.Application.Engine.Runs;

/// <summary>What the caller asks of a run.</summary>
/// <param name="Stream">Shows whether the caller wants the events of the run as a stream.</param>
/// <param name="RunId">An ID that the caller of the runner made, or <see langword="null"/> for a new ID. An asynchronous run needs its ID before it starts.</param>
public sealed record RunRequest(bool Stream, string? RunId = null);
