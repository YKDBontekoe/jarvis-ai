using System.Globalization;
using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Expenses;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Expenses;

namespace Jarvis.Api.Endpoints;

public sealed record ExpenseRequest(decimal? Amount, string? Currency, string? Merchant, string? Category,
    string? Note, DateOnly? SpentOn, Guid? ReceiptFileId, string? Kind = null, Guid? AccountId = null,
    Guid? TransferAccountId = null);

public sealed record ExpenseDto(Guid Id, decimal Amount, string Currency, string? Merchant, string Category,
    string? Note, DateOnly SpentOn, Guid? ReceiptFileId, string Source, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string Kind = "expense", Guid? AccountId = null, Guid? TransferAccountId = null);

public sealed record TransactionPageDto(IReadOnlyList<ExpenseDto> Items, bool HasMore);

public sealed record ExpenseMonthDto(int Year, int Month, string Currency, decimal Total, int Count,
    decimal PreviousTotal, IReadOnlyList<CategoryTotal> Categories, IReadOnlyList<DayTotal> Days,
    IReadOnlyList<MerchantTotal> TopMerchants, IReadOnlyList<CurrencyTotal> OtherCurrencies,
    IReadOnlyList<ExpenseDto> Expenses);

public sealed record ReceiptScanRequest(Guid FileId);

public sealed record ReceiptScanDto(Guid FileId, decimal? Amount, string? Currency, string? Merchant,
    string? Category, DateOnly? SpentOn, string? Note);

/// <summary>
/// The owner's expense log and monthly overview. Audit entries carry the expense id only, never amounts, shops,
/// or notes.
/// </summary>
internal static class ExpenseEndpoints
{
    public static RouteGroupBuilder MapExpenseEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/expenses");

        group.MapGet("", async (string? month, string? category, IExpenseService expenses,
            IDailyBriefingRepository briefings, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var (year, monthNumber) = await ResolveMonthAsync(month, briefings, currentUser, ct) ?? (0, 0);
            if (year == 0) return EndpointHelpers.Invalid("month", "Use the month format YYYY-MM.");
            if (category is not null && !ExpenseCategories.IsValid(category))
                return EndpointHelpers.Invalid("category", "Unknown category.");
            var summary = await expenses.SummarizeMonthAsync(currentUser.OwnerId, year, monthNumber, ct);
            var list = await expenses.ListMonthAsync(currentUser.OwnerId, year, monthNumber, category, ct);
            return Results.Ok(new ExpenseMonthDto(summary.Year, summary.Month, summary.Currency, summary.Total,
                summary.Count, summary.PreviousTotal, summary.Categories, summary.Days, summary.TopMerchants,
                summary.OtherCurrencies, list.Select(ToDto).ToArray()));
        }).WithName("ListExpenses");

        // The ledger across every kind and account, paged with offset and limit.
        api.MapGet("/transactions", async (string? from, string? to, Guid? accountId, string? kind, string? category,
            string? q, int? offset, int? limit, IExpenseService expenses, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            DateOnly? start = null, end = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                    return EndpointHelpers.Invalid("from", "Use the date format YYYY-MM-DD.");
                start = f;
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                if (!DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    return EndpointHelpers.Invalid("to", "Use the date format YYYY-MM-DD.");
                end = t;
            }
            if (kind is not null && !TransactionKinds.IsValid(kind))
                return EndpointHelpers.Invalid("kind", "Use expense, income, or transfer.");
            var size = Math.Clamp(limit ?? 50, 1, 100);
            var page = await expenses.QueryAsync(currentUser.OwnerId,
                new TransactionQuery(start, end, accountId, kind, category, q, offset ?? 0, size + 1), ct);
            return Results.Ok(new TransactionPageDto(page.Take(size).Select(ToDto).ToArray(), page.Count > size));
        }).WithName("ListTransactions");

        group.MapGet("/{id:guid}", async (Guid id, IExpenseService expenses, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var expense = await expenses.GetAsync(id, currentUser.OwnerId, ct);
            return expense is null ? Results.NotFound() : Results.Ok(ToDto(expense));
        }).WithName("GetExpense");

        group.MapPost("", async (ExpenseRequest request, IExpenseService expenses,
            IDailyBriefingRepository briefings, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var today = await TodayAsync(briefings, currentUser, ct);
            var source = request.ReceiptFileId is null ? ExpenseSources.App : ExpenseSources.Receipt;
            var result = await expenses.CreateAsync(currentUser.OwnerId, ToDraft(request, source), today,
                skipDuplicates: false, ct);
            if (Failed(result) is { } failure) return failure;
            var expense = result.Value!.Expense;
            await AuditAsync(audit, logger, currentUser, "expense.logged", expense.Id, ct);
            return Results.Created($"/api/v1/expenses/{expense.Id}", ToDto(expense));
        }).WithName("CreateExpense");

        group.MapPut("/{id:guid}", async (Guid id, ExpenseRequest request, IExpenseService expenses,
            IDailyBriefingRepository briefings, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            // The app sends the whole expense; empty merchant and note clear them.
            var draft = ToDraft(request, ExpenseSources.App) with
            {
                Merchant = request.Merchant ?? "",
                Note = request.Note ?? ""
            };
            var result = await expenses.UpdateAsync(id, currentUser.OwnerId, draft,
                await TodayAsync(briefings, currentUser, ct), ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "expense.updated", id, ct);
            return Results.Ok(ToDto(result.Value!));
        }).WithName("UpdateExpense");

        group.MapDelete("/{id:guid}", async (Guid id, IExpenseService expenses, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await expenses.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "expense.deleted", id, ct, "moderate");
            return Results.NoContent();
        }).WithName("DeleteExpense");

        // Reads a receipt photo the owner already uploaded through /files and returns a draft to review.
        // Nothing is saved until the app posts the reviewed expense.
        group.MapPost("/scan", async (ReceiptScanRequest request, IReceiptReader reader, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await reader.ReadAsync(currentUser.OwnerId, request.FileId, ct);
            return result.Failure switch
            {
                ReceiptReadFailure.None => Results.Ok(new ReceiptScanDto(request.FileId, result.Reading!.Amount,
                    result.Reading.Currency, result.Reading.Merchant,
                    result.Reading.Category ?? (result.Reading.Merchant is null && result.Reading.Note is null
                        ? null
                        : ExpenseCategories.Guess(result.Reading.Merchant, result.Reading.Note)),
                    result.Reading.SpentOn, result.Reading.Note)),
                ReceiptReadFailure.NotFound => Results.NotFound(),
                ReceiptReadFailure.NotAnImage => EndpointHelpers.Invalid("fileId",
                    "Use a JPEG, PNG, or WebP photo of at most 8 MB."),
                _ => ApiProblemResults.Validation("fileId",
                    "Jarvis could not read this receipt. Try a sharper photo or fill it in yourself.")
            };
        }).WithName("ScanReceipt");

        return api;
    }

    internal static ExpenseDto ToDto(Expense expense) => new(expense.Id, expense.Amount, expense.Currency,
        expense.Merchant, expense.Category, expense.Note, expense.SpentOn, expense.ReceiptFileId, expense.Source,
        expense.CreatedAt, expense.UpdatedAt, expense.Kind, expense.AccountId, expense.TransferAccountId);

    private static ExpenseDraft ToDraft(ExpenseRequest request, string source) => new(request.Amount,
        request.Currency, request.Merchant, request.Category, request.Note, request.SpentOn, request.ReceiptFileId,
        source, request.Kind, request.AccountId, request.TransferAccountId);

    private static async Task<(int Year, int Month)?> ResolveMonthAsync(string? month,
        IDailyBriefingRepository briefings, ICurrentUser currentUser, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            var today = await TodayAsync(briefings, currentUser, ct);
            return (today.Year, today.Month);
        }
        return DateOnly.TryParseExact(month.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) && parsed.Year is >= 2000 and <= 2100
            ? (parsed.Year, parsed.Month)
            : null;
    }

    private static IResult? Failed<T>(ExpenseOperation<T> result) => result.Failure switch
    {
        ExpenseFailure.None => null,
        ExpenseFailure.NotFound => Results.NotFound(),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    /// <summary>"Today" in the owner's configured time zone, falling back to UTC.</summary>
    private static async Task<DateOnly> TodayAsync(IDailyBriefingRepository briefings, ICurrentUser currentUser,
        CancellationToken ct)
    {
        var zoneId = (await briefings.GetAsync(currentUser.OwnerId, ct))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(), zone).DateTime);
    }

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid expenseId, CancellationToken ct, string risk = "low") =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "expenses", action, risk, true, null,
            JsonSerializer.Serialize(new { resourceId = expenseId, source = "app" }), ct);
}
