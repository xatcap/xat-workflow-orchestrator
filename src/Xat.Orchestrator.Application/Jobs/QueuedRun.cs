using System.Text.Json;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>A run that waits for a worker. The webhook address is not in the stored run, because it can hold a secret.</summary>
/// <param name="RunId">The ID of the run.</param>
/// <param name="Pipeline">The pipeline.</param>
/// <param name="Input">The input of the caller.</param>
/// <param name="Webhook">The address of the webhook, or <see langword="null"/>.</param>
public sealed record QueuedRun(string RunId, PipelineDefinition Pipeline, JsonElement Input, Uri? Webhook);
