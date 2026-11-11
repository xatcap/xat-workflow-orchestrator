using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>A step type that pipelines can use.</summary>
/// <param name="Name">The name.</param>
/// <param name="Summary">The summary.</param>
/// <param name="Traits">The properties of the step type.</param>
internal sealed record StepTypeDto(string Name, string Summary, IReadOnlyList<string> Traits);
