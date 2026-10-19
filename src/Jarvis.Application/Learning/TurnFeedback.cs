namespace Jarvis.Application.Learning;

/// <summary>
/// Which turns the owner judged, from the signals already kept: the last thumb on a reply wins, and a turn is bad when
/// it was rated down, asked for again (regenerate) or corrected in the next message. A correction is stored with the
/// owner's own message, so it belongs to the latest reply before it in that conversation.
/// </summary>
public sealed record TurnFeedback(IReadOnlySet<Guid> Bad, IReadOnlySet<Guid> Good)
{
    public static TurnFeedback From(IReadOnlyList<TurnTraceRecord> traces, IReadOnlyList<LearningSignalRecord> signals)
    {
        var thumbs = new Dictionary<Guid, string>();
        foreach (var signal in signals.Where(s => s.MessageId is not null &&
                                                  s.Kind is LearningSignalKinds.ThumbsDown or LearningSignalKinds.ThumbsUp)
                     .OrderBy(s => s.CreatedAt))
            thumbs[signal.MessageId!.Value] = signal.Kind;
        var regenerated = signals.Where(s => s.Kind == LearningSignalKinds.Regenerate && s.MessageId is not null)
            .Select(s => s.MessageId!.Value).ToHashSet();
        var corrected = new HashSet<Guid>();
        foreach (var correction in signals.Where(s => s.Kind == LearningSignalKinds.Correction))
        {
            var reply = traces.Where(trace => trace.ConversationId == correction.ConversationId &&
                                              trace.CreatedAt <= correction.CreatedAt)
                .OrderByDescending(trace => trace.CreatedAt).FirstOrDefault();
            if (reply is not null) corrected.Add(reply.Id);
        }

        var bad = new HashSet<Guid>();
        var good = new HashSet<Guid>();
        foreach (var trace in traces)
        {
            var thumb = trace.MessageId is { } message && thumbs.TryGetValue(message, out var kind) ? kind : null;
            var isBad = thumb == LearningSignalKinds.ThumbsDown || corrected.Contains(trace.Id) ||
                        (trace.MessageId is { } id && regenerated.Contains(id));
            if (isBad) bad.Add(trace.Id);
            else if (thumb == LearningSignalKinds.ThumbsUp) good.Add(trace.Id);
        }

        return new TurnFeedback(bad, good);
    }
}
