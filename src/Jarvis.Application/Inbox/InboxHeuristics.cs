using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Inbox;

/// <summary>
/// Rule-based triage that needs no model: a question or request to the owner needs a reply, an unanswered
/// question from the owner is waiting, thanks and acknowledgements are done. Dutch and English cues. The model
/// triage replaces these guesses when the owner asks for it, and falls back to them when the model is unavailable.
/// </summary>
public static class InboxHeuristics
{
    private static readonly string[] RequestCues =
    [
        "can you", "could you", "would you", "will you", "please", "let me know", "do you", "are you", "when can",
        "what time", "call me", "kun je", "kan je", "wil je", "zou je", "laat maar weten", "laat me weten", "graag",
        "wanneer", "hoe laat", "bel me", "heb je", "ben je"
    ];

    private static readonly string[] AcknowledgementCues =
    [
        "ok", "okay", "oke", "oké", "thanks", "thank you", "thx", "top", "prima", "bedankt", "dankjewel", "dank je",
        "great", "cool", "👍", "🙏", "👌", "perfect", "yes", "ja", "nice", "mooi"
    ];

    private static readonly string[] UrgentCues =
    [
        "urgent", "asap", "emergency", "right now", "immediately", "dringend", "spoed", "noodgeval", "meteen",
        "direct", "nu meteen", "z.s.m", "zsm"
    ];

    private static readonly string[] TodayCues =
    [
        "today", "tonight", "this evening", "vandaag", "vanavond", "deze avond", "straks", "morgen", "tomorrow"
    ];

    public static readonly TimeSpan StaleReply = TimeSpan.FromDays(2);
    public static readonly TimeSpan StaleWaiting = TimeSpan.FromDays(3);

    /// <param name="messages">Any order; the newest one decides.</param>
    public static InboxTriage Triage(IReadOnlyList<InboxMessageView> messages, bool isGroup, DateTimeOffset now)
    {
        if (messages.Count == 0)
            return new InboxTriage(InboxStates.Fyi, InboxPriorities.Low, null, null, []);
        var last = messages.OrderByDescending(x => x.At).First();
        var text = last.Text.Trim();
        var lower = text.ToLowerInvariant();
        var asks = text.Contains('?') || ContainsCue(lower, RequestCues);
        var age = now - last.At;

        if (last.FromMe)
        {
            // The owner spoke last: a question is still open, anything else is finished.
            if (asks && !isGroup)
                return new InboxTriage(InboxStates.Waiting,
                    age > StaleWaiting ? InboxPriorities.High : InboxPriorities.Normal, null, null, []);
            return new InboxTriage(InboxStates.Done, InboxPriorities.Low, null, null, []);
        }

        var urgent = ContainsCue(lower, UrgentCues);
        var soon = ContainsCue(lower, TodayCues) || text.Count(c => c == '!') >= 2;

        if (IsAcknowledgement(lower) && !asks)
            return new InboxTriage(InboxStates.Done, InboxPriorities.Low, null, null, []);

        // Group chats are noisy: only direct requests count, everything else is for information.
        if (isGroup && !asks)
            return new InboxTriage(InboxStates.Fyi, InboxPriorities.Low, null, null, []);

        var priority = urgent ? InboxPriorities.Urgent
            : soon ? InboxPriorities.High
            : age > StaleReply ? InboxPriorities.High
            : InboxPriorities.Normal;
        var state = asks || !isGroup ? InboxStates.NeedsReply : InboxStates.Fyi;
        return new InboxTriage(state, priority, null, null, []);
    }

    private static bool IsAcknowledgement(string lower)
    {
        var words = lower.Split([' ', '.', ',', '!'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;
        return words.Length <= 4 && words.All(word => AcknowledgementCues.Contains(word)) ||
               AcknowledgementCues.Contains(lower.Trim());
    }

    private static bool ContainsCue(string lower, string[] cues)
    {
        // Punctuation becomes a space so "asap!" still matches the whole-word cue "asap".
        var padded = " " + new string(lower.Select(c => char.IsLetterOrDigit(c) || c > 127 ? c : ' ').ToArray()) + " ";
        return cues.Any(cue => padded.Contains(cue.Length < 5 ? $" {cue} " : cue, StringComparison.Ordinal));
    }

    /// <summary>
    /// A new message reopens a finished or snoozed thread; otherwise an owner-set state is kept so
    /// "done" is not undone by a sync with nothing new in it.
    /// </summary>
    public static bool ShouldRetriage(InboxThread existing, DateTimeOffset? newestMessageAt) =>
        newestMessageAt is { } newest && (existing.LastMessageAt is null || newest > existing.LastMessageAt);
}
