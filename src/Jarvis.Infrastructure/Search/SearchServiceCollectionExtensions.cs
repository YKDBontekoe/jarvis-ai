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
        services.AddScoped<IFederatedSearchProvider, ConversationSearchProvider>();
        services.AddScoped<IFederatedSearchProvider, MemorySearchProvider>();
        services.AddScoped<IFederatedSearchProvider, FileSearchProvider>();
        services.AddScoped<IFederatedSearchProvider, TaskSearchProvider>();
        services.AddScoped<IFederatedSearchProvider, ReminderSearchProvider>();
        services.AddScoped<IFederatedSearchProvider, SkillSearchProvider>();
        services.AddScoped<IFederatedSearchProvider, GraphEntitySearchProvider>();
        services.AddScoped<IFederatedSearchProvider, ChannelThreadSearchProvider>();
        services.AddScoped<IFederatedSearchProvider, CodingRunSearchProvider>();
        return services;
    }
}
