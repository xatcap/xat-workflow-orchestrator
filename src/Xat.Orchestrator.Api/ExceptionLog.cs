using Microsoft.AspNetCore.Diagnostics;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Api;

/// <summary>Log messages for unexpected exceptions. The source generator writes the method bodies.</summary>
internal static partial class ExceptionLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "A request failed because of an unhandled exception.")]
    public static partial void Unhandled(ILogger logger, Exception exception);
}
