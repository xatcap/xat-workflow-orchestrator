using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options.Runtime;

/// <summary>The place of the prompt template files.</summary>
internal sealed class PromptsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Prompts";

    /// <summary>Gets the folder with the <c>name@version.liquid</c> template files. A relative path starts at the folder of the application.</summary>
    public string Folder { get; init; } = "prompts";
}
