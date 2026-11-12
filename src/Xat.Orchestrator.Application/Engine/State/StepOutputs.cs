using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Engine.State;

/// <summary>The keys of the step results that the built-in steps share. An expression reads them as <c>steps.&lt;id&gt;.&lt;name&gt;</c>.</summary>
public static class StepOutputs
{
    /// <summary>Gets the key of the chunks of a retrieval or merge step.</summary>
    public static StateKey<IReadOnlyList<Chunk>> Chunks { get; } = new("chunks");

    /// <summary>Gets the key of the number of chunks of a retrieval or merge step.</summary>
    public static StateKey<int> Count { get; } = new("count");

    /// <summary>Gets the key of the number of chunks that xat-rag left out.</summary>
    public static StateKey<int> Omitted { get; } = new("omitted");
}
