namespace Xat.Orchestrator.Application.Engine.Execution;

/// <summary>The settings of the pipeline runner.</summary>
public sealed class RunnerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Runs";

    /// <summary>Gets or sets the highest time that one run can use. A run that passes it ends with a timeout error.</summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromSeconds(180);
}
