using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Jobs;

namespace Xat.Orchestrator.Application.Tests.Engine;

/// <summary>Tests the ranges of the runner and jobs settings. The Application layer owns and checks these settings.</summary>
public class OptionsRangeTests
{
    private static ServiceProvider Build(Action<RunnerOptions>? runs = null, Action<JobsOptions>? jobs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.Configure<RunnerOptions>(options => runs?.Invoke(options));
        services.Configure<JobsOptions>(options => jobs?.Invoke(options));
        return services.BuildServiceProvider();
    }

    /// <summary>Each value outside its range gives a message with the full key.</summary>
    [Theory]
    [InlineData(0, 1, 2, "Runs:RunTimeout must be more than 0 and at most 1 hour.")]
    [InlineData(3601, 1, 2, "Runs:RunTimeout must be more than 0 and at most 1 hour.")]
    [InlineData(60, 4, 2, "Runs:IdempotentRetryCount must be from 0 to 3.")]
    [InlineData(60, 1, 0, "Jobs:Workers must be from 1 to 32.")]
    [InlineData(60, 1, 33, "Jobs:Workers must be from 1 to 32.")]
    public void ValueOutOfRange_StopsTheStart(int timeoutSeconds, int retries, int workers, string expected)
    {
        using var services = Build(
            runs => (runs.RunTimeout, runs.IdempotentRetryCount) = (TimeSpan.FromSeconds(timeoutSeconds), retries),
            jobs => jobs.Workers = workers);

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(expected, exception.Failures);
    }

    /// <summary>The ends of each range are valid.</summary>
    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(3600, 3, 32)]
    public void ValueAtTheEndOfTheRange_IsValid(int timeoutSeconds, int retries, int workers)
    {
        using var services = Build(
            runs => (runs.RunTimeout, runs.IdempotentRetryCount) = (TimeSpan.FromSeconds(timeoutSeconds), retries),
            jobs => jobs.Workers = workers);

        services.GetRequiredService<IStartupValidator>().Validate();
    }
}
