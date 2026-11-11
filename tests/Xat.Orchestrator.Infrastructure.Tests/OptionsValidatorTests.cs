using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Infrastructure.Definitions;
using Xat.Orchestrator.Infrastructure.Discovery;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Tests;

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

    /// <summary>The default model must be one of the configured models.</summary>
    [Fact]
    public void Llm_DefaultModelMustExist()
    {
        var options = new LlmOptions { DefaultModel = "missing" };
        options.Models["other"] = new ModelOptions { Model = "local/x" };

        var result = new LlmOptionsValidator().Validate(null, options);

        Assert.Contains(result.Failures!, failure => failure.Contains("DefaultModel", StringComparison.Ordinal));
    }

    /// <summary>Only the estimate tokenizer exists. Another name is refused, so it cannot silently count wrong.</summary>
    [Fact]
    public void Llm_UnknownTokenizerIsRefused()
    {
        var options = new LlmOptions();
        options.Models["default"] = new ModelOptions { Model = "local/x", Tokenizer = "tiktoken" };

        var result = new LlmOptionsValidator().Validate(null, options);

        Assert.Contains(result.Failures!, failure => failure.Contains("Tokenizer 'tiktoken'", StringComparison.Ordinal));
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
}
