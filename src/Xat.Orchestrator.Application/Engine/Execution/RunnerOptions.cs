using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Application.Engine.Execution;

/// <summary>The settings of the pipeline runner.</summary>
public sealed class RunnerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Runs";

    /// <summary>Gets or sets the highest time that one run can use. A run that passes it ends with a timeout error.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "01:00:00", ErrorMessage = "must be more than 0 and at most 1 hour.")]
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how many times the engine runs an idempotent step again after an upstream failure. 0 turns the retry off.
    /// The HTTP retries of the upstream clients come first, so this retry covers only the failures that they do not retry.
    /// </summary>
    [Range(0, 3, ErrorMessage = "must be from 0 to 3.")]
    public int IdempotentRetryCount { get; set; } = 1;
}
