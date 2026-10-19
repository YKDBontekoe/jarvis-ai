namespace Jarvis.Domain.Projects;

/// <summary>
/// Owner-named workspace that groups conversations, files, and tasks under shared instructions Jarvis
/// follows in every conversation of the project.
/// </summary>
public sealed class Project
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxInstructionsLength = 8_000;
    public const int MaxProjectsPerOwner = 100;
    public const string DefaultColor = "blue";
    public const int MaxOutcomeLength = 300;
    public const int MaxNextActionNoteLength = 200;

    public const string StatusActive = "active";
    public const string StatusCompleted = "completed";
    public const string StatusArchived = "archived";

    public static readonly IReadOnlySet<string> Statuses =
        new HashSet<string>(StringComparer.Ordinal) { StatusActive, StatusCompleted, StatusArchived };

    /// <summary>Accent colors the app knows how to paint; anything else falls back to <see cref="DefaultColor"/>.</summary>
    public static readonly IReadOnlySet<string> Colors =
        new HashSet<string>(StringComparer.Ordinal) { "blue", "green", "orange", "pink", "purple", "teal", "gray" };

    private Project() { }

    public Project(Guid ownerId, string name, string? description, string? instructions, string? color)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = DateTimeOffset.UtcNow;
        Apply(name, description, instructions, color);
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Instructions { get; private set; }
    public string Color { get; private set; } = DefaultColor;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>What the owner is trying to achieve with the project, in their words.</summary>
    public string? Outcome { get; private set; }

    public string Status { get; private set; } = StatusActive;

    /// <summary>The owner's chosen next step: a typed link to an existing thing, a short note, or both.</summary>
    public string? NextActionType { get; private set; }
    public Guid? NextActionId { get; private set; }
    public string? NextActionNote { get; private set; }

    /// <summary>
    /// Sets the outcome and next action together, so a link and its note never drift apart. A null
    /// <paramref name="status"/> leaves the status as it is.
    /// </summary>
    public void SetFocus(string? outcome, string? status, string? nextActionType, Guid? nextActionId,
        string? nextActionNote)
    {
        var cleanOutcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim();
        if (cleanOutcome is { Length: > MaxOutcomeLength })
            throw new ArgumentException($"Outcome must be {MaxOutcomeLength} characters or fewer.", nameof(outcome));
        var cleanNote = string.IsNullOrWhiteSpace(nextActionNote) ? null : nextActionNote.Trim();
        if (cleanNote is { Length: > MaxNextActionNoteLength })
            throw new ArgumentException($"The note must be {MaxNextActionNoteLength} characters or fewer.",
                nameof(nextActionNote));
        var cleanStatus = string.IsNullOrWhiteSpace(status) ? Status : status.Trim().ToLowerInvariant();
        if (!Statuses.Contains(cleanStatus))
            throw new ArgumentException("Use active, completed or archived.", nameof(status));
        if ((nextActionType is null) != (nextActionId is null))
            throw new ArgumentException("A linked next action needs both a type and an id.", nameof(nextActionType));

        Outcome = cleanOutcome;
        Status = cleanStatus;
        NextActionType = nextActionType;
        NextActionId = nextActionId;
        NextActionNote = cleanNote;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Update(string name, string? description, string? instructions, string? color)
    {
        Apply(name, description, instructions, color);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the project as recently used, so it rises in the sidebar.</summary>
    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private void Apply(string name, string? description, string? instructions, string? color)
    {
        var normalizedName = string.Join(' ', (name ?? string.Empty).Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalizedName.Length == 0) throw new ArgumentException("Name is required.", nameof(name));
        if (normalizedName.Length > MaxNameLength)
            throw new ArgumentException($"Name must be {MaxNameLength} characters or fewer.", nameof(name));

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > MaxDescriptionLength })
            throw new ArgumentException($"Description must be {MaxDescriptionLength} characters or fewer.",
                nameof(description));

        var normalizedInstructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
        if (normalizedInstructions is { Length: > MaxInstructionsLength })
            throw new ArgumentException("Instructions must be 8,000 characters or fewer.",
                nameof(instructions));

        var normalizedColor = string.IsNullOrWhiteSpace(color) ? DefaultColor : color.Trim().ToLowerInvariant();
        if (!Colors.Contains(normalizedColor))
            throw new ArgumentException("Choose one of the project colors.", nameof(color));

        Name = normalizedName;
        Description = normalizedDescription;
        Instructions = normalizedInstructions;
        Color = normalizedColor;
    }
}
