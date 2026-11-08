using Jarvis.Application.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Infrastructure.Search;

public static class SearchServiceCollectionExtensions
{
    public static IServiceCollection AddJarvisFederatedSearch(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FederatedSearchOptions>(configuration.GetSection(FederatedSearchOptions.SectionName));
        services.AddScoped<IFederatedSearchService, FederatedSearchService>();
        AddProvider<ConversationSearchProvider>(services);
        AddProvider<MemorySearchProvider>(services);
        AddProvider<FileSearchProvider>(services);
        AddProvider<TaskSearchProvider>(services);
        AddProvider<ReminderSearchProvider>(services);
        AddProvider<SkillSearchProvider>(services);
        AddProvider<GraphEntitySearchProvider>(services);
        AddProvider<ChannelThreadSearchProvider>(services);
        AddProvider<CodingRunSearchProvider>(services);
        AddProvider<MessageSearchProvider>(services);
        AddProvider<PersonSearchProvider>(services);
        AddProvider<CommitmentSearchProvider>(services);
        AddProvider<DecisionSearchProvider>(services);
        AddProvider<JournalSearchProvider>(services);
        AddProvider<ProjectSearchProvider>(services);
        AddProvider<MissionSearchProvider>(services);
        AddProvider<GoalSearchProvider>(services);
        AddProvider<ListSearchProvider>(services);
        AddProvider<TripSearchProvider>(services);
        return services;
    }

    // Registered by its own type too, so search can build one provider per scope without building them all.
    private static void AddProvider<TProvider>(IServiceCollection services)
        where TProvider : class, IFederatedSearchProvider
    {
        services.AddScoped<TProvider>();
        services.AddScoped<IFederatedSearchProvider>(provider => provider.GetRequiredService<TProvider>());
    }
}
