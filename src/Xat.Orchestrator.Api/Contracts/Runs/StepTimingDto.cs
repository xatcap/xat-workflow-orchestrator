using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>The time of one step.</summary>
/// <param name="Id">The ID of the step.</param>
/// <param name="Uses">The step type.</param>
/// <param name="DurationMs">The time in milliseconds.</param>
internal sealed record StepTimingDto(string Id, string Uses, double DurationMs);
