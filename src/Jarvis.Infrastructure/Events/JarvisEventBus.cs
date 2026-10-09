using Jarvis.Application.Events;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Events;

/// <summary>
/// Runs every <see cref="IJarvisEventHandler"/> for an event, one after another in registration order. One handler
/// failing is logged and never stops the others.
/// </summary>
public sealed class JarvisEventBus(
    IEnumerable<IJarvisEventHandler> handlers,
    ILogger<JarvisEventBus> logger,
    TimeProvider? timeProvider = null) : IJarvisEventBus
{
    private readonly IJarvisEventHandler[] _handlers = handlers.ToArray();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task PublishAsync(JarvisEvent ev, CancellationToken cancellationToken)
    {
        if (ev.OwnerId == Guid.Empty) throw new ArgumentException("An event needs an owner.", nameof(ev));
        ev = ev.Normalize(_clock.GetUtcNow());
        foreach (var handler in _handlers)
        {
            try
            {
                if (handler.Handles(ev)) await handler.HandleAsync(ev, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Event handler {Handler} failed for {EventKind}.",
                    handler.GetType().Name, ev.Kind);
            }
        }
    }
}
