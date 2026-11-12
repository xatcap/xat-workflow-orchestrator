using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Api;

/// <summary>The settings of the public API.</summary>
internal sealed class ApiOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Api";

    /// <summary>Gets the pipeline that <c>POST /v1/ask</c> runs.</summary>
    public string DefaultPipeline { get; init; } = "rag-qa";

    /// <summary>
    /// Gets the highest number of runs at the same time. The local model runs one request at a time and its queue holds 32,
    /// so the orchestrator must not send more than the engine can queue.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "must be 1 or higher.")]
    public int MaxConcurrentRuns { get; init; } = 8;

    /// <summary>Gets the number of runs that wait for a free place. A run that finds the queue full gets 503 with <c>Retry-After</c>.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "must be 0 or higher.")]
    public int QueueLimit { get; init; } = 16;

    /// <summary>Gets the highest size of a request body in bytes.</summary>
    [Range(1024, int.MaxValue, ErrorMessage = "must be 1024 or higher.")]
    public int MaxRequestBodyBytes { get; init; } = 4 * 1024 * 1024;
}
