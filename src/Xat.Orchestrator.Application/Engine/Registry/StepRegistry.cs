using Xat.Orchestrator.Application.Engine.StepContracts;

namespace Xat.Orchestrator.Application.Engine.Registry;

/// <summary>The default registry. It holds all registrations that the service collection has.</summary>
public sealed class StepRegistry : IStepRegistry
{
    private readonly Dictionary<string, IStepRegistration> _byName;

    /// <summary>Initializes a new instance of the <see cref="StepRegistry"/> class.</summary>
    /// <param name="registrations">The registrations from the service collection.</param>
    /// <exception cref="InvalidOperationException">
    /// Two registrations have the same name, or a registration has both <see cref="StepTraits.CallsLlm"/> and <see cref="StepTraits.Idempotent"/>.
    /// </exception>
    public StepRegistry(IEnumerable<IStepRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _byName = new Dictionary<string, IStepRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (registration.Traits.HasFlag(StepTraits.CallsLlm) && registration.Traits.HasFlag(StepTraits.Idempotent))
            {
                throw new InvalidOperationException(
                    $"The step type '{registration.Name}' calls the LLM, so it cannot be idempotent. The engine must not repeat an LLM call.");
            }

            if (!_byName.TryAdd(registration.Name, registration))
            {
                throw new InvalidOperationException($"Two step types have the name '{registration.Name}'.");
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IStepRegistration> All => _byName.Values;

    /// <inheritdoc/>
    public IStepRegistration? Find(string name) => _byName.GetValueOrDefault(name);
}
