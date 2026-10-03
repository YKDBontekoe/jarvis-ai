using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents;

/// <summary>Whether Codex is signed in, and the device code for a sign-in that is waiting for the owner.</summary>
public sealed record CodexSignInStatus(bool SignedIn, CodexDeviceCode? Pending, string? Error);

public sealed record CodexDeviceCode(string VerificationUrl, string UserCode, DateTimeOffset ExpiresAt);

/// <summary>
/// Signs the Codex CLI in to ChatGPT from the app, with the CLI's device code flow
/// (<c>codex login --device-auth</c>): the owner opens the link on any device and enters the code, and the CLI
/// writes <c>auth.json</c> into CODEX_HOME, which the API, worker and voice runtime share. A sign-in can only start
/// while Codex is signed out, so nobody can switch the server's ChatGPT account from the app.
/// </summary>
public sealed partial class CodexSignIn(CodexExecutable executable, ILogger<CodexSignIn> logger) : IDisposable
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private CodexDeviceCode? _pending;
    private string? _error;

    public async Task<CodexSignInStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var signedIn = await IsSignedInAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (signedIn || _pending is { } code && code.ExpiresAt <= DateTimeOffset.UtcNow) StopPending();
            return new CodexSignInStatus(signedIn, _pending, signedIn ? null : _error);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Starts a device-code sign-in, or returns the one already waiting.</summary>
    public async Task<CodexSignInStatus> StartAsync(CancellationToken cancellationToken)
    {
        if (await IsSignedInAsync(cancellationToken))
            throw new InvalidOperationException("Codex is already signed in on this server.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pending is { } waiting && waiting.ExpiresAt > DateTimeOffset.UtcNow && _process is { HasExited: false })
                return new CodexSignInStatus(false, waiting, null);
            StopPending();
            _error = null;

            var process = new Process { StartInfo = StartInfo(executable.Resolve(), "login", "--device-auth") };
            if (!process.Start()) throw new InvalidOperationException("The Codex CLI did not start.");
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            _process = process;
            var output = new StringBuilder();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartTimeout);
            CodexDeviceCode? code = null;
            try
            {
                while (code is null && await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                {
                    output.AppendLine(line);
                    code = ParseDeviceCode(output.ToString(), DateTimeOffset.UtcNow);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }

            if (code is null)
            {
                StopPending();
                LogNoCode(logger);
                throw new InvalidOperationException("Codex did not return a sign-in code. Try again in a minute.");
            }
            _pending = code;
            _ = WatchAsync(process);
            return new CodexSignInStatus(false, code, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the link and one-time code from the CLI's device-auth output.</summary>
    public static CodexDeviceCode? ParseDeviceCode(string output, DateTimeOffset now)
    {
        var text = AnsiEscape().Replace(output, "");
        var url = Url().Match(text);
        var code = UserCode().Match(text);
        return url.Success && code.Success ? new CodexDeviceCode(url.Value, code.Value, now.Add(CodeLifetime)) : null;
    }

    private async Task<bool> IsSignedInAsync(CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(executable.Resolve(), "login", "status") };
        try
        {
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                              System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return false;
        }
    }

    private async Task WatchAsync(Process process)
    {
        try
        {
            // Keep reading so the CLI never blocks on a full pipe while it waits for the owner.
            var drain = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(CodeLifetime + TimeSpan.FromMinutes(1));
            await process.WaitForExitAsync(timeout.Token);
            await drain;
            await _gate.WaitAsync();
            try
            {
                if (!ReferenceEquals(_process, process)) return;
                _error = process.ExitCode == 0 ? null : "The sign-in did not finish. Start again to get a new code.";
                _pending = null;
                _process = null;
            }
            finally
            {
                _gate.Release();
            }
            if (process.ExitCode == 0) LogSignedIn(logger);
        }
        catch (OperationCanceledException)
        {
            await _gate.WaitAsync();
            try
            {
                if (ReferenceEquals(_process, process)) StopPending();
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private void StopPending()
    {
        try { if (_process is { HasExited: false } running) running.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        _process = null;
        _pending = null;
    }

    private static ProcessStartInfo StartInfo(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["NO_COLOR"] = "1";
        return start;
    }

    public void Dispose()
    {
        StopPending();
        _gate.Dispose();
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();

    [GeneratedRegex(@"https://[^\s]+/device[^\s]*")]
    private static partial Regex Url();

    [GeneratedRegex(@"\b[A-Z0-9]{4}-[A-Z0-9]{4,6}\b")]
    private static partial Regex UserCode();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Codex device sign-in returned no code.")]
    private static partial void LogNoCode(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Codex is signed in to ChatGPT.")]
    private static partial void LogSignedIn(ILogger logger);
}
