using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Engine.State;

/// <summary>The keys that the built-in steps use to share data through the run state.</summary>
public static class StateKeys
{
    /// <summary>Gets the key of the chat messages that the assemble step builds.</summary>
    public static StateKey<IReadOnlyList<ChatMessage>> Messages { get; } = new("messages");

    /// <summary>Gets the key of the answer of the run.</summary>
    public static StateKey<Answer> Answer { get; } = new("answer");

    /// <summary>Gets the key of the numbered sources in the prompt. The model cites these numbers.</summary>
    public static StateKey<IReadOnlyDictionary<int, Chunk>> Sources { get; } = new("sources");
}
