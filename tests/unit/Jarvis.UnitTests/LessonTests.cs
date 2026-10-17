using Jarvis.Application.Library;
using Jarvis.Domain.Library;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class LessonTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000ff01");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private const string Article =
        "Sourdough starter is a living mixture of flour and water that holds wild yeast and bacteria. " +
        "Feeding the starter every day keeps the yeast active and the dough rising reliably. " +
        "A warm kitchen speeds up fermentation, while a cold fridge slows fermentation down to a crawl. " +
        "Bakers judge readiness by bubbles on the surface and a pleasant sour smell. " +
        "Short.\n" +
        "Visit https://example.com/bread for more on baking and similar recipes today. " +
        "Discarding half of the starter before each feeding prevents it from growing too large.";

    private static LibraryItem Item(string content = Article, string title = "Sourdough basics") =>
        new(Guid.NewGuid(), Owner, "web", null, title, "How a sourdough starter works.", [], [], content, "app", null,
            Now, Now);

    [Fact]
    public void A_lesson_quotes_sentences_from_the_text_with_their_position_and_nothing_else()
    {
        var item = Item();

        var lesson = LessonBuilder.Build(item)!;

        Assert.Equal(item.Id, lesson.ItemId);
        Assert.InRange(lesson.Points.Count, 3, LessonBuilder.MaxPoints);
        foreach (var point in lesson.Points)
        {
            Assert.Equal(point.Text, point.Source.Quote);
            Assert.Equal(point.Source.Quote, item.Content.Substring(point.Source.Offset, point.Source.Quote.Length));
        }
        Assert.DoesNotContain(lesson.Points, p => p.Text.Contains("http"));
        Assert.DoesNotContain(lesson.Points, p => p.Text == "Short.");
        Assert.Contains("Nothing here is invented", lesson.Note);
        Assert.InRange(lesson.Minutes, 3, 7);
    }

    [Fact]
    public void The_lesson_has_three_blank_questions_and_one_explain_back_prompt()
    {
        var lesson = LessonBuilder.Build(Item())!;

        Assert.Equal(3, lesson.Questions.Count(q => q.Kind == LessonQuestionKinds.Blank));
        var explain = Assert.Single(lesson.Questions, q => q.Kind == LessonQuestionKinds.ExplainBack);
        Assert.Contains("Sourdough basics", explain.Prompt);
        foreach (var blank in lesson.Questions.Where(q => q.Kind == LessonQuestionKinds.Blank))
        {
            Assert.Contains("_____", blank.Prompt);
            Assert.DoesNotContain(LessonBuilder.AnswerFor(Item(), blank.Id) ?? "?", blank.Prompt.Replace("_____", ""));
        }
    }

    [Fact]
    public void The_same_text_always_gives_the_same_lesson_and_answer_key()
    {
        var item = Item();

        var first = LessonBuilder.Build(item)!;
        var second = LessonBuilder.Build(item)!;

        Assert.Equal(first.Points.Select(p => p.Text), second.Points.Select(p => p.Text));
        Assert.Equal(first.Questions.Select(q => q.Prompt), second.Questions.Select(q => q.Prompt));
        Assert.Equal(LessonBuilder.AnswerFor(item, "b1"), LessonBuilder.AnswerFor(item, "b1"));
    }

    [Fact]
    public void Text_that_is_too_thin_to_teach_from_gives_no_lesson_instead_of_an_invented_one()
    {
        Assert.Null(LessonBuilder.Build(Item("Too short to learn from.")));
        Assert.Null(LessonBuilder.Build(Item(string.Join(' ', Enumerable.Repeat("tiny", 80)))));
    }

    private static string Answer(LibraryItem item, string id) => LessonBuilder.AnswerFor(item, id)!;

    [Fact]
    public void A_right_answer_matches_ignoring_case_and_accents_and_a_typo_is_close()
    {
        var item = Item();
        var answer = Answer(item, "b1");

        Assert.Equal(LessonVerdicts.Correct, LessonBuilder.Check(item, "b1", answer.ToUpperInvariant())!.Verdict);
        var typo = answer[..^1] + (answer[^1] == 'x' ? 'y' : 'x');
        Assert.Equal(LessonVerdicts.Close, LessonBuilder.Check(item, "b1", typo)!.Verdict);
        var wrong = LessonBuilder.Check(item, "b1", "pineapple")!;
        Assert.Equal(LessonVerdicts.Incorrect, wrong.Verdict);
        Assert.Equal(answer, wrong.Expected);
        Assert.Contains("correct this grade", wrong.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("I don't know")]
    [InlineData("weet niet")]
    [InlineData("?")]
    public void Not_knowing_is_welcome_and_shows_the_source_instead_of_a_wrong_mark(string answer)
    {
        var item = Item();

        var grade = LessonBuilder.Check(item, "b1", answer)!;

        Assert.Equal(LessonVerdicts.Unsure, grade.Verdict);
        Assert.Contains("what your text says", grade.Message);
        Assert.False(string.IsNullOrEmpty(grade.Source.Quote));
    }

    [Fact]
    public void Explain_it_back_is_checked_against_terms_from_the_source_and_never_called_a_judgement_of_understanding()
    {
        var item = Item();
        var lesson = LessonBuilder.Build(item)!;
        var terms = Answer(item, "e1").Split(", ");

        var good = LessonBuilder.Check(item, "e1", $"It is about {terms[0]} and {terms[1]} working together.")!;
        var thin = LessonBuilder.Check(item, "e1", "Something about bread.")!;

        Assert.Equal(LessonVerdicts.Correct, good.Verdict);
        Assert.NotEqual(LessonVerdicts.Correct, thin.Verdict);
        Assert.Contains("not a judgement of what you understand", thin.Message);
        Assert.Contains(lesson.Questions, q => q.Id == "e1");
    }

    [Fact]
    public void A_claim_the_text_does_not_make_is_never_graded_correct()
    {
        var item = Item();

        var grade = LessonBuilder.Check(item, "b2", "refrigerator")!;

        Assert.NotEqual(LessonVerdicts.Correct, grade.Verdict);
        Assert.Null(LessonBuilder.Check(item, "zz", "anything"));
    }

    [Fact]
    public void Cards_are_a_small_reviewed_set_of_blank_questions_with_the_source_on_the_back()
    {
        var item = Item();

        var cards = LessonBuilder.Cards(item, ["b1", "b3", "b1", "e1", "nope"]);

        Assert.Equal(2, cards.Count); // duplicates, the explain prompt and unknown ids are left out
        Assert.All(cards, card =>
        {
            Assert.Contains("_____", card.Front);
            Assert.Contains("“", card.Back);
        });
        Assert.Empty(LessonBuilder.Cards(Item("short"), ["b1"]));
        Assert.True(LessonBuilder.Cards(item, Enumerable.Range(0, 20).Select(i => $"b{i}")).Count <= LessonBuilder.MaxCards);
    }
}
