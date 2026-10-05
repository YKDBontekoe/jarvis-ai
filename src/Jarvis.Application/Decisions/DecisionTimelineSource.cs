using System.Globalization;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Decisions;

/// <summary>Shows decisions on the life timeline: one moment when logged and one when resolved.</summary>
public sealed class DecisionTimelineSource(IDecisionRepository decisions) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Decision };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var from = LocalClock.Resolve(window.From.ToDateTime(TimeOnly.MinValue), window.Zone);
        var to = LocalClock.Resolve(window.To.AddDays(1).ToDateTime(TimeOnly.MinValue), window.Zone);
        var found = await decisions.ListActiveBetweenAsync(window.OwnerId, from, to, window.Limit,
            cancellationToken);

        var events = new List<TimelineEvent>();
        foreach (var decision in found)
        {
            var chance = (decision.Probability * 100).ToString("0", CultureInfo.InvariantCulture);
            var title = TimelineRules.Shorten(decision.Title, TimelineRules.MaxTitleLength);
            if (decision.CreatedAt >= from && decision.CreatedAt < to)
                events.Add(new TimelineEvent($"{TimelineKinds.Decision}:{decision.Id}:logged", TimelineKinds.Decision,
                    title,
                    TimelineRules.Shorten($"Predicted: {decision.Prediction} ({chance}% sure)",
                        TimelineRules.MaxDetailLength),
                    decision.CreatedAt, TimelineRules.LocalDate(decision.CreatedAt, window.Zone), decision.Id));
            if (decision is { Outcome: { } outcome, ResolvedAt: { } resolvedAt } && resolvedAt >= from && resolvedAt < to)
                events.Add(new TimelineEvent($"{TimelineKinds.Decision}:{decision.Id}:resolved",
                    TimelineKinds.Decision, title,
                    TimelineRules.Shorten(
                        (outcome ? "It happened" : "It did not happen") + $" (you said {chance}%)" +
                        (string.IsNullOrWhiteSpace(decision.OutcomeNote) ? "" : ": " + decision.OutcomeNote),
                        TimelineRules.MaxDetailLength),
                    resolvedAt, TimelineRules.LocalDate(resolvedAt, window.Zone), decision.Id));
        }

        return events;
    }
}
