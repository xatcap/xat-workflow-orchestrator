using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Tests.Support;

namespace Xat.Orchestrator.Infrastructure.Tests.Definitions;

/// <summary>
/// Tests the errors for an unknown model, retrieval profile or template. The name can come from the input of the caller,
/// so the error is a validation error (400) and it never repeats the name.
/// </summary>
public sealed class CatalogLookupTests : IDisposable
{
    private const string CallerValue = "caller-secret-value";

    private readonly Harness _harness = new();

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    /// <summary>An unknown model alias.</summary>
    [Fact]
    public void UnknownModel_IsAValidationError_ThatDoesNotRepeatTheAlias()
    {
        var error = _harness.Get<IModelCatalog>().Resolve(CallerValue).Error!;

        AssertSafe(error, "llm.model_unknown");
    }

    /// <summary>An unknown retrieval profile.</summary>
    [Fact]
    public void UnknownProfile_IsAValidationError_ThatDoesNotRepeatTheAlias()
    {
        var error = _harness.Get<IRetrievalProfiles>().Resolve(CallerValue).Error!;

        AssertSafe(error, "retrieve.profile_unknown");
    }

    /// <summary>An unknown template.</summary>
    [Fact]
    public void UnknownTemplate_IsAValidationError_ThatDoesNotRepeatTheName()
    {
        var error = _harness.Get<IPromptTemplateStore>().Find(CallerValue).Error!;

        AssertSafe(error, "template.unknown");
    }

    private static void AssertSafe(PipelineError error, string code)
    {
        Assert.Equal(code, error.Code);
        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.DoesNotContain(CallerValue, error.Message, StringComparison.Ordinal);
    }
}
