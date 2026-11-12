namespace Xat.Orchestrator.Application.Abstractions.Runs;

/// <summary>The addresses on which the orchestrator listens. The webhook rule uses them, so a run cannot send its webhook to the orchestrator itself.</summary>
public interface IListenAddresses
{
    /// <summary>Gets the addresses that the server bound. The list is empty before the server starts.</summary>
    IReadOnlyCollection<Uri> Current { get; }
}
