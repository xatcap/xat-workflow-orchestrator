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
    public int MaxConcurrentRuns { get; init; } = 8;

    /// <summary>Gets the number of runs that wait for a free place. A run that finds the queue full gets 503 with <c>Retry-After</c>.</summary>
    public int QueueLimit { get; init; } = 16;

    /// <summary>Gets the highest size of a request body in bytes.</summary>
    public int MaxRequestBodyBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Checks the settings. A wrong value stops the start, so it never shows up in a request.</summary>
    /// <param name="pipelines">The pipelines, to check that the default pipeline exists.</param>
    /// <returns>The messages about wrong settings. An empty list means that the settings are valid.</returns>
    public IReadOnlyList<string> Validate(Xat.Orchestrator.Application.Abstractions.Pipelines.IPipelineCatalog pipelines)
    {
        ArgumentNullException.ThrowIfNull(pipelines);

        var problems = new List<string>();
        if (MaxConcurrentRuns < 1)
        {
            problems.Add($"{SectionName}:MaxConcurrentRuns must be 1 or higher.");
        }

        if (QueueLimit < 0)
        {
            problems.Add($"{SectionName}:QueueLimit must be 0 or higher.");
        }

        if (MaxRequestBodyBytes < 1024)
        {
            problems.Add($"{SectionName}:MaxRequestBodyBytes must be 1024 or higher.");
        }

        if (!pipelines.Find(DefaultPipeline).IsSuccess)
        {
            problems.Add($"{SectionName}:DefaultPipeline names the pipeline '{DefaultPipeline}', which does not exist.");
        }

        return problems;
    }
}
