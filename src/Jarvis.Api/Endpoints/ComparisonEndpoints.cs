using Jarvis.Application.Comparisons;
using Jarvis.Application.Conversations;

namespace Jarvis.Api.Endpoints;

public sealed record ComparisonRequest(ComparisonDefinition? Definition, Guid? ProjectId = null);

public sealed record ComparisonCellRequest(string? Option, string? Criterion, ComparisonCell? Cell);

public sealed record DecideComparisonRequest(string? OptionId, double? Probability, DateOnly? ReviewOn);

public sealed record ComparisonExportDto(string Markdown, bool LeftOut);

public sealed record ComparisonDto(Guid Id, ComparisonDefinition Definition, ComparisonResult Result,
    Guid? ProjectId, Guid? LibraryItemId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// Comparisons: criteria with weights, options, and what is known about each, ranked by deterministic arithmetic. A
/// comparison can be saved to the Library and turned into a logged decision; it never buys or contacts anyone.
/// </summary>
internal static class ComparisonEndpoints
{
    public static RouteGroupBuilder MapComparisonEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/comparisons").WithTags("Comparisons");

        group.MapGet("", async (IComparisonService comparisons, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await comparisons.ListAsync(user.OwnerId, ct))).WithName("ListComparisons");

        group.MapPost("", async (ComparisonRequest request, IComparisonService comparisons, ICurrentUser user,
            CancellationToken ct) =>
        {
            if (request.Definition is null) return EndpointHelpers.Invalid("definition", "Describe the comparison.");
            var created = await comparisons.CreateAsync(user.OwnerId, request.Definition, request.ProjectId, ct);
            return created.Succeeded
                ? Results.Created($"/api/v1/comparisons/{created.Value!.Record.Id}", ToDto(created.Value))
                : Failure(created);
        }).WithName("CreateComparison");

        group.MapGet("/{id:guid}", async (Guid id, IComparisonService comparisons, ICurrentUser user,
            CancellationToken ct) =>
            await comparisons.GetAsync(id, user.OwnerId, ct) is { } view ? Results.Ok(ToDto(view)) : Results.NotFound())
            .WithName("GetComparison");

        group.MapPut("/{id:guid}", async (Guid id, ComparisonRequest request, IComparisonService comparisons,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (request.Definition is null) return EndpointHelpers.Invalid("definition", "Describe the comparison.");
            var updated = await comparisons.UpdateAsync(id, user.OwnerId, request.Definition, ct);
            return updated.Succeeded ? Results.Ok(ToDto(updated.Value!)) : Failure(updated);
        }).WithName("UpdateComparison");

        group.MapPut("/{id:guid}/cell", async (Guid id, ComparisonCellRequest request, IComparisonService comparisons,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Option) || string.IsNullOrWhiteSpace(request.Criterion) ||
                request.Cell is null)
                return EndpointHelpers.Invalid("cell", "Say which option, which criterion and the answer.");
            var updated = await comparisons.SetCellAsync(id, user.OwnerId, request.Option, request.Criterion,
                request.Cell, ct);
            return updated.Succeeded ? Results.Ok(ToDto(updated.Value!)) : Failure(updated);
        }).WithName("SetComparisonCell");

        group.MapDelete("/{id:guid}", async (Guid id, IComparisonService comparisons, ICurrentUser user,
            CancellationToken ct) =>
            await comparisons.DeleteAsync(id, user.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteComparison");

        group.MapGet("/{id:guid}/export", async (Guid id, IComparisonService comparisons, ICurrentUser user,
            CancellationToken ct) =>
        {
            var exported = await comparisons.ExportShareableAsync(id, user.OwnerId, ct);
            return exported.Succeeded
                ? Results.Ok(new ComparisonExportDto(exported.Value.Markdown, exported.Value.LeftOut))
                : Failure(exported);
        }).WithName("ExportComparison");

        group.MapPost("/{id:guid}/library", async (Guid id, IComparisonService comparisons, ICurrentUser user,
            CancellationToken ct) =>
        {
            var saved = await comparisons.SaveToLibraryAsync(id, user.OwnerId, ct);
            return saved.Succeeded ? Results.Ok(new { libraryItemId = saved.Value }) : Failure(saved);
        }).WithName("SaveComparisonToLibrary");

        group.MapPost("/{id:guid}/decide", async (Guid id, DecideComparisonRequest request,
            IComparisonService comparisons, ICurrentUser user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.OptionId)) return EndpointHelpers.Invalid("optionId", "Choose an option.");
            if (request.Probability is not { } probability)
                return EndpointHelpers.Invalid("probability", "Say how sure you are, from 1 to 99.");
            if (request.ReviewOn is not { } reviewOn)
                return EndpointHelpers.Invalid("reviewOn", "Pick the day to check how it went.");
            var decided = await comparisons.DecideAsync(id, user.OwnerId, request.OptionId, probability, reviewOn, ct);
            return decided.Succeeded
                ? Results.Ok(decided.Value)
                : Failure(decided);
        }).WithName("DecideComparison");

        return api;
    }

    private static ComparisonDto ToDto(ComparisonView view) => new(view.Record.Id, view.Record.Definition, view.Result,
        view.Record.ProjectId, view.Record.LibraryItemId, view.Record.CreatedAt, view.Record.UpdatedAt);

    private static IResult Failure<T>(ComparisonOperation<T> operation) => operation.Failure switch
    {
        ComparisonFailure.NotFound => Results.NotFound(),
        ComparisonFailure.Conflict => Results.Conflict(new { message = operation.Message }),
        _ => EndpointHelpers.Invalid(operation.Field ?? "definition", operation.Message ?? "The comparison is invalid.")
    };
}
