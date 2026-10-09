namespace Jarvis.Application.Events;

/// <summary>Why two things are related. Free text is allowed, but these are the ones Jarvis writes itself.</summary>
public static class LinkRelations
{
    /// <summary>The conversation (or task) where the other thing was made.</summary>
    public const string Created = "created";
    public const string About = "about";
    public const string FollowsUp = "follows_up";
    public const string Blocks = "blocks";
    public const string PartOf = "part_of";
    public const string Mentions = "mentions";
    public const string Source = "source";
    public const string Reminds = "reminds";
    public const string Related = "related";

    public const int MaxLength = 40;

    public static string Normalize(string? relation)
    {
        var value = (relation ?? "").Trim().ToLowerInvariant().Replace(' ', '_');
        if (value.Length == 0) return Related;
        return value.Length <= MaxLength ? value : value[..MaxLength];
    }
}

/// <summary>One stored link. Links are directional, but the related view reads both directions.</summary>
public sealed record EntityLinkRecord(Guid Id, Guid OwnerId, EntityRef From, EntityRef To, string Relation,
    DateTimeOffset CreatedAt);

public interface IEntityLinkRepository
{
    /// <returns>False when the same link already existed.</returns>
    Task<bool> AddAsync(Guid ownerId, EntityRef from, EntityRef to, string relation, CancellationToken cancellationToken);
    Task<IReadOnlyList<EntityLinkRecord>> ListForAsync(Guid ownerId, EntityRef entity, int limit, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid ownerId, EntityRef from, EntityRef to, string relation, CancellationToken cancellationToken);
}

/// <summary>
/// One thing related to another, ready to show: the ref, why it is related, which way the link points and a title
/// read with the owner's scope. <see cref="Title"/> is null when the thing is gone.
/// </summary>
public sealed record RelatedEntity(string Ref, string Type, string Relation, string Direction, string? Title,
    DateTimeOffset? At);

/// <summary>
/// Everything related to one thing: stored links plus the links the features already keep (a reminder's
/// conversation, a commitment's reminder, a journal entry's memory, a mission step's task).
/// </summary>
public interface IRelatedEntityService
{
    Task<IReadOnlyList<RelatedEntity>> GetRelatedAsync(Guid ownerId, EntityRef entity, CancellationToken cancellationToken);

    /// <summary>The owner-scoped title of a thing, or null when it does not exist for this owner.</summary>
    Task<string?> DescribeAsync(Guid ownerId, EntityRef entity, CancellationToken cancellationToken);
}

/// <summary>
/// Links the conversation in which Jarvis made something to the thing it made, so a reminder, task or memory can
/// always open the chat it came from.
/// </summary>
public sealed class ConversationLinkHandler(IEntityLinkRepository links) : IJarvisEventHandler
{
    public bool Handles(JarvisEvent ev) =>
        ev.ConversationId is { } conversation && conversation != Guid.Empty && ev.Subject is { } subject &&
        subject.Type != EntityTypes.Conversation && ev.Origin is EventOrigin.Agent or EventOrigin.AgentReaction;

    public async Task HandleAsync(JarvisEvent ev, CancellationToken cancellationToken) =>
        await links.AddAsync(ev.OwnerId, new EntityRef(EntityTypes.Conversation, ev.ConversationId!.Value),
            ev.Subject!.Value, LinkRelations.Created, cancellationToken);
}

public enum LinkOutcome { Linked, AlreadyLinked, NotFound, Invalid }

/// <summary>Links two of the owner's things after checking both exist for that owner.</summary>
public sealed class EntityLinker(IEntityLinkRepository links, IRelatedEntityService related)
{
    public async Task<LinkOutcome> LinkAsync(Guid ownerId, string? from, string? to, string? relation,
        CancellationToken cancellationToken)
    {
        if (!EntityRef.TryParse(from, out var source) || !EntityRef.TryParse(to, out var target) || source == target)
            return LinkOutcome.Invalid;
        if (await related.DescribeAsync(ownerId, source, cancellationToken) is null ||
            await related.DescribeAsync(ownerId, target, cancellationToken) is null)
            return LinkOutcome.NotFound;
        return await links.AddAsync(ownerId, source, target, LinkRelations.Normalize(relation), cancellationToken)
            ? LinkOutcome.Linked
            : LinkOutcome.AlreadyLinked;
    }

    public async Task<bool> UnlinkAsync(Guid ownerId, string? from, string? to, string? relation,
        CancellationToken cancellationToken) =>
        EntityRef.TryParse(from, out var source) && EntityRef.TryParse(to, out var target) &&
        await links.RemoveAsync(ownerId, source, target, LinkRelations.Normalize(relation), cancellationToken);
}
