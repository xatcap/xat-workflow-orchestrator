using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Definitions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Definitions;

/// <summary>The error that the service throws when a pipeline file or a template file is not valid. The service does not start.</summary>
public sealed class ConfigurationFilesException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ConfigurationFilesException"/> class.</summary>
    /// <param name="problems">The problems of all files.</param>
    public ConfigurationFilesException(IReadOnlyList<string> problems)
        : base("The configuration files are not valid. " + string.Join(" ", problems ?? []))
    {
        Problems = problems ?? [];
    }

    /// <summary>Initializes a new instance of the <see cref="ConfigurationFilesException"/> class.</summary>
    public ConfigurationFilesException()
        : this([])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ConfigurationFilesException"/> class.</summary>
    /// <param name="message">The message.</param>
    public ConfigurationFilesException(string message)
        : this([message])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ConfigurationFilesException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ConfigurationFilesException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problems = [message];
    }

    /// <summary>Gets the problems of all files.</summary>
    public IReadOnlyList<string> Problems { get; }
}
