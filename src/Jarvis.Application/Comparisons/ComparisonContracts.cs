using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Comparisons;

public static class ComparisonRules
{
    public const int MaxCriteria = 12;
    public const int MaxOptions = 8;
    public const int MaxTitleLength = 120;
    public const int MaxQuestionLength = 500;
    public const int MaxNameLength = 80;
    public const int MaxSummaryLength = 300;
    public const int MaxNoteLength = 300;
    public const int MaxSourceLength = 300;
    public const int MaxWeight = 10;
    public const double MaxScore = 10;

    /// <summary>Scores within this of each other count as a tie: false precision is not a ranking.</summary>
    public const double TieMargin = 0.05;

    /// <summary>An option with less than this share of the weight filled in is flagged as thin.</summary>
    public const double ThinCoverage = 0.5;

    public static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9_-]{0,31}$", RegexOptions.Compiled);

    public static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }
}

/// <summary>
/// What matters to the owner and how much, 0 (ignore) to 10. A private criterion (a budget, say) is used for the
/// owner's own ranking and left out of anything shareable.
/// </summary>
public sealed record ComparisonCriterion(string Id, string Name, int Weight, bool Private = false);

/// <summary>
/// One option's answer to one criterion. A null <see cref="Score"/> means unknown: it is never counted as zero.
/// <see cref="AsOf"/> dates the observation, because a price or availability is only true on the day it was seen.
/// </summary>
public sealed record ComparisonCell(double? Score, string? Note = null, string? Source = null, DateOnly? AsOf = null);

public sealed record ComparisonOption(string Id, string Name, string? Summary,
    IReadOnlyDictionary<string, ComparisonCell> Cells);

public sealed record ComparisonDefinition(
    string Title,
    string? Question,
    IReadOnlyList<ComparisonCriterion> Criteria,
    IReadOnlyList<ComparisonOption> Options,
    string? ChosenOptionId = null,
    Guid? DecisionId = null);

/// <summary>
/// An option's weighted average over the criteria that are known for it, so a missing fact lowers confidence
/// (<see cref="Coverage"/>) instead of silently scoring zero. <see cref="Score"/> is null when nothing is known.
/// </summary>
public sealed record ComparisonRow(
    string OptionId,
    string Name,
    double? Score,
    double Coverage,
    IReadOnlyList<string> UnknownCriteria,
    int Rank,
    bool Tied,
    bool Thin);

/// <summary>Whether taking one criterion out would change which option leads.</summary>
public sealed record ComparisonSensitivity(string CriterionId, string CriterionName, bool ChangesLeader,
    string? LeaderWithout);

public sealed record ComparisonResult(
    IReadOnlyList<ComparisonRow> Rows,
    string? LeaderId,
    bool LeaderIsTied,
    IReadOnlyList<ComparisonSensitivity> Sensitivity,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Scores a comparison. Pure and deterministic: the same definition always gives the same ranking, with ties broken
/// by name, so a saved comparison reproduces its result.
/// </summary>
public static class ComparisonCalculator
{
    public static ComparisonResult Evaluate(ComparisonDefinition definition)
    {
        var rows = Score(definition.Criteria, definition.Options);
        var leader = rows.FirstOrDefault(row => row.Score is not null);
        var warnings = new List<string>();
        if (definition.Criteria.Count == 0) warnings.Add("Add at least one criterion to compare against.");
        else if (definition.Criteria.All(criterion => criterion.Weight == 0))
            warnings.Add("Every criterion has weight 0, so nothing can be ranked.");
        if (rows.Any(row => row.Thin))
            warnings.Add("Some options are missing most of what you care about, so their score is a rough guide.");
        if (rows.Count(row => row.Rank == 1 && row.Score is not null) > 1)
            warnings.Add("The leading options are effectively tied. Look at what separates them, not the number.");

        var sensitivity = new List<ComparisonSensitivity>();
        foreach (var criterion in definition.Criteria.Where(criterion => criterion.Weight > 0))
        {
            var without = Score(definition.Criteria.Where(other => other.Id != criterion.Id).ToArray(),
                definition.Options);
            var newLeader = without.FirstOrDefault(row => row.Score is not null);
            var changes = leader is not null && newLeader is not null && newLeader.OptionId != leader.OptionId &&
                          !(newLeader.Rank == 1 && leader.Rank == 1 && without.Single(row => row.OptionId == leader.OptionId).Rank == 1);
            sensitivity.Add(new ComparisonSensitivity(criterion.Id, criterion.Name, changes,
                changes ? newLeader!.Name : null));
        }

        return new ComparisonResult(rows, leader?.OptionId, leader is { Tied: true }, sensitivity, warnings);
    }

    private static IReadOnlyList<ComparisonRow> Score(IReadOnlyList<ComparisonCriterion> criteria,
        IReadOnlyList<ComparisonOption> options)
    {
        var totalWeight = criteria.Sum(criterion => criterion.Weight);
        var scored = new List<(ComparisonOption Option, double? Score, double Coverage, List<string> Unknown)>();
        foreach (var option in options)
        {
            double weighted = 0;
            var knownWeight = 0;
            var unknown = new List<string>();
            foreach (var criterion in criteria)
            {
                if (option.Cells.TryGetValue(criterion.Id, out var cell) && cell.Score is { } score)
                {
                    weighted += criterion.Weight * score;
                    knownWeight += criterion.Weight;
                }
                else unknown.Add(criterion.Name);
            }

            double? value = knownWeight == 0 ? null : Math.Round(weighted / knownWeight, 2);
            var coverage = totalWeight == 0 ? 0 : Math.Round((double)knownWeight / totalWeight, 2);
            scored.Add((option, value, coverage, unknown));
        }

        var ordered = scored
            .OrderByDescending(item => item.Score.HasValue)
            .ThenByDescending(item => item.Score ?? 0)
            .ThenBy(item => item.Option.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Option.Id, StringComparer.Ordinal)
            .ToList();

        var rows = new List<ComparisonRow>(ordered.Count);
        double? rankScore = null;
        var rank = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            // A new rank starts when the score drops by more than the tie margin from the one that opened the rank.
            if (item.Score is not null && (rankScore is null || rankScore - item.Score > ComparisonRules.TieMargin))
            {
                rank = index + 1;
                rankScore = item.Score;
            }
            var tied = item.Score is not null && ordered.Count(other => other.Score is not null &&
                                                                         Math.Abs(other.Score.Value - item.Score.Value) <= ComparisonRules.TieMargin) > 1;
            rows.Add(new ComparisonRow(item.Option.Id, item.Option.Name, item.Score, item.Coverage, item.Unknown,
                item.Score is null ? 0 : rank, tied, item.Score is not null && item.Coverage < ComparisonRules.ThinCoverage));
        }
        return rows;
    }

    /// <summary>
    /// A copy the owner can share: private criteria and everything said about them are left out, and the ranking is
    /// recomputed from what remains so no hidden criterion shapes the result. Says so, without naming what was kept back.
    /// </summary>
    public static (string Markdown, bool LeftOut) ToShareableMarkdown(ComparisonDefinition definition)
    {
        var kept = definition.Criteria.Where(criterion => !criterion.Private).ToArray();
        var leftOut = kept.Length != definition.Criteria.Count;
        if (kept.Length == 0)
            return ("# " + definition.Title + "\n\nEvery criterion is private, so there is nothing to share.\n", leftOut);
        var ids = kept.Select(criterion => criterion.Id).ToHashSet();
        var shared = definition with
        {
            Criteria = kept,
            Options = definition.Options.Select(option => option with
            {
                Cells = option.Cells.Where(cell => ids.Contains(cell.Key)).ToDictionary(cell => cell.Key, cell => cell.Value)
            }).ToArray(),
            ChosenOptionId = null,
            DecisionId = null
        };
        var markdown = ToMarkdown(shared, Evaluate(shared));
        return (leftOut
            ? markdown.TrimEnd() + "\n\n_Some criteria were left out of this copy, and the ranking above was worked out without them._\n"
            : markdown, leftOut);
    }

    /// <summary>The comparison as Markdown for the Library: assumptions, unknowns and dated sources stay visible.</summary>
    public static string ToMarkdown(ComparisonDefinition definition, ComparisonResult result)
    {
        var text = new StringBuilder();
        text.Append("# ").AppendLine(definition.Title).AppendLine();
        if (!string.IsNullOrWhiteSpace(definition.Question)) text.AppendLine(definition.Question).AppendLine();
        text.AppendLine("## Ranking").AppendLine();
        text.AppendLine("| Rank | Option | Score (0–10) | Known |").AppendLine("|---|---|---|---|");
        foreach (var row in result.Rows)
            text.Append("| ").Append(row.Rank == 0 ? "–" : row.Rank.ToString(CultureInfo.InvariantCulture))
                .Append(row.Tied ? " (tied)" : "").Append(" | ").Append(Cell(row.Name)).Append(" | ")
                .Append(row.Score is { } score ? score.ToString("0.0#", CultureInfo.InvariantCulture) : "unknown")
                .Append(" | ").Append((row.Coverage * 100).ToString("0", CultureInfo.InvariantCulture)).AppendLine("% |");
        text.AppendLine().AppendLine("Scores are the weighted average of what is known. An unknown is left out, never counted as zero.");
        text.AppendLine().AppendLine("## Criteria and weights").AppendLine();
        foreach (var criterion in definition.Criteria)
            text.Append("- ").Append(criterion.Name).Append(": weight ").Append(criterion.Weight).AppendLine();
        text.AppendLine().AppendLine("## Evidence").AppendLine();
        foreach (var option in definition.Options)
        {
            text.Append("### ").AppendLine(option.Name);
            if (!string.IsNullOrWhiteSpace(option.Summary)) text.AppendLine(option.Summary);
            foreach (var criterion in definition.Criteria)
            {
                option.Cells.TryGetValue(criterion.Id, out var cell);
                text.Append("- ").Append(criterion.Name).Append(": ");
                if (cell?.Score is not { } value) text.Append("unknown");
                else text.Append(value.ToString("0.#", CultureInfo.InvariantCulture)).Append("/10");
                if (!string.IsNullOrWhiteSpace(cell?.Note)) text.Append(" — ").Append(cell.Note);
                if (!string.IsNullOrWhiteSpace(cell?.Source)) text.Append(" (source: ").Append(cell.Source).Append(')');
                if (cell?.AsOf is { } asOf) text.Append(" (as of ").Append(asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')');
                text.AppendLine();
            }
            text.AppendLine();
        }
        var flips = result.Sensitivity.Where(item => item.ChangesLeader).ToArray();
        if (flips.Length > 0)
        {
            text.AppendLine("## What could change the answer").AppendLine();
            foreach (var flip in flips)
                text.Append("- Without “").Append(flip.CriterionName).Append("” the leader would be ").Append(flip.LeaderWithout).AppendLine(".");
        }
        foreach (var warning in result.Warnings) text.Append("- Note: ").AppendLine(warning);
        return text.ToString().TrimEnd();
    }

    private static string Cell(string value) => value.Replace("|", "/");
}

/// <summary>Checks a definition's shape and limits; the owner's text is trimmed, never trusted.</summary>
public static class ComparisonValidator
{
    public static (ComparisonDefinition? Definition, string? Field, string? Message) Normalize(
        ComparisonDefinition input)
    {
        var title = ComparisonRules.Clean(input.Title, ComparisonRules.MaxTitleLength);
        if (title is null) return (null, "title", "Give the comparison a title.");
        if (input.Criteria.Count > ComparisonRules.MaxCriteria)
            return (null, "criteria", $"Use at most {ComparisonRules.MaxCriteria} criteria.");
        if (input.Options.Count > ComparisonRules.MaxOptions)
            return (null, "options", $"Compare at most {ComparisonRules.MaxOptions} options.");

        var criteria = new List<ComparisonCriterion>();
        foreach (var criterion in input.Criteria)
        {
            var name = ComparisonRules.Clean(criterion.Name, ComparisonRules.MaxNameLength);
            if (name is null) return (null, "criteria", "Every criterion needs a name.");
            if (!ComparisonRules.IdPattern.IsMatch(criterion.Id))
                return (null, "criteria", "A criterion id uses lower-case letters, digits, - and _.");
            if (criteria.Any(other => other.Id == criterion.Id))
                return (null, "criteria", "Two criteria share an id.");
            if (criterion.Weight is < 0 or > ComparisonRules.MaxWeight)
                return (null, "criteria", $"A weight is 0 to {ComparisonRules.MaxWeight}.");
            criteria.Add(criterion with { Name = name });
        }

        var options = new List<ComparisonOption>();
        foreach (var option in input.Options)
        {
            var name = ComparisonRules.Clean(option.Name, ComparisonRules.MaxNameLength);
            if (name is null) return (null, "options", "Every option needs a name.");
            if (!ComparisonRules.IdPattern.IsMatch(option.Id))
                return (null, "options", "An option id uses lower-case letters, digits, - and _.");
            if (options.Any(other => other.Id == option.Id)) return (null, "options", "Two options share an id.");
            var cells = new Dictionary<string, ComparisonCell>();
            foreach (var (criterionId, cell) in option.Cells)
            {
                if (criteria.All(criterion => criterion.Id != criterionId)) continue;
                if (cell.Score is { } score && (double.IsNaN(score) || score < 0 || score > ComparisonRules.MaxScore))
                    return (null, "cells", $"A score is 0 to {ComparisonRules.MaxScore:0}.");
                cells[criterionId] = new ComparisonCell(cell.Score is { } value ? Math.Round(value, 2) : null,
                    ComparisonRules.Clean(cell.Note, ComparisonRules.MaxNoteLength),
                    ComparisonRules.Clean(cell.Source, ComparisonRules.MaxSourceLength), cell.AsOf);
            }
            options.Add(new ComparisonOption(option.Id, name,
                ComparisonRules.Clean(option.Summary, ComparisonRules.MaxSummaryLength), cells));
        }

        var chosen = input.ChosenOptionId is { } id && options.Any(option => option.Id == id) ? id : null;
        return (new ComparisonDefinition(title, ComparisonRules.Clean(input.Question, ComparisonRules.MaxQuestionLength),
            criteria, options, chosen, input.DecisionId), null, null);
    }
}

/// <summary>A saved comparison: its definition, where it was saved in the Library and when it was last changed.</summary>
public sealed record ComparisonRecord(
    Guid Id,
    Guid OwnerId,
    ComparisonDefinition Definition,
    Guid? ProjectId,
    Guid? LibraryItemId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ComparisonView(ComparisonRecord Record, ComparisonResult Result);

public sealed record ComparisonSummary(
    Guid Id,
    string Title,
    int Options,
    int Criteria,
    string? Leader,
    bool Decided,
    DateTimeOffset UpdatedAt);

public enum ComparisonFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

public sealed record ComparisonOperation<T>(T? Value, ComparisonFailure Failure = ComparisonFailure.None,
    string? Field = null, string? Message = null)
{
    public bool Succeeded => Failure == ComparisonFailure.None;
    public static ComparisonOperation<T> Ok(T value) => new(value);
    public static ComparisonOperation<T> NotFound() => new(default, ComparisonFailure.NotFound);
    public static ComparisonOperation<T> Invalid(string field, string message) =>
        new(default, ComparisonFailure.Invalid, field, message);
    public static ComparisonOperation<T> Conflict(string message) =>
        new(default, ComparisonFailure.Conflict, null, message);
}

public sealed record ComparisonDecision(Guid DecisionId, string OptionId, string OptionName, bool Created);

public interface IComparisonRepository
{
    Task<IReadOnlyList<ComparisonRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ComparisonRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(ComparisonRecord record, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(ComparisonRecord record, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IComparisonService
{
    public const int MaxPerOwner = 100;

    Task<IReadOnlyList<ComparisonSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ComparisonView?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<ComparisonOperation<ComparisonView>> CreateAsync(Guid ownerId, ComparisonDefinition definition,
        Guid? projectId, CancellationToken cancellationToken);

    /// <summary>Replaces the definition. Editing a weight or a cell recomputes the ranking; nothing else changes.</summary>
    Task<ComparisonOperation<ComparisonView>> UpdateAsync(Guid id, Guid ownerId, ComparisonDefinition definition,
        CancellationToken cancellationToken);

    /// <summary>Sets or clears one option's answer to one criterion, found by id or name (for conversations).</summary>
    Task<ComparisonOperation<ComparisonView>> SetCellAsync(Guid id, Guid ownerId, string option, string criterion,
        ComparisonCell cell, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Saves the comparison as a Library report. Saving again updates nothing: it makes a new report.</summary>
    Task<ComparisonOperation<Guid>> SaveToLibraryAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>A copy without the private criteria, ranked again without them. Nothing is saved or sent.</summary>
    Task<ComparisonOperation<(string Markdown, bool LeftOut)>> ExportShareableAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the owner's choice as a logged decision with a review date. Nothing is bought or sent; choosing twice
    /// for the same option returns the same decision.
    /// </summary>
    Task<ComparisonOperation<ComparisonDecision>> DecideAsync(Guid id, Guid ownerId, string optionId,
        double probability, DateOnly reviewOn, CancellationToken cancellationToken);
}
