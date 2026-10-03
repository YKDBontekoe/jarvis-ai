using Jarvis.Application.Inbox;
using Jarvis.Application.Timeline;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Infrastructure;

/// <summary>Registers the timeline, inbox, finance, studio, library, modes, and mission services.</summary>
public static class LifeFeaturesRegistration
{
    public static IServiceCollection AddJarvisLifeFeatures(this IServiceCollection services)
    {
        // Timeline: each source reads one area; the service merges them.
        services.AddScoped<ITimelineSource, JournalTimelineSource>();
        services.AddScoped<ITimelineSource, ExpenseTimelineSource>();
        services.AddScoped<ITimelineSource, HabitTimelineSource>();
        services.AddScoped<ITimelineSource, PeopleTimelineSource>();
        services.AddScoped<ITimelineSource, TaskTimelineSource>();
        services.AddScoped<ITimelineSource, ReminderTimelineSource>();
        services.AddScoped<ITimelineSource, MemoryTimelineSource>();
        services.AddScoped<ITimelineSource, ConversationTimelineSource>();
        services.AddScoped<ITimelineService, TimelineService>();

        // Inbox and commitments ledger.
        services.AddScoped<IInboxRepository, InboxRepository>();
        services.AddScoped<IInboxService, InboxService>();
        services.AddScoped<ICommitmentService, CommitmentService>();
        return services;
    }
}
