using System.Net;
using System.Text;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Infrastructure.Discovery;
using Xat.Orchestrator.Infrastructure.Options.Upstream;
using Xat.Orchestrator.Infrastructure.Tests.Support;
using Xat.Orchestrator.Infrastructure.XatLlmEngine;

namespace Xat.Orchestrator.Infrastructure.Tests.Discovery;

/// <summary>Tests the errors for an upstream that cannot be found or that stops in the middle of an answer.</summary>
public sealed class UpstreamFailureTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("xat-failure-").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static LlmRequest Request() =>
        new(new ModelProfile("default", "local/test-model", 1, []), [new ChatMessage(ChatRole.User, "Hello")], null, null);

    /// <summary>A ready file with a wrong shape is the same as a file that cannot be read. It does not cause an exception.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"url":5}""")]
    [InlineData("")]
    [InlineData("""{"url":"http://10.0.0.1:8421"}""")]
    [InlineData("""{"url":"http://127.0.0.1:8421/api"}""")]
    public async Task MalformedReadyFile_GivesNotLocated(string content)
    {
        var file = Path.Combine(_folder, "ready.json");
        await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);
        var locator = new ReadyFileLocator(Microsoft.Extensions.Options.Options.Create(new UpstreamOptions
        {
            XatRag = new UpstreamEndpointOptions { ReadyFile = file },
        }));

        var result = await locator.LocateAsync(Upstream.XatRag, TestContext.Current.CancellationToken);

        Assert.Equal("xat-rag.not_located", result.Error!.Code);
        Assert.Equal(ErrorKind.UpstreamUnavailable, result.Error.Kind);
        Assert.DoesNotContain("10.0.0.1", result.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>The caller sees the error of the locator, not a general connection error.</summary>
    [Fact]
    public async Task UnknownAddress_ReachesTheCallerAsNotLocated()
    {
        using var harness = new Harness(new Dictionary<string, string?>
        {
            ["Upstreams:XatRag:BaseUrl"] = string.Empty,
            ["Upstreams:XatRag:ReadyFile"] = Path.Combine(_folder, "none.json"),
        });

        var result = await harness.Get<IRetrievalClient>().RetrieveAsync(
            new RetrievalQuery("ask-all", "question", 3), TestContext.Current.CancellationToken);

        Assert.Equal("xat-rag.not_located", result.Error!.Code);
        Assert.Equal(ErrorKind.UpstreamUnavailable, result.Error.Kind);
    }

    /// <summary>A connection that breaks in the middle of a stream gives stream_incomplete, not an internal error.</summary>
    [Fact]
    public async Task BrokenStream_GivesStreamIncomplete()
    {
        using var handler = new BreakingHandler("data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\n");
        var client = new XatEngineLlmClient(new SingleClientFactory(handler, new Uri("http://127.0.0.1:8420/")));

        var items = new List<Result<CompletionDelta>>();
        await foreach (var item in client.StreamAsync(Request(), TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        Assert.Equal("Hi", items[0].Value!.Text);
        Assert.Equal("xat-llm-engine.stream_incomplete", items[^1].Error!.Code);
        Assert.Equal(ErrorKind.UpstreamFailed, items[^1].Error!.Kind);
    }

    /// <summary>A connection that breaks while a complete answer is read gives unreachable, not an internal error.</summary>
    [Fact]
    public async Task BrokenBody_GivesUnreachable()
    {
        using var handler = new BreakingHandler("{\"choices\":");
        var client = new XatEngineLlmClient(new SingleClientFactory(handler, new Uri("http://127.0.0.1:8420/")));

        var result = await client.CompleteAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("xat-llm-engine.unreachable", result.Error!.Code);
    }

    /// <summary>Answers with status 200, then the body breaks after the given text.</summary>
    private sealed class BreakingHandler(string prefix) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BreakingStream(prefix)) });
    }

    /// <summary>Gives the text, then throws the exception that a dropped connection causes.</summary>
    private sealed class BreakingStream(string prefix) : Stream
    {
        private readonly byte[] _data = Encoding.UTF8.GetBytes(prefix);
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length)
            {
                throw new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
            }

            var length = Math.Min(count, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, length);
            _position += length;
            return length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
