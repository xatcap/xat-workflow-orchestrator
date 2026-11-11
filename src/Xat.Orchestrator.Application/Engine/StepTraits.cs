using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>The properties of a step type. The definition validator and the runner use them.</summary>
[Flags]
public enum StepTraits
{
    /// <summary>The step has no special property.</summary>
    None = 0,

    /// <summary>The step calls the LLM.</summary>
    CallsLlm = 1,

    /// <summary>The step can send the answer as a stream.</summary>
    Streams = 2,

    /// <summary>The step can set the answer of the run.</summary>
    ProducesAnswer = 4,

    /// <summary>The engine can run the step again without a harm.</summary>
    Idempotent = 8,
}
