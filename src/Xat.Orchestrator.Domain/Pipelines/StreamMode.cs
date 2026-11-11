namespace Xat.Orchestrator.Domain.Pipelines;

/// <summary>Shows whether a pipeline can send its answer as a stream.</summary>
public enum StreamMode
{
    /// <summary>The caller can ask for a stream or for one complete answer.</summary>
    Allowed,

    /// <summary>The pipeline always returns one complete answer.</summary>
    Never,

    /// <summary>The pipeline always sends a stream.</summary>
    Required,
}
