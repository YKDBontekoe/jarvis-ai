using Jarvis.Application.Decisions;
using Jarvis.Application.Library;

namespace Jarvis.Application.Comparisons;

public sealed class ComparisonService(IComparisonRepository repository, ILibraryService library,
    IDecisionService decisions, TimeProvider? timeProvider = null) : IComparisonService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<ComparisonSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(ownerId, cancellationToken)).Select(record =>
        {
            var result = ComparisonCalculator.Evaluate(record.Definition);
            var leader = result.Rows.FirstOrDefault(row => row.OptionId == result.LeaderId)?.Name;
            return new ComparisonSummary(record.Id, record.Definition.Title, record.Definition.Options.Count,
                record.Definition.Criteria.Count, leader, record.Definition.DecisionId is not null, record.UpdatedAt);
        }).ToArray();

    public async Task<ComparisonView?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await repository.GetAsync(id, ownerId, cancellationToken) is { } record ? View(record) : null;

    public async Task<ComparisonOperation<ComparisonView>> CreateAsync(Guid ownerId, ComparisonDefinition definition,
        Guid? projectId, CancellationToken cancellationToken)
    {
        var (normalized, field, message) = ComparisonValidator.Normalize(definition with
        {
            ChosenOptionId = null, DecisionId = null
        });
        if (normalized is null) return ComparisonOperation<ComparisonView>.Invalid(field!, message!);
        if (await repository.CountAsync(ownerId, cancellationToken) >= IComparisonService.MaxPerOwner)
            return ComparisonOperation<ComparisonView>.Conflict(
                $"You can keep up to {IComparisonService.MaxPerOwner} comparisons. Delete one first.");
        var now = clock.GetUtcNow();
        var record = new ComparisonRecord(Guid.CreateVersion7(), ownerId, normalized, projectId, null, now, now);
        await repository.AddAsync(record, cancellationToken);
        return ComparisonOperation<ComparisonView>.Ok(View(record));
    }

    public async Task<ComparisonOperation<ComparisonView>> UpdateAsync(Guid id, Guid ownerId,
        ComparisonDefinition definition, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return ComparisonOperation<ComparisonView>.NotFound();
        // The decision link belongs to the server: an edit cannot forge or drop it.
        var (normalized, field, message) = ComparisonValidator.Normalize(definition with
        {
            ChosenOptionId = current.Definition.ChosenOptionId, DecisionId = current.Definition.DecisionId
        });
        if (normalized is null) return ComparisonOperation<ComparisonView>.Invalid(field!, message!);
        var updated = current with { Definition = normalized, UpdatedAt = clock.GetUtcNow() };
        await repository.UpdateAsync(updated, cancellationToken);
        return ComparisonOperation<ComparisonView>.Ok(View(updated));
    }

    public async Task<ComparisonOperation<ComparisonView>> SetCellAsync(Guid id, Guid ownerId, string option,
        string criterion, ComparisonCell cell, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return ComparisonOperation<ComparisonView>.NotFound();
        var definition = current.Definition;
        var target = definition.Options.FirstOrDefault(item => Matches(item.Id, item.Name, option));
        if (target is null) return ComparisonOperation<ComparisonView>.Invalid("option", "That option is not in this comparison.");
        var column = definition.Criteria.FirstOrDefault(item => Matches(item.Id, item.Name, criterion));
        if (column is null) return ComparisonOperation<ComparisonView>.Invalid("criterion", "That criterion is not in this comparison.");
        var cells = new Dictionary<string, ComparisonCell>(target.Cells) { [column.Id] = cell };
        var edited = definition with
        {
            Options = definition.Options.Select(item => item.Id == target.Id ? item with { Cells = cells } : item).ToArray()
        };
        return await UpdateAsync(id, ownerId, edited, cancellationToken);
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<ComparisonOperation<(string Markdown, bool LeftOut)>> ExportShareableAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        return current is null
            ? ComparisonOperation<(string Markdown, bool LeftOut)>.NotFound()
            : ComparisonOperation<(string Markdown, bool LeftOut)>.Ok(ComparisonCalculator.ToShareableMarkdown(current.Definition));
    }

    public async Task<ComparisonOperation<Guid>> SaveToLibraryAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return ComparisonOperation<Guid>.NotFound();
        var markdown = ComparisonCalculator.ToMarkdown(current.Definition, ComparisonCalculator.Evaluate(current.Definition));
        var saved = await library.AddNoteAsync(ownerId,
            new NoteDraft(current.Definition.Title, markdown, LibraryKinds.Report, null, ["comparison"],
                LibraryOrigins.App, current.ProjectId), cancellationToken);
        if (!saved.Succeeded)
            return ComparisonOperation<Guid>.Invalid(saved.Field ?? "library", saved.Message ?? "Could not save to the Library.");
        await repository.UpdateAsync(current with { LibraryItemId = saved.Value!.Id, UpdatedAt = clock.GetUtcNow() },
            cancellationToken);
        return ComparisonOperation<Guid>.Ok(saved.Value.Id);
    }

    public async Task<ComparisonOperation<ComparisonDecision>> DecideAsync(Guid id, Guid ownerId, string optionId,
        double probability, DateOnly reviewOn, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return ComparisonOperation<ComparisonDecision>.NotFound();
        var option = current.Definition.Options.FirstOrDefault(item => item.Id == optionId);
        if (option is null) return ComparisonOperation<ComparisonDecision>.Invalid("optionId", "That option is not in this comparison.");
        if (current.Definition.DecisionId is { } existing)
        {
            // Choosing the same option again is the same decision; changing your mind is a new decision the owner
            // makes deliberately after resolving or deleting the first.
            return current.Definition.ChosenOptionId == option.Id
                ? ComparisonOperation<ComparisonDecision>.Ok(new ComparisonDecision(existing, option.Id, option.Name, false))
                : ComparisonOperation<ComparisonDecision>.Conflict(
                    "A different option was already chosen. Resolve or delete that decision first.");
        }

        var result = ComparisonCalculator.Evaluate(current.Definition);
        var row = result.Rows.Single(item => item.OptionId == option.Id);
        var context = $"From the comparison “{current.Definition.Title}”. " +
                      (row.Score is { } score ? $"It scored {score:0.#}/10 on what was known" : "It had no score") +
                      $" ({row.Coverage * 100:0}% of what you care about was known)." +
                      (row.UnknownCriteria.Count > 0 ? $" Still unknown: {string.Join(", ", row.UnknownCriteria)}." : "");
        try
        {
            var created = await decisions.CreateAsync(ownerId, new CreateDecisionRequest(
                ComparisonRules.Clean($"Choose {option.Name}: {current.Definition.Title}", DecisionRules.MaxTitleLength)!,
                ComparisonRules.Clean($"{option.Name} turns out to be the right choice.", DecisionRules.MaxPredictionLength)!,
                probability, reviewOn, context), cancellationToken);
            await repository.UpdateAsync(current with
            {
                Definition = current.Definition with { ChosenOptionId = option.Id, DecisionId = created.Id },
                UpdatedAt = clock.GetUtcNow()
            }, cancellationToken);
            return ComparisonOperation<ComparisonDecision>.Ok(new ComparisonDecision(created.Id, option.Id, option.Name, true));
        }
        catch (ArgumentException exception)
        {
            return ComparisonOperation<ComparisonDecision>.Invalid(exception.ParamName ?? "reviewOn", exception.Message);
        }
    }

    private static bool Matches(string id, string name, string wanted) =>
        string.Equals(id, wanted.Trim(), StringComparison.Ordinal) ||
        string.Equals(name, wanted.Trim(), StringComparison.OrdinalIgnoreCase);

    private static ComparisonView View(ComparisonRecord record) =>
        new(record, ComparisonCalculator.Evaluate(record.Definition));
}
