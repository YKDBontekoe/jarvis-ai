using Jarvis.Application.Events;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Infrastructure.Events;

/// <summary>
/// Registers the event spine: the bus, the event log, entity links and the handlers every host shares. Hosts add their
/// own handlers (such as Jarvis's reactions) with more <see cref="IJarvisEventHandler"/> registrations.
/// </summary>
public static class EventSpineRegistration
{
    public static IServiceCollection AddJarvisEventSpine(this IServiceCollection services)
    {
        services.AddScoped<IJarvisEventBus, JarvisEventBus>();
        services.AddScoped<IOwnerEventRepository, OwnerEventRepository>();
        services.AddScoped<IEntityLinkRepository, EntityLinkRepository>();
        services.AddScoped<IRelatedEntityService, RelatedEntityService>();
        services.AddScoped<EntityLinker>();

        // Order matters: the event is stored before anything reacts to it.
        services.AddScoped<IJarvisEventHandler, PersistingEventHandler>();
        services.AddScoped<IJarvisEventHandler, ConversationLinkHandler>();
        services.AddScoped<IJarvisEventHandler, RealtimeEventHandler>();
        return services;
    }
}
