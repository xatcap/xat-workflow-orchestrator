using System.Net;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>Tests the rule that refuses every request with an Origin header. This stops a web page in a browser from calling the orchestrator.</summary>
public class BrowserOriginGuardTests
{
    /// <summary>A request with an Origin header gets 403 in the normal error form, on every kind of route.</summary>
    [Theory]
    [InlineData("GET", "/v1/pipelines")]
    [InlineData("GET", "/health/live")]
    [InlineData("POST", "/v1/pipelines/chat/runs")]
    [InlineData("POST", "/v1/rags/docs/rebuild")]
    [InlineData("PUT", "/v1/rags/docs/documents")]
    [InlineData("DELETE", "/v1/runs/run_1")]
    [InlineData("OPTIONS", "/v1/pipelines")]
    public async Task RequestWithOrigin_Gets403(string method, string path)
    {
        var llm = new FakeLlmClient();
        await using var factory = ApiHost.Create(llm: llm);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Origin", "http://evil.example");
        if (method is "POST" or "PUT")
        {
            request.Content = ApiHost.Json("""{"input":{"prompt":"Q"}}""");
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("request.origin_not_allowed", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(llm.Requests);
    }

    /// <summary>An Origin header with a loopback address is refused too. xat-rag has the same rule.</summary>
    [Fact]
    public async Task LoopbackOrigin_IsRefusedToo()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/pipelines");
        request.Headers.Add("Origin", "http://localhost:3000");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A program sends no Origin header, so it is not affected.</summary>
    [Fact]
    public async Task RequestWithoutOrigin_Works()
    {
        await using var factory = ApiHost.Create();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/pipelines", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
