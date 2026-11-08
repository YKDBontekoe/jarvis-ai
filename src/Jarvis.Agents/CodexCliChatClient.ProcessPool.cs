using System.Diagnostics;
using System.Text;

namespace Jarvis.Agents;

/// <summary>
/// Keeps initialized Codex app-server processes between model calls. Starting one costs a process launch, the
/// initialize handshake and a graceful-exit wait, and an agent turn makes several model calls in a row (one per tool
/// step), so each call used to pay that again. A returned process keeps its own scratch directory, emptied between
/// calls, so the working directory Codex puts in front of the prompt also stays the same.
/// </summary>
public sealed partial class CodexCliChatClient
{
    /// <summary>A process is replaced after this many model calls so per-thread state cannot pile up.</summary>
    internal const int MaxProcessUses = 25;
    private static readonly TimeSpan IdleProcessLifetime = TimeSpan.FromMinutes(10);
    private readonly object _poolSync = new();
    private readonly Stack<AppServerProcess> _idle = new();
    private Task? _warming;
    private Timer? _idleSweep;
    private bool _disposed;
    private static int _generation;

    /// <summary>Stops every kept process from serving another call, for example after the Codex account changes.</summary>
    public static void RetireProcesses() => Interlocked.Increment(ref _generation);

    /// <summary>Processes kept ready between calls: enough for every concurrent model call the limiter allows.</summary>
    private int MaxIdleProcesses => Math.Max(1, _processSlots.Capacity);

    /// <summary>
    /// Starts a Codex process in the background when none is ready, so the next model call does not wait for a
    /// launch and handshake. Call it when a turn begins; the work before the first model call hides the start-up.
    /// </summary>
    public void Prewarm()
    {
        string executablePath;
        try { executablePath = executable.Resolve(); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              FileNotFoundException) { return; }
        lock (_poolSync)
        {
            if (_disposed || _idle.Count > 0 || _warming is { IsCompleted: false }) return;
            _warming = Task.Run(async () =>
            {
                AppServerProcess? started = null;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    started = await StartProcessAsync(executablePath, timeout.Token);
                    await GetModelCatalogAsync(started.Rpc, executablePath, timeout.Token);
                    Return(started);
                    started = null;
                }
                catch (Exception) { /* A failed warm-up costs nothing: the next call starts its own process. */ }
                finally
                {
                    if (started is not null) started.Dispose();
                }
            });
        }
    }

    private async Task<AppServerProcess> RentAsync(string executablePath, CancellationToken cancellationToken)
    {
        while (true)
        {
            AppServerProcess? candidate = null;
            Task? warming;
            lock (_poolSync)
            {
                if (_idle.Count > 0) candidate = _idle.Pop();
                warming = _warming;
            }
            if (candidate is not null)
            {
                if (candidate.CanServe(executablePath, DateTimeOffset.UtcNow, IdleProcessLifetime)) return candidate;
                candidate.Dispose();
                continue;
            }
            // A warm-up already under way finishes sooner than a fresh start, so wait for it briefly.
            if (warming is { IsCompleted: false })
            {
                try { await warming.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); }
                catch (TimeoutException) { }
                lock (_poolSync)
                {
                    if (_idle.Count > 0) continue;
                }
            }
            return await StartProcessAsync(executablePath, cancellationToken);
        }
    }

    private void Return(AppServerProcess process)
    {
        process.Uses++;
        if (process.Uses >= MaxProcessUses || !process.IsAlive || !process.TryClearScratch())
        {
            process.Dispose();
            return;
        }
        process.IdleSince = DateTimeOffset.UtcNow;
        lock (_poolSync)
        {
            if (!_disposed && _idle.Count < MaxIdleProcesses)
            {
                _idle.Push(process);
                _idleSweep ??= new Timer(_ => SweepIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                return;
            }
        }
        process.Dispose();
    }

    private void SweepIdle()
    {
        List<AppServerProcess> expired = [];
        lock (_poolSync)
        {
            var keep = _idle.Reverse()
                .Where(process => process.CanServe(process.ExecutablePath, DateTimeOffset.UtcNow, IdleProcessLifetime))
                .ToArray();
            expired.AddRange(_idle.Except(keep));
            _idle.Clear();
            foreach (var process in keep) _idle.Push(process);
        }
        foreach (var process in expired) process.Dispose();
    }

    private void DisposePool()
    {
        AppServerProcess[] idle;
        lock (_poolSync)
        {
            _disposed = true;
            idle = _idle.ToArray();
            _idle.Clear();
            _idleSweep?.Dispose();
            _idleSweep = null;
        }
        foreach (var process in idle) process.Dispose();
    }

    private async Task<AppServerProcess> StartProcessAsync(string executablePath, CancellationToken cancellationToken)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "jarvis-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        Process? process = null;
        AppServerProcess? started = null;
        try
        {
            process = new Process { StartInfo = CreateAppServerStart(executablePath, scratch, enableWebSearch, _access) };
            if (!process.Start()) throw new InvalidOperationException("Could not start the Codex CLI app-server.");
            started = new AppServerProcess(process, executablePath, scratch, _access);
            process = null;
            using var killOnCancellation = cancellationToken.Register(started.Kill);
            await started.Rpc.InitializeAsync(cancellationToken);
            var ready = started;
            started = null;
            return ready;
        }
        finally
        {
            if (started is not null) started.Dispose();
            else if (process is not null)
            {
                process.Dispose();
                TryDeleteDirectory(scratch);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>One running app-server with its pipes, its scratch directory and the last of its error output.</summary>
    private sealed class AppServerProcess : IDisposable
    {
        private const int StderrTailLength = 4_000;
        private readonly Process _process;
        private readonly StringBuilder _stderr = new();
        private readonly Task _stderrPump;
        private int _disposed;

        public AppServerProcess(Process process, string executablePath, string scratch, CodexAccess access)
        {
            _process = process;
            ExecutablePath = executablePath;
            Scratch = scratch;
            // Drain error output for the life of the process: a full pipe would stall it, and an unbounded copy would
            // grow forever now that processes live across calls.
            _stderrPump = Task.Run(PumpStderrAsync);
            Rpc = new AppServerConnection(process.StandardInput, process.StandardOutput, ReadStderrAfterExitAsync,
                access, scratch);
        }

        public string ExecutablePath { get; }
        public string Scratch { get; }
        public AppServerConnection Rpc { get; }
        public int Uses { get; set; }
        public DateTimeOffset IdleSince { get; set; } = DateTimeOffset.UtcNow;
        public int Generation { get; } = Volatile.Read(ref _generation);

        public bool IsAlive
        {
            get
            {
                try { return Volatile.Read(ref _disposed) == 0 && !_process.HasExited; }
                catch (InvalidOperationException) { return false; }
            }
        }

        public bool CanServe(string executablePath, DateTimeOffset now, TimeSpan idleLifetime) =>
            IsAlive && ExecutablePath == executablePath && now - IdleSince < idleLifetime &&
            Generation == Volatile.Read(ref _generation);

        /// <summary>Empties the scratch directory so the next call starts clean. False when it cannot.</summary>
        public bool TryClearScratch()
        {
            try
            {
                var directory = new DirectoryInfo(Scratch);
                if (!directory.Exists)
                {
                    directory.Create();
                    return true;
                }
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo child) child.Delete(recursive: true);
                    else entry.Delete();
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Kill()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Kill();
            try { _stderrPump.Wait(StderrDrainWait); }
            catch (AggregateException) { }
            _process.Dispose();
            TryDeleteDirectory(Scratch);
        }

        private async Task PumpStderrAsync()
        {
            var buffer = new char[1_024];
            try
            {
                int read;
                while ((read = await _process.StandardError.ReadAsync(buffer)) > 0)
                {
                    lock (_stderr)
                    {
                        _stderr.Append(buffer, 0, read);
                        if (_stderr.Length > StderrTailLength * 2)
                            _stderr.Remove(0, _stderr.Length - StderrTailLength);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or
                                                  InvalidOperationException) { }
        }

        private async Task<string> ReadStderrAfterExitAsync(CancellationToken cancellationToken)
        {
            try { await _stderrPump.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (TimeoutException) { }
            lock (_stderr)
            {
                return _stderr.Length <= StderrTailLength
                    ? _stderr.ToString()
                    : _stderr.ToString(_stderr.Length - StderrTailLength, StderrTailLength);
            }
        }
    }
}
