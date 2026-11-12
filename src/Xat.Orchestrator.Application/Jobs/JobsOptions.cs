using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>The settings of the asynchronous runs.</summary>
public sealed class JobsOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Jobs";

    /// <summary>Gets or sets the number of runs that execute at the same time. The local model runs one request at a time.</summary>
    [Range(1, 32, ErrorMessage = "must be from 1 to 32.")]
    public int Workers { get; set; } = 2;

    /// <summary>Gets or sets the number of webhooks that the service delivers at the same time. Delivery has its own workers, so a slow receiver does not stop the runs.</summary>
    [Range(1, 32, ErrorMessage = "must be from 1 to 32.")]
    public int WebhookWorkers { get; set; } = 4;

    /// <summary>Gets or sets the number of runs that wait for a worker. A run above this number gets 503 with <c>Retry-After</c>.</summary>
    [Range(1, 10_000, ErrorMessage = "must be from 1 to 10000.")]
    public int QueueLimit { get; set; } = 64;

    /// <summary>Gets or sets the highest number of finished runs that the store keeps.</summary>
    [Range(1, 100_000, ErrorMessage = "must be from 1 to 100000.")]
    public int RetainFinished { get; set; } = 500;

    /// <summary>Gets or sets the number of minutes for which the store keeps a finished run.</summary>
    [Range(1, 10_080, ErrorMessage = "must be from 1 to 10080.")]
    public int RetentionMinutes { get; set; } = 60;
}
