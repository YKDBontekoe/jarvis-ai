namespace Jarvis.Application.Search;

public static class SearchResultKinds
{
    public const string Conversation = "conversation";
    public const string Memory = "memory";
    public const string File = "file";
    public const string Task = "task";
    public const string Reminder = "reminder";
    public const string Skill = "skill";
    public const string GraphEntity = "graph_entity";
    public const string ChannelThread = "channel_thread";
    public const string CodingRun = "coding_run";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Conversation, Memory, File, Task, Reminder, Skill, GraphEntity, ChannelThread, CodingRun
    };

    public static bool IsValid(string? kind) => kind is not null && All.Contains(kind);

    public static bool IsValidFilter(IReadOnlySet<string>? kinds) =>
        kinds is null || kinds.Count == 0 || kinds.All(IsValid);
}

/// <summary>Typed navigation target; clients must not interpolate arbitrary URLs from search hits.</summary>
public sealed record SearchRouteTarget(string Kind, IReadOnlyDictionary<string, string> Parameters);

public sealed record FederatedSearchResult(
    string Kind,
    string Id,
    string Title,
    string? Summary,
    DateTimeOffset? Timestamp,
    SearchRouteTarget Route,
    double Relevance,
    bool IsPinned);

public sealed record SearchProviderStatus(string ProviderId, bool Succeeded, string? Error);

public sealed record FederatedSearchResponse(
    IReadOnlyList<FederatedSearchResult> Results,
    IReadOnlyList<SearchProviderStatus> Providers);

public interface IFederatedSearchProvider
{
    string ProviderId { get; }
    IReadOnlySet<string> ResultKinds { get; }
    Task<IReadOnlyList<FederatedSearchResult>> SearchAsync(Guid ownerId, string query, int limit,
        CancellationToken cancellationToken);
}

public interface IFederatedSearchService
{
    Task<FederatedSearchResponse> SearchAsync(Guid ownerId, string query, IReadOnlySet<string>? kindsFilter,
        CancellationToken cancellationToken);
}

public sealed class FederatedSearchOptions
{
    public const string SectionName = "Search";
    public int MaxPerProvider { get; set; } = 8;
    public int MaxTotal { get; set; } = 40;
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
