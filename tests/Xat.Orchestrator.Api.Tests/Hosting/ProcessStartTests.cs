using System.Diagnostics;

namespace Xat.Orchestrator.Api.Tests.Hosting;

/// <summary>
/// Starts the real program with a bad setting. The operator must see a plain message, the exit code must be 1, and the output
/// must have no stack trace. The test host cannot show this, because it reports every failed start as an exception.
/// </summary>
public class ProcessStartTests
{
    /// <summary>Each bad setting, also two bad sections at once, stops the program with a plain message.</summary>
    [Theory]
    [InlineData("must be from 0 to 5", "--Upstreams:XatRag:RetryCount=9")]
    [InlineData("does not exist", "--Api:DefaultPipeline=missing")]
    [InlineData("Jobs:Workers", "--Jobs:Workers=0", "--Webhooks:RetryCount=99")]
    [InlineData("loopback", "--urls", "http://0.0.0.0:8497")]
    public async Task BadSetting_ExitsWithOne_AndAPlainMessage(string expected, params string[] arguments)
    {
        var program = Path.Combine(AppContext.BaseDirectory, "Xat.Orchestrator.Api.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(program);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add("http://127.0.0.1:0");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(limit.Token);
        var text = await errors + await output;

        Assert.Equal(1, process.ExitCode);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
    }
}
