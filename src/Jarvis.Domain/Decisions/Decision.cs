namespace Jarvis.Domain.Decisions;

/// <summary>
/// A call the owner made, with a prediction and how sure they were. <see cref="Probability"/> is the chance the
/// owner gave that the prediction comes true. On <see cref="ReviewOn"/> they say whether it did (<see cref="Outcome"/>),
/// which is what the calibration score is computed from. <see cref="ReminderId"/> is the reminder that asks them.
/// </summary>
public sealed record Decision(
    Guid Id,
    Guid OwnerId,
    string Title,
    string? Context,
    string Prediction,
    double Probability,
    DateOnly ReviewOn,
    bool? Outcome,
    string? OutcomeNote,
    DateTimeOffset? ResolvedAt,
    Guid? ReminderId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsResolved => Outcome is not null;
}
