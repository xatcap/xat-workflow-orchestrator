namespace Xat.Orchestrator.Domain.Runs;

/// <summary>The state of an asynchronous run.</summary>
public enum RunState
{
    /// <summary>The run waits for a free worker.</summary>
    Queued,

    /// <summary>A worker executes the run.</summary>
    Running,

    /// <summary>The run ended and has a result. The result can be an answer or a no-answer result.</summary>
    Succeeded,

    /// <summary>The run ended with an error.</summary>
    Failed,

    /// <summary>The caller or the service stopped the run.</summary>
    Cancelled,
}
