namespace Xat.Orchestrator.Api.Errors;

/// <summary>Log messages for unexpected exceptions. The source generator writes the method bodies.</summary>
internal static partial class ExceptionLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "A request failed because of an unhandled exception.")]
    public static partial void Unhandled(ILogger logger, Exception exception);
}
