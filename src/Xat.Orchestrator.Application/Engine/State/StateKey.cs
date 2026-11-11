namespace Xat.Orchestrator.Application.Engine.State;

/// <summary>A typed key for a value in the state of a run. A typed key avoids magic strings.</summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="Name">The name of the key. The name must be unique in the state.</param>
public sealed record StateKey<T>(string Name);
