namespace Xat.Orchestrator.Domain.Runs;

/// <summary>Shows how a successful run ended.</summary>
public enum RunStatus
{
    /// <summary>The model wrote the answer.</summary>
    Answered,

    /// <summary>A step ended the run early, because it had no basis for an answer.</summary>
    NoAnswer,
}
