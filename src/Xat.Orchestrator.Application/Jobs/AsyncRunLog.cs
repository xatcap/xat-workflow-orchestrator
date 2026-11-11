using Microsoft.Extensions.Logging;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>Log messages of the asynchronous runs. The source generator writes the method bodies.</summary>
internal static partial class AsyncRunLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The asynchronous run {RunId} failed because of an unexpected error.")]
    public static partial void UnexpectedFailure(ILogger logger, Exception exception, string runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The webhook of the run {RunId} was not delivered. {Reason}")]
    public static partial void WebhookFailed(ILogger logger, string runId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "The delivery of the webhook of the run {RunId} failed because of an unexpected error.")]
    public static partial void WebhookUnexpectedFailure(ILogger logger, Exception exception, string runId);
}
