using Jarvis.Agents;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CodexSignInTests
{
    [Fact]
    public void Reads_the_link_and_code_from_colored_cli_output()
    {
        const string output = "Welcome to Codex [v\u001b[90m0.145.0\u001b[0m]\n" +
            "1. Open this link in your browser and sign in to your account\n" +
            "   \u001b[94mhttps://auth.openai.com/codex/device\u001b[0m\n" +
            "2. Enter this one-time code \u001b[90m(expires in 15 minutes)\u001b[0m\n" +
            "   \u001b[94mAB12-CD34E\u001b[0m\n";
        var now = DateTimeOffset.Parse("2026-10-03T10:00:00Z");

        var code = CodexSignIn.ParseDeviceCode(output, now);

        Assert.NotNull(code);
        Assert.Equal("https://auth.openai.com/codex/device", code.VerificationUrl);
        Assert.Equal("AB12-CD34E", code.UserCode);
        Assert.Equal(now.AddMinutes(15), code.ExpiresAt);
    }

    [Fact]
    public void Waits_until_both_the_link_and_the_code_have_arrived() =>
        Assert.Null(CodexSignIn.ParseDeviceCode("1. Open this link\n   https://auth.openai.com/codex/device\n",
            DateTimeOffset.UtcNow));

    [Fact]
    public async Task Status_checks_share_one_cli_call_and_reads_stdin_free()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("jarvis-sign-in-").FullName;
        try
        {
            var cli = Path.Combine(directory, "codex");
            // Reads stdin like a wrapper would, so this also checks stdin is closed.
            File.WriteAllText(cli, "#!/bin/sh\necho call >> \"$(dirname \"$0\")/calls.log\"\ncat > /dev/null\nexit 0\n");
            File.SetUnixFileMode(cli, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);
            using var signIn = new CodexSignIn(new CodexExecutable(cli, Path.Combine(directory, "managed")),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CodexSignIn>.Instance);

            var started = System.Diagnostics.Stopwatch.StartNew();
            var statuses = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => signIn.GetStatusAsync(TestContext.Current.CancellationToken)));
            var again = await signIn.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.All(statuses, status => Assert.True(status.SignedIn));
            Assert.True(again.SignedIn);
            Assert.Single(File.ReadAllLines(Path.Combine(directory, "calls.log")));
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), $"took {started.Elapsed}");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
