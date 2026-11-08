using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace Jarvis.Mcp;

/// <summary>
/// Keeps MCP server connections open between chat turns. <see cref="McpToolHost"/> lives for one request, and starting
/// a server (an npx or uvx process, or an HTTP session) took seconds in front of every message. A connection is keyed
/// by its owner and a hash of everything used to open it, credentials included, so a changed key or address opens a
/// new one and owners never share one. Connections are checked with a ping before reuse, closed after
/// <see cref="IdleLifetime"/> unused, and renewed after <see cref="MaxLifetime"/>.
/// </summary>
public sealed class McpConnectionPool : IAsyncDisposable
{
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly Timer _sweep;
    private bool _disposed;

    public McpConnectionPool() : this(TimeProvider.System) { }

    public McpConnectionPool(TimeProvider clock)
    {
        _clock = clock;
        _sweep = new Timer(_ => _ = SweepAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>A pooled connection held by one request until it calls <see cref="Release"/>.</summary>
    public sealed class Lease(string key, McpClient client, IList<McpClientTool> tools)
    {
        public string Key { get; } = key;
        public McpClient Client { get; } = client;
        public IList<McpClientTool> Tools { get; } = tools;
    }

    private sealed class Entry(McpClient client, IList<McpClientTool> tools, DateTimeOffset openedAt)
    {
        public McpClient Client { get; } = client;
        public IList<McpClientTool> Tools { get; } = tools;
        public DateTimeOffset OpenedAt { get; } = openedAt;
        public DateTimeOffset LastUsed { get; set; } = openedAt;
        public int Leases { get; set; }
        public bool Retired { get; set; }
    }

    /// <summary>
    /// The pool key for a server whose credentials are already resolved into <paramref name="server"/>. The values
    /// are hashed, never stored. Tool allowlists are left out: narrowing them reuses the same connection.
    /// </summary>
    public static string KeyFor(Guid ownerId, McpServerOptions server)
    {
        var material = JsonSerializer.Serialize(new
        {
            server.Name,
            server.Transport,
            server.Command,
            server.Endpoint,
            server.Arguments,
            server.WorkingDirectory,
            server.CredentialProvider,
            server.ConnectionTimeoutSeconds,
            Environment = server.EnvironmentVariables.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
            Headers = server.Headers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray()
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return ownerId.ToString("N") + ":" + hash;
    }

    /// <summary>Takes a healthy pooled connection for <paramref name="key"/>, or null when there is none.</summary>
    public async Task<Lease?> TryRentAsync(string key, CancellationToken cancellationToken)
    {
        Entry? entry;
        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(key, out entry)) return null;
            var now = _clock.GetUtcNow();
            if (entry.Retired || entry.Client.Completion.IsCompleted || now - entry.OpenedAt >= MaxLifetime)
            {
                RetireLocked(key, entry);
                entry = null;
            }
            else entry.Leases++;
        }
        if (entry is null)
        {
            await DisposeRetiredAsync();
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PingTimeout);
            await entry.Client.PingAsync(cancellationToken: timeout.Token);
            return new Lease(key, entry.Client, entry.Tools);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Release(new Lease(key, entry.Client, entry.Tools));
            throw;
        }
        catch (Exception)
        {
            // A dead or stuck server: drop it so the caller opens a fresh connection.
            Release(new Lease(key, entry.Client, entry.Tools), healthy: false);
            return null;
        }
    }

    /// <summary>
    /// Hands a new connection to the pool and leases it to the caller. When the key is already taken (two turns
    /// opened the same server at once) the caller keeps its connection unpooled and gets null.
    /// </summary>
    public Lease? Add(string key, McpClient client, IList<McpClientTool> tools)
    {
        lock (_sync)
        {
            if (_disposed || _entries.ContainsKey(key)) return null;
            var entry = new Entry(client, tools, _clock.GetUtcNow()) { Leases = 1 };
            _entries[key] = entry;
            return new Lease(key, client, tools);
        }
    }

    /// <summary>Gives a lease back. An unhealthy connection is closed once nobody else holds it.</summary>
    public void Release(Lease lease, bool healthy = true)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(lease.Key, out var entry) || !ReferenceEquals(entry.Client, lease.Client))
                return;
            entry.Leases = Math.Max(0, entry.Leases - 1);
            entry.LastUsed = _clock.GetUtcNow();
            if (!healthy) RetireLocked(lease.Key, entry);
        }
        _ = DisposeRetiredAsync();
    }

    public int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    private readonly List<Entry> _retired = [];

    private void RetireLocked(string key, Entry entry)
    {
        entry.Retired = true;
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
        if (!_retired.Contains(entry)) _retired.Add(entry);
    }

    private async Task DisposeRetiredAsync()
    {
        Entry[] closing;
        lock (_sync)
        {
            closing = _retired.Where(entry => entry.Leases == 0).ToArray();
            foreach (var entry in closing) _retired.Remove(entry);
        }
        foreach (var entry in closing)
        {
            try { await entry.Client.DisposeAsync(); }
            catch { /* Closing a connection that already failed must not fail anyone's turn. */ }
        }
    }

    internal async Task SweepAsync()
    {
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            foreach (var (key, entry) in _entries.ToArray())
            {
                if (entry.Leases == 0 && (now - entry.LastUsed >= IdleLifetime || now - entry.OpenedAt >= MaxLifetime ||
                                          entry.Client.Completion.IsCompleted))
                    RetireLocked(key, entry);
            }
        }
        await DisposeRetiredAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Entry[] all;
        lock (_sync)
        {
            _disposed = true;
            all = [.. _entries.Values, .. _retired];
            _entries.Clear();
            _retired.Clear();
        }
        await _sweep.DisposeAsync();
        foreach (var entry in all)
        {
            try { await entry.Client.DisposeAsync(); }
            catch { /* Shutting down. */ }
        }
    }
}
