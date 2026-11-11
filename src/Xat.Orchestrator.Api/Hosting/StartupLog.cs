namespace Xat.Orchestrator.Api.Hosting;

/// <summary>Log messages of the host start. The source generator writes the method bodies.</summary>
internal static partial class StartupLog
{
    /// <summary>Reports that the server listens on an address that is not loopback.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The error from the listen address check.</param>
    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "The server listens on an address that is not loopback. The server stops now.")]
    public static partial void NonLoopbackAddress(ILogger logger, Exception exception);
}
