namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>The part of an upstream call in which a failure occurs.</summary>
internal enum UpstreamCallPhase
{
    /// <summary>The request, or the read of a body that is not a stream.</summary>
    Request,

    /// <summary>The read of a stream, after the upstream sent the headers.</summary>
    StreamBody,
}
