using System.Text.Json;

namespace Xat.Orchestrator.Domain.Pipelines;

/// <summary>One step of a pipeline, as the pipeline file defines it.</summary>
/// <param name="Id">The unique name of the step inside the pipeline.</param>
/// <param name="Uses">The registered name of the step type, for example <c>rag.retrieve</c>.</param>
/// <param name="With">The options of the step as a JSON object. A text value can hold an expression such as <c>${input.prompt}</c>.</param>
public sealed record StepDefinition(string Id, string Uses, JsonElement With);
