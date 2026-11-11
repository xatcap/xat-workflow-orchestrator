using Microsoft.Extensions.Logging;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>Log messages of the pipeline runner. The source generator writes the method bodies.</summary>
internal static partial class RunnerLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The run {RunId} failed because of an unexpected error.")]
    public static partial void UnexpectedFailure(ILogger logger, Exception exception, string runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The hook {Observer} failed or did not finish in time. The run goes on.")]
    public static partial void ObserverFailed(ILogger logger, Exception exception, string observer);
}
