using Jarvis.Application.Search;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class FederatedSearchTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Conversation_provider_search_is_owner_scoped_through_federated_service()
    {
        var owner = Guid.CreateVersion7();
        var otherOwner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var store = new ConversationStore(database);
        var owned = await store.CreateAsync(owner, "Federated search conversation", CancellationToken.None);
        await store.CreateAsync(otherOwner, "Federated search conversation", CancellationToken.None);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        services.AddScoped<IFederatedSearchProvider, ConversationSearchProvider>();
        services.AddSingleton<IOptions<FederatedSearchOptions>>(Options.Create(new FederatedSearchOptions()));
        services.AddSingleton<IFederatedSearchService, Jarvis.Infrastructure.Search.FederatedSearchService>();

        await using var provider = services.BuildServiceProvider();
        var search = provider.GetRequiredService<IFederatedSearchService>();
        var response = await search.SearchAsync(owner, "Federated", null, CancellationToken.None);
        var hit = Assert.Single(response.Results);
        Assert.Equal(SearchResultKinds.Conversation, hit.Kind);
        Assert.Equal(owned.Id.ToString(), hit.Id);
        Assert.Equal("conversation", hit.Route.Kind);
        Assert.Equal(owned.Id.ToString(), hit.Route.Parameters["conversationId"]);
    }

    private JarvisDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);
}
