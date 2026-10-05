using System.Globalization;
using System.Text;
using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Finance;
using Jarvis.Domain.Finance;

namespace Jarvis.Api.Endpoints;

public sealed record BudgetRequest(string? Category, decimal? Limit, string? Currency);

public sealed record SubscriptionUpdateRequest(string? Status, int? RemindDaysBefore, bool? ClearReminder);

public sealed record ImportRequest(string? Csv, bool? Commit, string? Currency);

public sealed record NegotiateRequest(string? Goal, string? Mode, string? CancelUrl);

public sealed record CancelUrlRequest(string? CancelUrl);

public sealed record NegotiationDto(string Mode, string Merchant, Guid? TaskId, string? Prompt);

public sealed record SubscriptionDto(Guid Id, string Merchant, decimal Amount, string Currency, string Cadence,
    DateOnly LastChargedOn, DateOnly NextDueOn, int ChargeCount, decimal? PreviousAmount, string Status,
    int? RemindDaysBefore, decimal PerMonth, string? CancelUrl = null, Guid? NegotiationTaskId = null,
    string? NegotiationGoal = null, DateTimeOffset? NegotiationStartedAt = null);

public sealed record BudgetDto(Guid Id, string Category, decimal Limit, string Currency);

public sealed record FinanceOverviewDto(int Year, int Month, string Currency, decimal Total,
    IReadOnlyList<BudgetStatus> Budgets, SpendingForecast Forecast, IReadOnlyList<SubscriptionDto> Subscriptions,
    decimal SubscriptionsPerMonth, IReadOnlyList<FinanceAlert> Alerts);

/// <summary>
/// Budgets, recurring charges, the month forecast, bank-statement import and CSV export. Audit entries carry ids
/// and counts only, never merchants or amounts.
/// </summary>
internal static class FinanceEndpoints
{
    public static RouteGroupBuilder MapFinanceEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/finance");

        group.MapGet("/overview", async (IFinanceService finance, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var overview = await finance.OverviewAsync(currentUser.OwnerId, today, ct);
            return Results.Ok(new FinanceOverviewDto(overview.Year, overview.Month, overview.Currency, overview.Total,
                overview.Budgets, overview.Forecast, overview.Subscriptions.Select(ToDto).ToArray(),
                overview.SubscriptionsPerMonth, overview.Alerts));
        }).WithName("FinanceOverview");

        group.MapPut("/budgets", async (BudgetRequest request, IFinanceService finance, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await finance.SetBudgetAsync(currentUser.OwnerId, request.Category, request.Limit,
                request.Currency, ct);
            if (!result.Succeeded)
                return ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "Invalid budget.");
            await AuditAsync(audit, logger, currentUser, "budget.set", result.Value!.Id, ct);
            var budget = result.Value;
            return Results.Ok(new BudgetDto(budget.Id, budget.Category, budget.Limit, budget.Currency));
        }).WithName("SetBudget");

        group.MapDelete("/budgets/{id:guid}", async (Guid id, IFinanceService finance, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await finance.DeleteBudgetAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "budget.removed", id, ct);
            return Results.NoContent();
        }).WithName("DeleteBudget");

        group.MapPatch("/subscriptions/{id:guid}", async (Guid id, SubscriptionUpdateRequest request,
            IFinanceService finance, IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var result = await finance.UpdateSubscriptionAsync(id, currentUser.OwnerId, request.Status,
                request.RemindDaysBefore, request.ClearReminder == true, today, ct);
            if (result.Failure == FinanceFailure.NotFound) return Results.NotFound();
            if (!result.Succeeded)
                return ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "Invalid.");
            await AuditAsync(audit, logger, currentUser, "subscription.updated", id, ct);
            return Results.Ok(ToDto(result.Value!));
        }).WithName("UpdateSubscription");

        // draft starts a background task that writes the message; browser only returns the prompt, because the
        // browser tools run on the API host and its actions need approval in a chat.
        group.MapPost("/subscriptions/{id:guid}/negotiate", async (Guid id, NegotiateRequest request,
            ISubscriptionNegotiationService negotiation, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var result = await negotiation.StartAsync(currentUser.OwnerId, id, request.Goal, request.Mode,
                request.CancelUrl, ct);
            if (result.Failure == FinanceFailure.NotFound) return Results.NotFound();
            if (!result.Succeeded)
                return ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "Invalid.");
            await AuditAsync(audit, logger, currentUser, "subscription.negotiation_started", id, ct);
            var started = result.Value!;
            return Results.Ok(new NegotiationDto(started.Mode, started.Merchant, started.TaskId, started.Prompt));
        }).WithName("NegotiateSubscription");

        group.MapPut("/subscriptions/{id:guid}/cancel-url", async (Guid id, CancelUrlRequest request,
            ISubscriptionNegotiationService negotiation, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await negotiation.SetCancelUrlAsync(currentUser.OwnerId, id, request.CancelUrl, ct);
            if (result.Failure == FinanceFailure.NotFound) return Results.NotFound();
            if (!result.Succeeded)
                return ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "Invalid.");
            return Results.Ok(ToDto(result.Value!));
        }).WithName("SetSubscriptionCancelUrl");

        // The app reads the statement file and posts its text; commit=false previews without saving anything.
        group.MapPost("/import", async (ImportRequest request, IFinanceService finance, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Csv))
                return EndpointHelpers.Invalid("csv", "Choose a CSV file.");
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var report = await finance.ImportAsync(currentUser.OwnerId, request.Csv, request.Commit == true,
                request.Currency, today, ct);
            if (report.Committed)
                await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "finance",
                    "finance.imported", "moderate", true, null,
                    JsonSerializer.Serialize(new { imported = report.Imported, duplicates = report.Duplicates, source = "app" }),
                    ct);
            return Results.Ok(report);
        }).WithName("ImportBankStatement");

        group.MapGet("/export", async (string? from, string? to, string? category, IFinanceService finance,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            if (!TryDate(to, today, out var end)) return EndpointHelpers.Invalid("to", "Use the date format YYYY-MM-DD.");
            if (!TryDate(from, new DateOnly(end.Year, 1, 1), out var start))
                return EndpointHelpers.Invalid("from", "Use the date format YYYY-MM-DD.");
            var csv = await finance.ExportCsvAsync(currentUser.OwnerId, start, end, category, ct);
            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8",
                $"jarvis-expenses-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
        }).WithName("ExportExpenses");

        return api;
    }

    internal static SubscriptionDto ToDto(Subscription x) => new(x.Id, x.Merchant, x.Amount, x.Currency, x.Cadence,
        x.LastChargedOn, x.NextDueOn, x.ChargeCount, x.PreviousAmount, x.Status, x.RemindDaysBefore,
        Math.Round(SubscriptionCadences.PerMonth(x.Amount, x.Cadence), 2), x.CancelUrl, x.NegotiationTaskId,
        x.NegotiationGoal, x.NegotiationStartedAt);

    private static bool TryDate(string? text, DateOnly fallback, out DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            date = fallback;
            return true;
        }
        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out date);
    }

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid resourceId, CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "finance", action, "low", true, null,
            JsonSerializer.Serialize(new { resourceId, source = "app" }), ct);
}
