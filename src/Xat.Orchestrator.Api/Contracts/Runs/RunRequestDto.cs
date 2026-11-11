using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>The body of a request to run a pipeline.</summary>
/// <param name="Input">The input of the pipeline. The input schema of the pipeline describes it.</param>
/// <param name="Stream">Set <see langword="true"/> to receive the answer as a stream of server-sent events.</param>
/// <param name="Debug">Set <see langword="true"/> to receive the time of each step.</param>
/// <param name="Webhook">The address that receives the result when an asynchronous run ends. Use it only with <c>?mode=async</c>.</param>
internal sealed record RunRequestDto(JsonElement? Input, bool Stream = false, bool Debug = false, string? Webhook = null);
