namespace Jarvis.Api.Conversations;

/// <summary>
/// Tracks a chat, voice, or approval query that must finish after the caller's connection drops.
/// Closing the phone aborts the HTTP request; that abort is not a reason to stop the query.
/// The run ends on application shutdown, an explicit cancel, or the run timeout.
/// </summary>
public sealed class RemoteQueryHost
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(20);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, List<ActiveRun>> _runs = [];

    public bool IsResponding(Guid conversationId)
    {
        lock (_gate)
            return _runs.TryGetValue(conversationId, out var runs) && runs.Count > 0;
    }

    public RemoteQuery Begin(Guid ownerId, Guid conversationId, CancellationToken applicationStopping,
        TimeSpan? timeout = null)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner id is required.", nameof(ownerId));
        if (conversationId == Guid.Empty)
            throw new ArgumentException("A conversation id is required.", nameof(conversationId));

        var cts = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        var run = new ActiveRun(ownerId, conversationId, cts);
        lock (_gate)
        {
            if (!_runs.TryGetValue(conversationId, out var runs))
                _runs[conversationId] = runs = [];
            runs.Add(run);
        }

        return new RemoteQuery(cts.Token, () => End(run));
    }

    /// <summary>Stops the owner's in-flight query. A disconnect does not call this.</summary>
    public bool TryCancel(Guid ownerId, Guid conversationId)
    {
        List<ActiveRun> matched;
        lock (_gate)
        {
            if (!_runs.TryGetValue(conversationId, out var runs)) return false;
            matched = runs.Where(run => run.OwnerId == ownerId).ToList();
        }

        if (matched.Count == 0) return false;
        foreach (var run in matched) run.Cancel();
        return true;
    }

    private void End(ActiveRun run)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(run.ConversationId, out var runs)) return;
            runs.Remove(run);
            if (runs.Count == 0) _runs.Remove(run.ConversationId);
        }

        run.Dispose();
    }

    private sealed class ActiveRun(Guid ownerId, Guid conversationId, CancellationTokenSource cts) : IDisposable
    {
        public Guid OwnerId { get; } = ownerId;
        public Guid ConversationId { get; } = conversationId;

        public void Cancel()
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose() => cts.Dispose();
    }
}

public sealed class RemoteQuery(CancellationToken token, Action release) : IDisposable
{
    private int _released;

    public CancellationToken Token { get; } = token;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0) release();
    }
}
