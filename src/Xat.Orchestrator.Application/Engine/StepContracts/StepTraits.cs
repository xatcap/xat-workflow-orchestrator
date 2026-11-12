namespace Xat.Orchestrator.Application.Engine.StepContracts;

/// <summary>The properties of a step type. The definition validator and the runner use them.</summary>
[Flags]
public enum StepTraits
{
    /// <summary>The step has no special property.</summary>
    None = 0,

    /// <summary>
    /// The step calls the LLM, and only through the LLM port. The port limits the calls for each model. A step with this trait
    /// cannot also be <see cref="Idempotent"/>, because a second call uses model time again and can give a different answer.
    /// </summary>
    CallsLlm = 1,

    /// <summary>The step can send the answer as a stream.</summary>
    Streams = 2,

    /// <summary>The step can set the answer of the run.</summary>
    ProducesAnswer = 4,

    /// <summary>
    /// The step can run again without harm. After an upstream failure, the engine runs it again up to <c>Runs:IdempotentRetryCount</c> times,
    /// but only when the step sent no event to the caller yet.
    /// </summary>
    Idempotent = 8,
}
