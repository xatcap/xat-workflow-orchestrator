using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>Smoke tests of the health routes. They prove that the host builds and starts.</summary>
/// <param name="factory">The factory that starts the host in memory.</param>
public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Each health route answers with status 200.</summary>
    /// <param name="path">The path of the route.</param>
    /// <returns>A task that completes when the test ends.</returns>
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthRoute_Returns200(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
