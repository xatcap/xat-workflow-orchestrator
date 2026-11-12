namespace Xat.Orchestrator.Application.Engine.Registry;

/// <summary>Finds the registered step types.</summary>
public interface IStepRegistry
{
    /// <summary>Gets all registered step types.</summary>
    IReadOnlyCollection<IStepRegistration> All { get; }

    /// <summary>Finds a step type by its name.</summary>
    /// <param name="name">The registered name.</param>
    /// <returns>The registration, or <see langword="null"/> when no step type has this name.</returns>
    IStepRegistration? Find(string name);
}
