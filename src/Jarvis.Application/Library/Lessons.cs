using System.Globalization;
using System.Text;
using Jarvis.Domain.Library;

namespace Jarvis.Application.Library;

/// <summary>A sentence taken word for word from the saved text, with where it came from.</summary>
public sealed record LessonSource(string Quote, int Offset);

public sealed record LessonPoint(string Text, LessonSource Source);

public sealed record LessonQuestion(
    string Id,
    string Kind,
    string Prompt,
    string Hint,
    LessonSource Source);

public static class LessonQuestionKinds
{
    public const string Blank = "blank";
    public const string ExplainBack = "explain_back";
}

public sealed record Lesson(
    Guid ItemId,
    string Title,
    string Overview,
    IReadOnlyList<LessonPoint> Points,
    IReadOnlyList<LessonQuestion> Questions,
    int Minutes,
    string Note);

public static class LessonVerdicts
{
    public const string Correct = "correct";
    public const string Close = "close";
    public const string Incorrect = "incorrect";
    public const string Unsure = "unsure";
}

/// <summary>
/// How an answer compares with the saved text. The expected answer always comes from the owner's own source and
/// is shown with it; a claim the text does not make is never graded as fact, and "I don't know" is welcome.
/// </summary>
public sealed record LessonGrade(string Verdict, string Expected, LessonSource Source, string Message);

public sealed record LessonCheckRequest(string? QuestionId, string? Answer);

public sealed record LessonCardsRequest(IReadOnlyList<string>? QuestionIds);

/// <summary>
/// Builds a short lesson from a saved item without a model: it picks the sentences that carry the item's ideas,
/// quotes them with their position, and turns three of them into fill-in-the-blank questions plus one
/// explain-it-back prompt. Pure and deterministic, so the same text always gives the same lesson and the same
/// answer key.
/// </summary>
public static class LessonBuilder
{
    public const int MaxPoints = 5;
    public const int MaxBlanks = 3;
    public const int MinSentence = 40;
    public const int MaxSentence = 260;
    public const int MinTextLength = 120;
    public const int MaxCards = 5;

    public const string Note = "Every point is a sentence from your saved text, quoted with where it is. Nothing here is invented.";

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "about", "after", "again", "also", "because", "before", "being", "between", "could", "every", "first",
        "from", "have", "into", "more", "most", "other", "over", "some", "such", "than", "that", "their", "them",
        "then", "there", "these", "they", "this", "those", "through", "under", "very", "were", "what", "when",
        "where", "which", "while", "will", "with", "would", "your", "zijn", "voor", "maar", "naar", "niet", "onder",
        "ook", "over", "veel", "waar", "want", "wordt", "worden", "deze", "deze", "door", "echter", "heeft", "hebben"
    };

    public static Lesson? Build(LibraryItem item)
    {
        var text = item.Content ?? string.Empty;
        if (text.Trim().Length < MinTextLength) return null;
        var sentences = Sentences(text);
        if (sentences.Count == 0) return null;

        var topic = Words($"{item.Title} {string.Join(' ', item.KeyPoints)} {item.Summary}").ToHashSet();
        var scored = sentences
            .Select((sentence, index) => (sentence, index,
                score: Words(sentence.Quote).Count(topic.Contains) * 2 + (index < 3 ? 1 : 0)))
            .OrderByDescending(entry => entry.score).ThenBy(entry => entry.index)
            .Take(MaxPoints).OrderBy(entry => entry.index).Select(entry => entry.sentence).ToArray();

        var points = scored.Select(sentence => new LessonPoint(sentence.Quote, sentence)).ToArray();
        var blanks = new List<LessonQuestion>();
        for (var i = 0; i < points.Length && blanks.Count < MaxBlanks; i++)
        {
            var target = BlankTarget(points[i].Source.Quote, item.Title);
            if (target is null) continue;
            blanks.Add(new LessonQuestion($"b{blanks.Count + 1}", LessonQuestionKinds.Blank,
                "Fill in the missing word: " + Replace(points[i].Source.Quote, target),
                $"It starts with “{target[0]}” and has {target.Length} letters.", points[i].Source));
        }
        if (blanks.Count == 0) return null;

        var explain = new LessonQuestion("e1", LessonQuestionKinds.ExplainBack,
            $"In your own words, what is the main idea of “{item.Title}”?",
            "Mention the key terms you remember from the points above.", points[0].Source);
        var overview = !string.IsNullOrWhiteSpace(item.Summary) ? item.Summary : points[0].Text;
        return new Lesson(item.Id, item.Title, overview, points, [.. blanks, explain],
            Math.Clamp((int)Math.Ceiling(points.Sum(p => p.Text.Length) / 900.0) + 2, 3, 7), Note);
    }

    /// <summary>The answer key for a question, rebuilt from the same text so nothing is stored or trusted from a client.</summary>
    public static string? AnswerFor(LibraryItem item, string questionId)
    {
        var lesson = Build(item);
        var question = lesson?.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null) return null;
        return question.Kind == LessonQuestionKinds.Blank
            ? BlankTarget(question.Source.Quote, item.Title)
            : string.Join(", ", RubricTerms(question.Source.Quote, item.Title));
    }

    public static LessonGrade? Check(LibraryItem item, string? questionId, string? answer)
    {
        var lesson = Build(item);
        var question = lesson?.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null) return null;
        var given = Fold(answer ?? string.Empty).Trim();
        var source = question.Source;
        if (given.Length == 0 || given is "?" or "idk" or "i dont know" or "dont know" or "weet niet" or "geen idee")
            return new LessonGrade(LessonVerdicts.Unsure, Expected(question, item), source,
                "No problem. Here is what your text says; try again in a moment or add it to your cards.");

        if (question.Kind == LessonQuestionKinds.Blank)
        {
            var target = BlankTarget(source.Quote, item.Title)!;
            var expected = Fold(target);
            if (given == expected)
                return new LessonGrade(LessonVerdicts.Correct, target, source, "That matches your text.");
            if (Distance(given, expected) <= 1 && expected.Length >= 5)
                return new LessonGrade(LessonVerdicts.Close, target, source,
                    $"Very close. Your text says “{target}”.");
            return new LessonGrade(LessonVerdicts.Incorrect, target, source,
                $"Your text says “{target}”. If you think your answer is also right, you can correct this grade.");
        }

        var terms = RubricTerms(source.Quote, item.Title);
        var folded = Words(answer!).Select(Fold).ToHashSet();
        var hit = terms.Where(term => folded.Contains(Fold(term))).ToArray();
        var needed = Math.Min(2, terms.Count);
        return hit.Length >= needed
            ? new LessonGrade(LessonVerdicts.Correct, string.Join(", ", terms), source,
                $"You covered {string.Join(", ", hit)} from your text.")
            : new LessonGrade(hit.Length > 0 ? LessonVerdicts.Close : LessonVerdicts.Incorrect,
                string.Join(", ", terms), source,
                $"Your text leans on: {string.Join(", ", terms)}. You covered {hit.Length} of them; that is a check against the source, not a judgement of what you understand.");
    }

    public static IReadOnlyList<CardDraft> Cards(LibraryItem item, IEnumerable<string> questionIds)
    {
        var lesson = Build(item);
        if (lesson is null) return [];
        return questionIds.Distinct().Take(MaxCards)
            .Select(id => lesson.Questions.FirstOrDefault(q => q.Id == id && q.Kind == LessonQuestionKinds.Blank))
            .Where(question => question is not null)
            .Select(question => new CardDraft(question!.Prompt,
                $"{BlankTarget(question.Source.Quote, item.Title)}\n\n“{question.Source.Quote}”"))
            .ToArray();
    }

    private static string Expected(LessonQuestion question, LibraryItem item) =>
        question.Kind == LessonQuestionKinds.Blank
            ? BlankTarget(question.Source.Quote, item.Title)!
            : string.Join(", ", RubricTerms(question.Source.Quote, item.Title));

    private static List<LessonSource> Sentences(string text)
    {
        var result = new List<LessonSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var end = text[i] is '.' or '!' or '?' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]));
            var lineBreak = text[i] == '\n';
            if (!end && !lineBreak && i + 1 != text.Length) continue;
            var length = i - start + (end || i + 1 == text.Length && !lineBreak ? 1 : 0);
            var raw = text.Substring(start, Math.Max(0, length));
            var trimmed = raw.Trim().TrimStart('#', '*', '-', '>', ' ');
            var offset = start + raw.IndexOf(trimmed, StringComparison.Ordinal);
            start = i + 1;
            if (trimmed.Length is < MinSentence or > MaxSentence) continue;
            if (trimmed.Contains("http", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("](")) continue;
            if (!trimmed.Contains(' ') || !seen.Add(trimmed)) continue;
            result.Add(new LessonSource(trimmed, Math.Max(0, offset)));
        }
        return result;
    }

    private static string? BlankTarget(string sentence, string title)
    {
        var titleWords = Words(title).ToHashSet();
        return sentence.Split([' ', ',', ';', ':', '(', ')', '“', '”', '"', '.', '!', '?'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 5 && word.All(char.IsLetter))
            .Where(word => !Stop.Contains(word.ToLowerInvariant()) && !titleWords.Contains(word.ToLowerInvariant()))
            // The longest, then the earliest, so the blank is stable for a given sentence.
            .OrderByDescending(word => word.Length).ThenBy(word => sentence.IndexOf(word, StringComparison.Ordinal))
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> RubricTerms(string sentence, string title)
    {
        var titleWords = Words(title).ToHashSet();
        return sentence.Split([' ', ',', ';', ':', '(', ')', '“', '”', '"', '.', '!', '?'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 5 && word.All(char.IsLetter))
            .Where(word => !Stop.Contains(word.ToLowerInvariant()))
            .Select(word => word.ToLowerInvariant()).Distinct()
            .OrderByDescending(word => titleWords.Contains(word)).ThenByDescending(word => word.Length)
            .Take(4).ToArray();
    }

    private static string Replace(string sentence, string target)
    {
        // Every occurrence goes, so the answer is not given away by its own repeat.
        return sentence.Replace(target, "_____", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Words(string text) =>
        text.Split([' ', ',', ';', ':', '(', ')', '“', '”', '"', '.', '!', '?', '\n', '-'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToLowerInvariant()).Where(word => word.Length >= 4 && !Stop.Contains(word));

    private static string Fold(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c != '\'' && c != '’') builder.Append(c);
        }
        return builder.ToString();
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
