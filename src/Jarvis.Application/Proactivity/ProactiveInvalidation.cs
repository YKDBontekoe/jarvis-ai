using Jarvis.Application.Events;

namespace Jarvis.Application.Proactivity;

/// <summary>Events invalidate existing explicitly enabled work; they never create or enable a responsibility.</summary>
public sealed class ProactiveInvalidation(IProactiveRepository repository) : IJarvisEventHandler
{
    public bool Handles(JarvisEvent ev) => ev.Subject is not null && ev.Origin != EventOrigin.AgentReaction;

    public async Task HandleAsync(JarvisEvent ev, CancellationToken ct)
    {
        var reference = ev.Subject!.Value.ToString();
        foreach (var value in (await repository.ListAsync(ev.OwnerId, ct)).Where(c =>
                     c.Enabled && !c.Terminal && c.Status is not ("paused" or "suggested") && c.SourceRefs.Contains(reference)))
        {
            // An active check will detect the changed revision and discard its prepared mutation.
            var next = value with { Revision = value.Revision + 1, NextCheckAt = ev.At ?? DateTimeOffset.UtcNow,
                RunId = null, LeaseUntil = null, LastTrigger = ev.Kind + ":" + ev.Id };
            await repository.CompareExchangeAsync(next, value.Revision, ct);
        }
    }
}
