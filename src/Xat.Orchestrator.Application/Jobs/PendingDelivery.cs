using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>A webhook that waits for delivery. A separate queue and separate workers deliver it, so a slow receiver never blocks a run.</summary>
/// <param name="Item">The run, with the address of the webhook.</param>
/// <param name="Finished">The run in its final state.</param>
internal sealed record PendingDelivery(QueuedRun Item, RunRecord Finished);
