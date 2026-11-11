using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The debug data of a run.</summary>
/// <param name="Steps">The time of each step.</param>
internal sealed record DebugDto(IReadOnlyList<StepTimingDto> Steps);
