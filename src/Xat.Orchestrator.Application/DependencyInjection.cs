using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Definitions;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Hooks;
using Xat.Orchestrator.Application.Engine.Registry;
using Xat.Orchestrator.Application.Engine.Runs;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Application.Jobs;
using Xat.Orchestrator.Application.Steps.Citations;
using Xat.Orchestrator.Application.Steps.Flow;
using Xat.Orchestrator.Application.Steps.Llm;
using Xat.Orchestrator.Application.Steps.Prompt;
using Xat.Orchestrator.Application.Steps.Retrieval;

namespace Xat.Orchestrator.Application;

/// <summary>Registers the services of the Application layer.</summary>
public static class DependencyInjection
{
    /// <summary>Adds the pipeline engine, the built-in steps and their settings to the service collection.</summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">The configuration that holds the <c>Runs</c> and <c>Jobs</c> sections. A test can leave it out and set the options in code.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var runs = services.AddOptions<RunnerOptions>().ValidateOnStart();
        var jobs = services.AddOptions<JobsOptions>().ValidateOnStart();
        if (configuration is not null)
        {
            runs.Bind(configuration.GetSection(RunnerOptions.SectionName));
            jobs.Bind(configuration.GetSection(JobsOptions.SectionName));
        }

        services.AddSingleton<IValidateOptions<RunnerOptions>, RunnerOptionsValidator>();
        services.AddSingleton<IValidateOptions<JobsOptions>, JobsOptionsValidator>();
        services.AddSingleton<AsyncRunService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStepRegistry, StepRegistry>();
        services.AddSingleton<PipelineRunner>();
        services.AddSingleton<RunStarter>();
        services.AddSingleton<PipelineDefinitionValidator>();
        // The first middleware is the outer one. So one span covers all the tries of a step.
        services.AddStepMiddleware<StepActivityMiddleware>();
        services.AddStepMiddleware<IdempotentRetryMiddleware>();
        services.AddPipelineObserver<RunLogObserver>();

        services
            .AddPipelineStep<PromptSystemStep, PromptSystemOptions>(
                "prompt.system", StepTraits.None, "Sets the system prompt from a text or a template.")
            .AddPipelineStep<PromptUserStep, PromptUserOptions>(
                "prompt.user", StepTraits.None, "Sets the prompt of the caller.")
            .AddPipelineStep<PromptContextStep, PromptContextOptions>(
                "prompt.context", StepTraits.None, "Adds context text that the caller sent, in an escaped tag.")
            .AddPipelineStep<PromptAppendStep, PromptAppendOptions>(
                "prompt.append", StepTraits.None, "Adds an instruction that the pipeline author wrote.")
            .AddPipelineStep<RetrieveStep, RetrieveOptions>(
                "rag.retrieve", StepTraits.Idempotent, "Gets the chunks of one xat-rag context for a question.")
            .AddPipelineStep<RetrievalMergeStep, RetrievalMergeOptions>(
                "retrieval.merge", StepTraits.Idempotent, "Merges the chunks of several retrieval steps.")
            .AddPipelineStep<FlowCompleteIfStep, FlowCompleteIfOptions>(
                "flow.complete-if", StepTraits.ProducesAnswer, "Ends the run with a fixed answer when a condition is true.")
            .AddPipelineStep<PromptAssembleStep, PromptAssembleOptions>(
                "prompt.assemble", StepTraits.None, "Builds the chat messages from the prompt parts and numbers the sources.")
            .AddPipelineStep<LlmGenerateStep, LlmGenerateOptions>(
                "llm.generate",
                StepTraits.CallsLlm | StepTraits.Streams | StepTraits.ProducesAnswer,
                "Sends the messages to the LLM and stores the answer.")
            .AddPipelineStep<VerifyCitationsStep, VerifyCitationsOptions>(
                "answer.verify-citations", StepTraits.None, "Maps the citations in the answer to the sources.");

        return services;
    }
}
