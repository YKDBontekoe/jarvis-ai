namespace Jarvis.Application.Usage;

public sealed record PersonalizationInputs(
    int ActiveMemories,
    int PinnedMemories,
    int MemoryKinds,
    int PersonaTraits,
    bool HasCustomInstructions,
    bool HasPreferredName,
    int FeedbackRatings,
    int SupersededMemories = 0);

/// <summary>
/// Scores how much Jarvis can tailor itself to the owner. Active memories supply most of the score;
/// pins, variety, persona traits, stated instructions, and ratings fill in the rest.
/// </summary>
public static class PersonalizationLevel
{
    public static PersonalizationSnapshot Score(PersonalizationInputs input)
    {
        var memories = Math.Max(0, input.ActiveMemories);
        var memoryPoints = 70d * (1d - Math.Exp(-memories / 8d));
        var pinPoints = Math.Min(8, Math.Max(0, input.PinnedMemories) * 2);
        var varietyPoints = Math.Min(8, Math.Max(0, input.MemoryKinds) * 2);
        var personaPoints = Math.Min(10, Math.Max(0, input.PersonaTraits) * 2);
        var instructionPoints = (input.HasCustomInstructions ? 3 : 0) + (input.HasPreferredName ? 2 : 0);
        var feedbackPoints = Math.Min(2, Math.Max(0, input.FeedbackRatings));
        var score = (int)Math.Clamp(Math.Round(
            memoryPoints + pinPoints + varietyPoints + personaPoints + instructionPoints + feedbackPoints), 0, 100);
        var band = score switch
        {
            < 15 => "New",
            < 35 => "Learning",
            < 60 => "Familiar",
            < 85 => "Close",
            _ => "Deep"
        };
        return new PersonalizationSnapshot(
            score, band, Summary(band, memories, input), memories,
            Math.Max(0, input.PinnedMemories), Math.Max(0, input.SupersededMemories),
            Math.Max(0, input.MemoryKinds), Math.Max(0, input.PersonaTraits),
            input.HasCustomInstructions, input.HasPreferredName, Math.Max(0, input.FeedbackRatings));
    }

    private static string Summary(string band, int memories, PersonalizationInputs input)
    {
        var lead = band switch
        {
            "New" => "Jarvis is just getting to know you",
            "Learning" => "Jarvis is starting to remember how you work",
            "Familiar" => "Jarvis has a working picture of you",
            "Close" => "Jarvis can tailor replies from a deep memory of you",
            _ => "Jarvis is highly personalized to you"
        };
        var parts = new List<string> { $"{memories} active {(memories == 1 ? "memory" : "memories")}" };
        if (input.PinnedMemories > 0)
            parts.Add($"{input.PinnedMemories} pinned");
        if (input.PersonaTraits > 0)
            parts.Add($"{input.PersonaTraits} persona {(input.PersonaTraits == 1 ? "trait" : "traits")}");
        return lead + ", from " + string.Join(", ", parts) + ".";
    }
}
