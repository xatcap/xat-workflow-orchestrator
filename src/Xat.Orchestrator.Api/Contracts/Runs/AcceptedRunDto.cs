namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>The answer to a request for an asynchronous run.</summary>
/// <param name="RunId">The ID of the run.</param>
/// <param name="Status">The state of the run. The value is <c>queued</c>.</param>
/// <param name="StatusUrl">The path from which the caller reads the state, for example <c>/v1/runs/run_123</c>.</param>
internal sealed record AcceptedRunDto(string RunId, string Status, string StatusUrl);
