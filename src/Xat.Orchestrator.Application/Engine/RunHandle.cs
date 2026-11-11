using System.Threading.Channels;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>A run that started.</summary>
/// <param name="RunId">The ID of the run.</param>
/// <param name="Events">The reader of the events, or <see langword="null"/> when the caller did not ask for a stream.</param>
/// <param name="Completion">A task that completes when the run ends. It never throws for an expected failure.</param>
public sealed record RunHandle(string RunId, ChannelReader<RunEvent>? Events, Task<Result<RunResult>> Completion);
