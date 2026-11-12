using Microsoft.AspNetCore.Mvc.Testing;
using Xat.Orchestrator.Api.Hosting;

namespace Xat.Orchestrator.Api.Tests.Hosting;

/// <summary>Tests that the host accepts only loopback listen addresses.</summary>
public class ListenAddressPolicyTests
{
    /// <summary>Loopback addresses and Unix sockets are accepted.</summary>
    /// <param name="url">The listen address.</param>
    [Theory]
    [InlineData("http://127.0.0.1:8430")]
    [InlineData("http://localhost:8430")]
    [InlineData("http://[::1]:8430")]
    [InlineData("http://unix:/tmp/xat-orchestrator.sock")]
    public void EnsureLoopbackOnly_AcceptsLoopback(string url)
    {
        var exception = Record.Exception(() => ListenAddressPolicy.EnsureLoopbackOnly([url]));

        Assert.Null(exception);
    }

    /// <summary>Network and wildcard addresses are refused.</summary>
    /// <param name="url">The listen address.</param>
    [Theory]
    [InlineData("http://0.0.0.0:8430")]
    [InlineData("http://[::]:8430")]
    [InlineData("http://+:8430")]
    [InlineData("http://*:8430")]
    [InlineData("http://192.168.1.10:8430")]
    [InlineData("http://example.com:8430")]
    [InlineData("not a url")]
    public void EnsureLoopbackOnly_RefusesOtherAddresses(string url)
    {
        Assert.Throws<InvalidOperationException>(() => ListenAddressPolicy.EnsureLoopbackOnly([url]));
    }

    /// <summary>The host fails to start when the <c>urls</c> setting holds a network address.</summary>
    [Fact]
    public void Host_WithNetworkAddress_FailsToStart()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("urls", "http://0.0.0.0:8431"));

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    /// <summary>The splitter handles an unset value and a list with spaces.</summary>
    [Fact]
    public void SplitUrls_HandlesEmptyAndSeparatedValues()
    {
        Assert.Empty(ListenAddressPolicy.SplitUrls(null));
        Assert.Empty(ListenAddressPolicy.SplitUrls("  "));
        Assert.Equal(["http://127.0.0.1:1", "http://[::1]:2"], ListenAddressPolicy.SplitUrls("http://127.0.0.1:1; http://[::1]:2;"));
    }

    /// <summary>One bad address in a list is enough to refuse the whole list.</summary>
    [Fact]
    public void EnsureLoopbackOnly_RefusesAListThatHoldsOneBadAddress()
    {
        Assert.Throws<InvalidOperationException>(
            () => ListenAddressPolicy.EnsureLoopbackOnly(["http://127.0.0.1:8430", "http://0.0.0.0:8431"]));
    }

    /// <summary>The check of the settings reads urls, the Kestrel endpoints and the port settings, before the server binds.</summary>
    [Theory]
    [InlineData("urls", "http://0.0.0.0:8430", false)]
    [InlineData("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:8430", false)]
    [InlineData("HTTP_PORTS", "8080", false)]
    [InlineData("HTTPS_PORTS", "8443", false)]
    [InlineData("Kestrel:Endpoints:Http:Url", "http://127.0.0.1:8430", true)]
    [InlineData("urls", "http://127.0.0.1:8430", true)]
    public void EnsureLoopbackOnly_ChecksEverySetting(string key, string value, bool accepted)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();

        var exception = Record.Exception(() => ListenAddressPolicy.EnsureLoopbackOnly(configuration));

        Assert.Equal(accepted, exception is null);
    }

    /// <summary>Kestrel ignores the port settings when urls is set. So a container image that sets HTTP_PORTS still starts on loopback.</summary>
    [Fact]
    public void PortSettings_AreAllowed_WhenUrlsIsSet()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["urls"] = "http://127.0.0.1:8430",
            ["HTTP_PORTS"] = "8080",
        }).Build();

        Assert.Null(Record.Exception(() => ListenAddressPolicy.EnsureLoopbackOnly(configuration)));
    }
}
