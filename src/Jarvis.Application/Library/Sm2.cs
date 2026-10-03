using Jarvis.Domain.Library;

namespace Jarvis.Application.Library;

/// <summary>
/// The SM-2 spaced-repetition schedule. A review is graded 0-5: below 3 is a lapse (see it again tomorrow, ease
/// drops); 3 and up grows the interval (1 day, 6 days, then interval times ease) and nudges the ease.
/// </summary>
public static class Sm2
{
    public const double StartEase = 2.5;
    public const double MinEase = 1.3;
    public const int MaxIntervalDays = 365 * 3;

    /// <summary>Maps the app's four buttons (again, hard, good, easy) to a 0-5 grade.</summary>
    public static int QualityFor(string button) => button switch
    {
        "again" => 1,
        "hard" => 3,
        "good" => 4,
        "easy" => 5,
        _ => 4
    };

    public static Flashcard Review(Flashcard card, int quality, DateOnly today)
    {
        quality = Math.Clamp(quality, 0, 5);
        if (quality < 3)
        {
            return card with
            {
                Repetitions = 0,
                IntervalDays = 1,
                Lapses = card.Lapses + 1,
                Ease = Math.Max(MinEase, card.Ease - 0.2),
                DueOn = today.AddDays(1),
                LastReviewedOn = today
            };
        }

        var repetitions = card.Repetitions + 1;
        var interval = repetitions switch
        {
            1 => 1,
            2 => 6,
            _ => (int)Math.Round(Math.Max(1, card.IntervalDays) * card.Ease)
        };
        // A hard answer grows the gap less; an easy one a little more.
        if (quality == 3 && repetitions > 2) interval = Math.Max(card.IntervalDays + 1, (int)Math.Round(interval * 0.8));
        if (quality == 5 && repetitions > 2) interval = (int)Math.Round(interval * 1.3);
        interval = Math.Clamp(interval, 1, MaxIntervalDays);

        var ease = card.Ease + 0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02);
        return card with
        {
            Repetitions = repetitions,
            IntervalDays = interval,
            Ease = Math.Max(MinEase, Math.Round(ease, 3)),
            DueOn = today.AddDays(interval),
            LastReviewedOn = today
        };
    }
}
