using System.Reflection;
using NetArchTest.Rules;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Architecture.Tests;

/// <summary>Enforces the Clean Architecture rule that dependencies point inward only.</summary>
public class DependencyRuleTests
{
    /// <summary>The assembly of the Domain layer.</summary>
    private static readonly Assembly _domain = typeof(PipelineError).Assembly;

    /// <summary>The assembly of the Application layer.</summary>
    private static readonly Assembly _applicationLayer = typeof(Application.DependencyInjection).Assembly;

    /// <summary>The assembly of the Infrastructure layer.</summary>
    private static readonly Assembly _infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;

    /// <summary>
    /// Domain references only the base class library. This allow-list also catches a package that the
    /// deny-list in the next test does not name, such as a JSON library or a database library.
    /// </summary>
    [Fact]
    public void Domain_ReferencesOnlyTheBaseClassLibrary()
    {
        var allowedPrefixes = new[] { "System", "mscorlib", "netstandard" };

        var unexpected = _domain.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => !allowedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(unexpected.Count == 0, string.Join(", ", unexpected));
    }

    /// <summary>Domain does not use a type from an outer layer or from a framework namespace.</summary>
    [Fact]
    public void Domain_DependsOnNoOuterLayerOrFramework()
    {
        var result = Types.InAssembly(_domain).ShouldNot().HaveDependencyOnAny(
            "Xat.Orchestrator.Application",
            "Xat.Orchestrator.Infrastructure",
            "Xat.Orchestrator.Api",
            "Microsoft.Extensions",
            "Microsoft.AspNetCore",
            "System.Net.Http").GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>Application does not depend on an outer layer or on the web framework.</summary>
    [Fact]
    public void Application_DependsOnNoOuterLayerOrWebFramework()
    {
        var result = Types.InAssembly(_applicationLayer).ShouldNot().HaveDependencyOnAny(
            "Xat.Orchestrator.Infrastructure",
            "Xat.Orchestrator.Api",
            "Microsoft.AspNetCore",
            "System.Net.Http").GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>Infrastructure does not depend on the Api layer or on the web framework.</summary>
    [Fact]
    public void Infrastructure_DoesNotDependOnApiOrWebFramework()
    {
        var result = Types.InAssembly(_infrastructure).ShouldNot().HaveDependencyOnAny(
            "Xat.Orchestrator.Api",
            "Microsoft.AspNetCore").GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Application uses no vendor library. The libraries for YAML, templates and resilience belong to the adapters,
    /// so that a library can change without a change to a use case.
    /// </summary>
    [Fact]
    public void Application_UsesNoVendorLibrary()
    {
        var result = Types.InAssembly(_applicationLayer).ShouldNot().HaveDependencyOnAny(
            "YamlDotNet",
            "Fluid",
            "Polly",
            "Microsoft.Extensions.Http").GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// An adapter is reachable only through the port that it implements. The classes of the adapters are not public,
    /// except the registration class and the start error.
    /// </summary>
    [Fact]
    public void InfrastructureAdapters_AreNotPublic()
    {
        var publicTypes = Types.InAssembly(_infrastructure)
            .That().ArePublic()
            .GetTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["ConfigurationFilesException", "DependencyInjection"], publicTypes);
    }

    /// <summary>Makes a text from the types that broke a rule.</summary>
    /// <param name="result">The result of the rule check.</param>
    /// <returns>The names of the failing types, or an empty text.</returns>
    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is null ? string.Empty : string.Join(", ", result.FailingTypeNames);
}
