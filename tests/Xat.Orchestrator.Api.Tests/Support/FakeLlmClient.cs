using System.Runtime.CompilerServices;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Api.Tests.Support;

/// <summary>A fake of xat-llm-engine. A test sets the answer or a handler, and reads the requests.</summary>
internal sealed class FakeLlmClient : ILlmClient
{
    public List<LlmRequest> Requests { get; } = [];

    public string Answer { get; set; } = "The orders service uses port 8080 [1].";

    public Func<LlmRequest, CancellationToken, Task<Result<Completion>>>? OnComplete { get; set; }

    /// <summary>Gets or sets an error that the stream sends after its second word. The stream then ends.</summary>
    public PipelineError? StreamFailure { get; set; }

    public async Task<Result<Completion>> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return OnComplete is null
            ? Result<Completion>.Ok(new Completion(Answer, "stop", new TokenUsage(100, 20)))
            : await OnComplete(request, ct);
    }

    public async IAsyncEnumerable<Result<CompletionDelta>> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        var words = Answer.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            await Task.Yield();
            yield return Result<CompletionDelta>.Ok(new CompletionDelta(i == 0 ? words[i] : " " + words[i], null, null));
            if (StreamFailure is not null && i == 1)
            {
                yield return Result<CompletionDelta>.Fail(StreamFailure);
                yield break;
            }
        }

        yield return Result<CompletionDelta>.Ok(new CompletionDelta(string.Empty, "stop", new TokenUsage(100, 20)));
    }
}
