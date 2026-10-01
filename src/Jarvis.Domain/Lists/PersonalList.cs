namespace Jarvis.Domain.Lists;

/// <summary>
/// A personal list the owner keeps, such as groceries or to-dos. <see cref="Kind"/> is "shopping", "todo", or
/// "general" and only changes how the list is presented. Items are ordered oldest first.
/// </summary>
public sealed record PersonalList(
    Guid Id,
    Guid OwnerId,
    string Name,
    string Kind,
    IReadOnlyList<ListItem> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public int OpenCount => Items.Count(item => !item.IsDone);
}

/// <summary>One line on a <see cref="PersonalList"/>; <see cref="DoneAt"/> is set while it is checked off.</summary>
public sealed record ListItem(
    Guid Id,
    Guid ListId,
    Guid OwnerId,
    string Text,
    bool IsDone,
    DateTimeOffset? DoneAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
