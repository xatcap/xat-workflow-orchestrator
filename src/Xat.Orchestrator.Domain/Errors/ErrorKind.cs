namespace Xat.Orchestrator.Domain.Errors;

/// <summary>The class of an expected failure. The Api layer maps each kind to one HTTP status.</summary>
public enum ErrorKind
{
    /// <summary>The input of the caller is not valid.</summary>
    Validation,

    /// <summary>A pipeline, run, context or model does not exist.</summary>
    NotFound,

    /// <summary>The state of an upstream or of a run does not allow the request.</summary>
    Conflict,

    /// <summary>The orchestrator or an upstream has no free capacity. The caller can try again later.</summary>
    Overloaded,

    /// <summary>An upstream limits the rate of requests.</summary>
    RateLimited,

    /// <summary>A step or the run passed its time limit.</summary>
    Timeout,

    /// <summary>An upstream is not reachable or not ready.</summary>
    UpstreamUnavailable,

    /// <summary>An upstream returned a failure that is not covered by another kind.</summary>
    UpstreamFailed,

    /// <summary>The work stopped because the caller or an operator cancelled it.</summary>
    Cancelled,

    /// <summary>A failure inside the orchestrator, for example a bug or a wrong pipeline setup. The caller cannot fix it.</summary>
    Internal,
}
