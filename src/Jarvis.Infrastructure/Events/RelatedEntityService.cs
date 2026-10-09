using Jarvis.Application.Events;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Events;

/// <summary>
/// Reads what is related to a thing from two places: the stored <c>entity_links</c>, and the links features already
/// keep as columns (a reminder's conversation, a commitment's reminder, a journal entry's memory, a mission step's
/// task, a memory's source). Every query is scoped to the owner, and titles are read the same way, so a ref to
/// someone else's thing resolves to nothing.
/// </summary>
public sealed class RelatedEntityService(JarvisDbContext db, IEntityLinkRepository links) : IRelatedEntityService
{
    private const int MaxRelated = 40;
    private const int TitleLength = 120;

    public async Task<IReadOnlyList<RelatedEntity>> GetRelatedAsync(Guid ownerId, EntityRef entity,
        CancellationToken cancellationToken)
    {
        if (await DescribeAsync(ownerId, entity, cancellationToken) is null) return [];

        var found = new List<(EntityRef Ref, string Relation, string Direction, DateTimeOffset? At)>();
        foreach (var link in await links.ListForAsync(ownerId, entity, MaxRelated, cancellationToken))
        {
            var outgoing = link.From == entity;
            found.Add((outgoing ? link.To : link.From, link.Relation, outgoing ? "out" : "in", link.CreatedAt));
        }

        found.AddRange(await ImplicitAsync(ownerId, entity, cancellationToken));

        var result = new List<RelatedEntity>();
        foreach (var group in found.GroupBy(x => x.Ref).Take(MaxRelated))
        {
            var first = group.OrderByDescending(x => x.At ?? DateTimeOffset.MinValue).First();
            var title = await DescribeAsync(ownerId, first.Ref, cancellationToken);
            if (title is null) continue;
            result.Add(new RelatedEntity(first.Ref.ToString(), first.Ref.Type, first.Relation, first.Direction, title,
                first.At));
        }
        return result;
    }

    private async Task<IReadOnlyList<(EntityRef, string, string, DateTimeOffset?)>> ImplicitAsync(Guid ownerId,
        EntityRef entity, CancellationToken ct)
    {
        var id = entity.Id;
        var list = new List<(EntityRef, string, string, DateTimeOffset?)>();
        void Add(string type, Guid? target, string relation, string direction, DateTimeOffset? at = null)
        {
            if (target is { } value && value != Guid.Empty) list.Add((new EntityRef(type, value), relation, direction, at));
        }

        switch (entity.Type)
        {
            case EntityTypes.Reminder:
                Add(EntityTypes.Conversation, await db.Reminders.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => x.ConversationId).FirstOrDefaultAsync(ct), LinkRelations.Created, "in");
                foreach (var c in await db.Commitments.Where(x => x.OwnerId == ownerId && x.ReminderId == id)
                             .Select(x => new { x.Id, x.CreatedAt }).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Commitment, c.Id, LinkRelations.Reminds, "out", c.CreatedAt);
                foreach (var d in await db.Decisions.Where(x => x.OwnerId == ownerId && x.ReminderId == id)
                             .Select(x => new { x.Id, x.CreatedAt }).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Decision, d.Id, LinkRelations.Reminds, "out", d.CreatedAt);
                break;
            case EntityTypes.Task:
            {
                var task = await db.Tasks.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => new { x.ConversationId }).FirstOrDefaultAsync(ct);
                Add(EntityTypes.Conversation, task?.ConversationId, LinkRelations.Created, "in");
                foreach (var step in await db.MissionSteps.Where(x => x.OwnerId == ownerId && x.TaskId == id)
                             .Select(x => x.MissionId).Distinct().Take(5).ToListAsync(ct))
                    Add(EntityTypes.Mission, step, LinkRelations.PartOf, "out");
                foreach (var m in await db.Memories.Where(x => x.OwnerId == ownerId && x.SourceId == id)
                             .Select(x => new { x.Id, x.CreatedAt }).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Memory, m.Id, LinkRelations.Source, "out", m.CreatedAt);
                break;
            }
            case EntityTypes.Conversation:
            {
                var conversation = await db.Conversations.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => new { x.ProjectId }).FirstOrDefaultAsync(ct);
                Add(EntityTypes.Project, conversation?.ProjectId, LinkRelations.PartOf, "out");
                foreach (var r in await db.Reminders.Where(x => x.OwnerId == ownerId && x.ConversationId == id)
                             .Select(x => x.Id).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Reminder, r, LinkRelations.Created, "out");
                foreach (var t in await db.Tasks.Where(x => x.OwnerId == ownerId && x.ConversationId == id)
                             .Select(x => x.Id).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Task, t, LinkRelations.Created, "out");
                foreach (var m in await db.Memories.Where(x => x.OwnerId == ownerId && x.SourceId == id)
                             .Select(x => new { x.Id, x.CreatedAt }).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Memory, m.Id, LinkRelations.Created, "out", m.CreatedAt);
                break;
            }
            case EntityTypes.Memory:
            {
                var memory = await db.Memories.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => new { x.SourceType, x.SourceId }).FirstOrDefaultAsync(ct);
                if (memory?.SourceId is { } sourceId && SourceTypeToEntity(memory.SourceType) is { } sourceType)
                    Add(sourceType, sourceId, LinkRelations.Source, "in");
                Add(EntityTypes.Journal, await db.JournalEntries.Where(x => x.OwnerId == ownerId && x.MemoryId == id)
                    .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct), LinkRelations.Source, "in");
                break;
            }
            case EntityTypes.Journal:
                Add(EntityTypes.Memory, await db.JournalEntries.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => x.MemoryId).FirstOrDefaultAsync(ct), LinkRelations.Source, "out");
                break;
            case EntityTypes.Expense:
                Add(EntityTypes.File, await db.Expenses.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => x.ReceiptFileId).FirstOrDefaultAsync(ct), LinkRelations.Source, "out");
                break;
            case EntityTypes.File:
                foreach (var e in await db.Expenses.Where(x => x.OwnerId == ownerId && x.ReceiptFileId == id)
                             .Select(x => x.Id).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Expense, e, LinkRelations.Source, "in");
                break;
            case EntityTypes.Commitment:
            {
                var commitment = await db.Commitments.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => new { x.ReminderId, x.InboxThreadId }).FirstOrDefaultAsync(ct);
                Add(EntityTypes.Reminder, commitment?.ReminderId, LinkRelations.Reminds, "in");
                Add(EntityTypes.InboxThread, commitment?.InboxThreadId, LinkRelations.Source, "in");
                break;
            }
            case EntityTypes.InboxThread:
                foreach (var c in await db.Commitments.Where(x => x.OwnerId == ownerId && x.InboxThreadId == id)
                             .Select(x => new { x.Id, x.CreatedAt }).Take(10).ToListAsync(ct))
                    Add(EntityTypes.Commitment, c.Id, LinkRelations.Source, "out", c.CreatedAt);
                break;
            case EntityTypes.Decision:
                Add(EntityTypes.Reminder, await db.Decisions.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => x.ReminderId).FirstOrDefaultAsync(ct), LinkRelations.Reminds, "in");
                break;
            case EntityTypes.Mission:
                foreach (var t in await db.MissionSteps.Where(x => x.OwnerId == ownerId && x.MissionId == id && x.TaskId != null)
                             .OrderBy(x => x.Ordinal).Select(x => x.TaskId).Take(20).ToListAsync(ct))
                    Add(EntityTypes.Task, t, LinkRelations.PartOf, "in");
                break;
            case EntityTypes.Project:
                foreach (var c in await db.Conversations.Where(x => x.OwnerId == ownerId && x.ProjectId == id)
                             .Select(x => x.Id).Take(20).ToListAsync(ct))
                    Add(EntityTypes.Conversation, c, LinkRelations.PartOf, "in");
                break;
            case EntityTypes.Approval:
            {
                var approval = await db.ToolApprovals.Where(x => x.OwnerId == ownerId && x.Id == id)
                    .Select(x => new { x.ConversationId, x.TaskId }).FirstOrDefaultAsync(ct);
                Add(EntityTypes.Conversation, approval?.ConversationId, LinkRelations.Source, "in");
                Add(EntityTypes.Task, approval?.TaskId, LinkRelations.Source, "in");
                break;
            }
        }
        return list;
    }

    /// <summary>Memory sources are written by several features with their own words; map the ones that are refs.</summary>
    private static string? SourceTypeToEntity(string? sourceType) => sourceType?.ToLowerInvariant() switch
    {
        "conversation" or "chat" => EntityTypes.Conversation,
        "task" => EntityTypes.Task,
        "journal" => EntityTypes.Journal,
        "file" => EntityTypes.File,
        "library" => EntityTypes.Library,
        _ => null
    };

    public async Task<string?> DescribeAsync(Guid ownerId, EntityRef entity, CancellationToken ct)
    {
        var id = entity.Id;
        var title = entity.Type switch
        {
            EntityTypes.Task => await db.Tasks.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.Reminder => await db.Reminders.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.Watch => await db.ConditionWatches.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.Memory => await db.Memories.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Content).FirstOrDefaultAsync(ct),
            EntityTypes.Journal => await db.JournalEntries.Where(x => x.OwnerId == ownerId && x.Id == id)
                .Select(x => "Journal · " + x.EntryDate.ToString()).FirstOrDefaultAsync(ct),
            EntityTypes.Expense => await db.Expenses.Where(x => x.OwnerId == ownerId && x.Id == id)
                .Select(x => (x.Merchant ?? x.Category) + " · " + x.Amount.ToString() + " " + x.Currency).FirstOrDefaultAsync(ct),
            EntityTypes.Project => await db.Projects.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            EntityTypes.Conversation => await db.Conversations.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.Approval => await db.ToolApprovals.Where(x => x.OwnerId == ownerId && x.Id == id)
                .Select(x => "Approval · " + x.ToolName).FirstOrDefaultAsync(ct),
            EntityTypes.Notification => await db.Notifications.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.Person => await db.People.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            EntityTypes.Decision => await db.Decisions.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.Mission => await db.Missions.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.File => await db.Files.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.FileName).FirstOrDefaultAsync(ct),
            EntityTypes.Commitment => await db.Commitments.Where(x => x.OwnerId == ownerId && x.Id == id)
                .Select(x => x.Description).FirstOrDefaultAsync(ct),
            EntityTypes.InboxThread => await db.InboxThreads.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            EntityTypes.Automation => await db.AutomationRules.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            EntityTypes.Habit => await db.Habits.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),
            EntityTypes.Library => await db.LibraryItems.Where(x => x.OwnerId == ownerId && x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(ct),
            _ => null
        };
        if (title is null) return null;
        title = title.ReplaceLineEndings(" ").Trim();
        return title.Length <= TitleLength ? title : title[..(TitleLength - 1)] + "…";
    }
}
