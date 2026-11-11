using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Options;
using Xat.Orchestrator.Infrastructure.Webhooks;

namespace Xat.Orchestrator.Infrastructure.Tests;

/// <summary>Tests the rule that decides to which addresses the orchestrator sends webhooks.</summary>
public class WebhookPolicyTests
{
    private static WebhookPolicy Policy(params string[] allowed)
    {
        var options = new WebhookOptions();
        options.AllowedHosts.AddRange(allowed);
        return new WebhookPolicy(Microsoft.Extensions.Options.Options.Create(options));
    }

    /// <summary>By default only loopback hosts are allowed.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:9000/hook")]
    [InlineData("http://localhost/hook")]
    [InlineData("https://LOCALHOST:9000/hook?x=1")]
    [InlineData("http://[::1]:9000/hook")]
    public void Loopback_IsAllowedByDefault(string url)
    {
        Assert.True(Policy().Check(url).IsSuccess);
    }

    /// <summary>A host that is not loopback and not in the list is refused. The error never repeats the address.</summary>
    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("http://192.168.1.5/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public void OtherHosts_AreRefusedByDefault(string url)
    {
        var result = Policy().Check(url);

        Assert.False(result.IsSuccess);
        Assert.Equal("webhook.host_not_allowed", result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.DoesNotContain("example.com", result.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>A host in the list is allowed, with any case and any port.</summary>
    [Fact]
    public void ListedHost_IsAllowed()
    {
        var policy = Policy("Hooks.Example.COM");

        Assert.True(policy.Check("https://hooks.example.com:8443/x").IsSuccess);
        Assert.False(policy.Check("https://other.example.com/x").IsSuccess);
    }

    /// <summary>The value star allows every host.</summary>
    [Fact]
    public void Star_AllowsEveryHost()
    {
        Assert.True(Policy("*").Check("https://anything.example/x").IsSuccess);
    }

    /// <summary>Only absolute http and https addresses are valid. An address with credentials is refused.</summary>
    [Theory]
    [InlineData("ftp://127.0.0.1/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    [InlineData("http://user:pass@127.0.0.1/x")]
    public void InvalidAddresses_AreRefused(string url)
    {
        var result = Policy("*").Check(url);

        Assert.Equal("webhook.invalid_url", result.Error!.Code);
        Assert.DoesNotContain("user:pass", result.Error.Message, StringComparison.Ordinal);
    }
}
