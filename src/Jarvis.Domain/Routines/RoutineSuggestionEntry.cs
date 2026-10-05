namespace Jarvis.Domain.Routines;

/// <summary>
/// A repeated behaviour Jarvis noticed and the automation it proposes. <see cref="Fingerprint"/> identifies the
/// pattern so a refresh updates the same row and a dismissed pattern is never offered again.
/// <see cref="DefinitionJson"/> is a complete automation definition; accepting creates it as a draft and records
/// the new rule in <see cref="AutomationId"/>.
/// </summary>
public sealed record RoutineSuggestionEntry(
    Guid Id,
    Guid OwnerId,
    string Fingerprint,
    string Title,
    string Evidence,
    double Confidence,
    string DefinitionJson,
    string Status,
    Guid? AutomationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
