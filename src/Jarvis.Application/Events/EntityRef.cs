using System.Diagnostics.CodeAnalysis;

namespace Jarvis.Application.Events;

/// <summary>The kinds of thing an <see cref="EntityRef"/> can point at. One vocabulary for events, links, tools and the app.</summary>
public static class EntityTypes
{
    public const string Task = "task";
    public const string Reminder = "reminder";
    public const string Watch = "watch";
    public const string Memory = "memory";
    public const string Journal = "journal";
    public const string Expense = "expense";
    public const string Project = "project";
    public const string Conversation = "conversation";
    public const string Approval = "approval";
    public const string Notification = "notification";
    public const string Person = "person";
    public const string Decision = "decision";
    public const string Mission = "mission";
    public const string File = "file";
    public const string Commitment = "commitment";
    public const string InboxThread = "inbox_thread";
    public const string Automation = "automation";
    public const string Habit = "habit";
    public const string Library = "library";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Task, Reminder, Watch, Memory, Journal, Expense, Project, Conversation, Approval, Notification, Person,
        Decision, Mission, File, Commitment, InboxThread, Automation, Habit, Library
    };

    public static bool IsValid([NotNullWhen(true)] string? type) => type is not null && All.Contains(type);
}

/// <summary>
/// A pointer to one owner-scoped thing, written <c>type:id</c> (for example <c>task:0190…</c>). The id alone never
/// grants access: whoever resolves a ref still checks the owner.
/// </summary>
public readonly record struct EntityRef(string Type, Guid Id)
{
    public override string ToString() => $"{Type}:{Id:D}";

    public static EntityRef Of(string type, Guid id) =>
        EntityTypes.IsValid(type) ? new EntityRef(type, id) : throw new ArgumentException($"Unknown entity type '{type}'.", nameof(type));

    public static bool TryParse([NotNullWhen(true)] string? value, out EntityRef entity)
    {
        entity = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var separator = value.IndexOf(':');
        if (separator <= 0) return false;
        var type = value[..separator].Trim().ToLowerInvariant();
        if (!EntityTypes.IsValid(type) || !Guid.TryParse(value[(separator + 1)..].Trim(), out var id) || id == Guid.Empty)
            return false;
        entity = new EntityRef(type, id);
        return true;
    }

    public static EntityRef Parse(string value) =>
        TryParse(value, out var entity) ? entity : throw new FormatException($"'{value}' is not an entity reference.");
}
