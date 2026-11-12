using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.Execution;

/// <summary>Shows how the engine continues after a step.</summary>
public abstract record StepOutcome
{
    private StepOutcome()
    {
    }

    /// <summary>Gets the outcome that makes the engine run the next step.</summary>
    public static StepOutcome Continue { get; } = new ContinueOutcome();

    /// <summary>Makes an outcome that ends the run with an answer. The engine skips the other steps.</summary>
    /// <param name="answer">The answer.</param>
    /// <param name="status">Shows how the run ended.</param>
    /// <returns>The outcome.</returns>
    public static StepOutcome Complete(Answer answer, RunStatus status) => new CompleteOutcome(answer, status);

    /// <summary>Makes an outcome that ends the run with a failure.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The outcome.</returns>
    public static StepOutcome Fail(PipelineError error) => new FailedOutcome(error);

    /// <summary>The engine runs the next step.</summary>
    public sealed record ContinueOutcome : StepOutcome;

    /// <summary>The run ends with an answer.</summary>
    /// <param name="Answer">The answer.</param>
    /// <param name="Status">Shows how the run ended.</param>
    public sealed record CompleteOutcome(Answer Answer, RunStatus Status) : StepOutcome;

    /// <summary>The run ends with a failure.</summary>
    /// <param name="Error">The error.</param>
    public sealed record FailedOutcome(PipelineError Error) : StepOutcome;
}
