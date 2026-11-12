using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Infrastructure.Options.Llm;
using Xat.Orchestrator.Infrastructure.Options.Retrieval;
using Xat.Orchestrator.Infrastructure.Options.Upstream;

namespace Xat.Orchestrator.Infrastructure.Tests.Options;

/// <summary>Tests the check of the settings at start.</summary>
public class OptionsValidatorTests
{
    /// <summary>An upstream with no address, and an upstream with an address that is not loopback, are not valid.</summary>
    [Fact]
    public void Upstreams_NeedALoopbackAddress()
    {
        var options = new UpstreamOptions
        {
            XatRag = new UpstreamEndpointOptions(),
            XatLlmEngine = new UpstreamEndpointOptions { BaseUrl = "http://example.com" },
        };

        var result = new UpstreamOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("XatRag needs BaseUrl or ReadyFile", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("loopback", StringComparison.Ordinal));
    }

    /// <summary>The adapters send absolute paths, so a base address with a path is refused at start and not dropped in silence.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:8421/api")]
    [InlineData("http://127.0.0.1:8421/?x=1")]
    public void Upstreams_RefuseABaseUrlWithAPath(string url)
    {
        var options = new UpstreamOptions
        {
            XatRag = new UpstreamEndpointOptions { BaseUrl = url },
            XatLlmEngine = new UpstreamEndpointOptions { BaseUrl = "http://127.0.0.1:8420" },
        };

        var result = new UpstreamOptionsValidator().Validate(null, options);

        Assert.Contains(result.Failures!, failure => failure.Contains("XatRag:BaseUrl", StringComparison.Ordinal));
    }

    /// <summary>The range of each number lives in an attribute. The check enforces both ends, and the message names the full key.</summary>
    [Theory]
    [InlineData(0, 2, "Upstreams:XatRag:RequestTimeoutSeconds must be from 1 to 3600.")]
    [InlineData(3601, 2, "Upstreams:XatRag:RequestTimeoutSeconds must be from 1 to 3600.")]
    [InlineData(30, -1, "Upstreams:XatRag:RetryCount must be from 0 to 5.")]
    [InlineData(30, 6, "Upstreams:XatRag:RetryCount must be from 0 to 5.")]
    [InlineData(1, 0, null)]
    [InlineData(3600, 5, null)]
    public void Upstreams_CheckTheRanges(int timeout, int retries, string? expected)
    {
        var options = new UpstreamOptions
        {
            XatRag = new UpstreamEndpointOptions { BaseUrl = "http://127.0.0.1:8421", RequestTimeoutSeconds = timeout, RetryCount = retries },
        };

        var result = new UpstreamOptionsValidator().Validate(null, options);

        if (expected is null)
        {
            Assert.True(result.Succeeded);
        }
        else
        {
            Assert.Contains(expected, result.Failures!);
        }
    }

    /// <summary>An upstream time limit that is not shorter than the run time limit gives a warning, because the run timeout then always comes first.</summary>
    [Fact]
    public void Upstreams_WarnWhenTheCallTimeoutIsNotShorterThanTheRunTimeout()
    {
        var logger = new ListLogger<UpstreamOptionsValidator>();
        var runs = Microsoft.Extensions.Options.Options.Create(new Application.Engine.Execution.RunnerOptions { RunTimeout = TimeSpan.FromSeconds(60) });
        var options = new UpstreamOptions
        {
            XatRag = new UpstreamEndpointOptions { BaseUrl = "http://127.0.0.1:8421", RequestTimeoutSeconds = 30 },
            XatLlmEngine = new UpstreamEndpointOptions { BaseUrl = "http://127.0.0.1:8420", RequestTimeoutSeconds = 60 },
        };

        var result = new UpstreamOptionsValidator(runs, logger).Validate(null, options);

        Assert.True(result.Succeeded);
        var warning = Assert.Single(logger.Lines);
        Assert.Contains("Upstreams:XatLlmEngine:RequestTimeoutSeconds", warning, StringComparison.Ordinal);
    }

    /// <summary>The default model must be one of the configured models.</summary>
    [Fact]
    public void Llm_DefaultModelMustExist()
    {
        var options = new LlmOptions { DefaultModel = "missing" };
        options.Models["other"] = new ModelOptions { Model = "local/x" };

        var result = new LlmOptionsValidator().Validate(null, options);

        Assert.Contains(result.Failures!, failure => failure.Contains("DefaultModel", StringComparison.Ordinal));
    }

    /// <summary>A key that the code does not know is not accepted. The start fails and the error names the key.</summary>
    [Fact]
    public void Llm_UnknownKeyIsRefused()
    {
        const string key = "UnknownKey";
        const string value = "1";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:DefaultModel"] = "default",
                ["Llm:Models:default:Model"] = "local/x",
                [$"Llm:Models:default:{key}"] = value,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptions<LlmOptions>>().Value);

        Assert.Contains($"'{key}'", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    /// <summary>A retrieval alias must point to a valid xat-rag context name and a valid number of chunks.</summary>
    [Fact]
    public void Retrieval_ChecksNamesAndChunks()
    {
        var options = new RetrievalOptions();
        options.Profiles["Bad Alias"] = new RetrievalProfileOptions { Context = "ask-docs" };
        options.Profiles["ok"] = new RetrievalProfileOptions { Context = "BAD", Chunks = 500 };

        var result = new RetrievalOptionsValidator().Validate(null, options);

        Assert.Equal(3, result.Failures!.Count());
    }

    private sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
