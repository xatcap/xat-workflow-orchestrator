using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xat.Orchestrator.Application.Engine.Hooks;
using Xat.Orchestrator.Application.Engine.StepContracts;

namespace Xat.Orchestrator.Application.Engine.Registry;

/// <summary>Registers steps, middleware and hooks. <see cref="DependencyInjection.AddApplication"/> uses them for the built-in steps.</summary>
public static class PipelineServiceCollectionExtensions
{
    /// <summary>Registers a step type. A pipeline file can then use it by name.</summary>
    /// <typeparam name="TStep">The step class.</typeparam>
    /// <typeparam name="TOptions">The class of the options.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name that pipeline files use, for example <c>rag.retrieve</c>.</param>
    /// <param name="traits">The properties of the step type.</param>
    /// <param name="summary">A short description.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPipelineStep<TStep, TOptions>(
        this IServiceCollection services,
        string name,
        StepTraits traits,
        string summary)
        where TStep : class, IPipelineStep<TOptions>
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        services.TryAddSingleton<TStep>();
        services.AddSingleton<IStepRegistration>(
            provider => new StepRegistration<TStep, TOptions>(name, traits, summary, provider.GetRequiredService<TStep>()));
        return services;
    }

    /// <summary>Registers a middleware that wraps every step. The middleware run in the order of registration.</summary>
    /// <typeparam name="TMiddleware">The middleware class.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddStepMiddleware<TMiddleware>(this IServiceCollection services)
        where TMiddleware : class, IStepMiddleware
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IStepMiddleware, TMiddleware>();
        return services;
    }

    /// <summary>Registers a hook that watches every run.</summary>
    /// <typeparam name="TObserver">The hook class.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPipelineObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, IPipelineObserver
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPipelineObserver, TObserver>();
        return services;
    }
}
