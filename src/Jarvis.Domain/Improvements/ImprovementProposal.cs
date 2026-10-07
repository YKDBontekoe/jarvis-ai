namespace Jarvis.Domain.Improvements;

/// <summary>
/// A change Jarvis thinks would make it better, waiting for the owner or already made. <see cref="Fingerprint"/>
/// identifies the change so a rerun updates the same row, and a dismissed or undone one is never offered again.
/// <see cref="PayloadJson"/> holds what accepting applies; <see cref="ResultingRef"/> is the memory or skill it created
/// or changed, which is what undo reverses.
/// </summary>
public sealed record ImprovementProposal(
    Guid Id,
    Guid OwnerId,
    string Kind,
    string Fingerprint,
    string Title,
    string Evidence,
    double Confidence,
    string PayloadJson,
    string Status,
    Guid? ResultingRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
