namespace Jarvis.Application.Files;

/// <summary>
/// Limits file search to explicit file ids. When <see cref="FileIds"/> is null, search spans all owner files.
/// When it is an empty list, search returns no hits.
/// </summary>
public sealed record FileSearchScope(IReadOnlyList<Guid>? FileIds)
{
    public static FileSearchScope AllOwnerFiles { get; } = new((IReadOnlyList<Guid>?)null);

    public bool IsRestricted => FileIds is not null;
}
