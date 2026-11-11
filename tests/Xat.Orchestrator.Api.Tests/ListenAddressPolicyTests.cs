using Microsoft.AspNetCore.Mvc.Testing;

namespace Xat.Orchestrator.Api.Tests;

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
}
