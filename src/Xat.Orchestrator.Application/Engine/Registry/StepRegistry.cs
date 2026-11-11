using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xat.Orchestrator.Application.Engine.Registry;

/// <summary>The default registry. It holds all registrations that the service collection has.</summary>
public sealed class StepRegistry : IStepRegistry
{
    private readonly Dictionary<string, IStepRegistration> _byName;

    /// <summary>Initializes a new instance of the <see cref="StepRegistry"/> class.</summary>
    /// <param name="registrations">The registrations from the service collection.</param>
    /// <exception cref="InvalidOperationException">Two registrations have the same name.</exception>
    public StepRegistry(IEnumerable<IStepRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        _byName = new Dictionary<string, IStepRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
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
