using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Finance;
using Jarvis.Domain.Finance;

namespace Jarvis.Api.Endpoints;

public sealed record AccountRequest(string? Name, string? Type, string? Currency, string? Institution, string? Last4,
    decimal? OpeningBalance, DateOnly? OpeningOn, bool? Archived);

public sealed record ReconcileRequest(decimal? Balance);

public sealed record AccountImportRequest(string? Csv, bool? Commit);

public sealed record AccountDto(Guid Id, string Name, string Type, string Currency, string? Institution,
    string? Last4, decimal OpeningBalance, DateOnly OpeningOn, bool Archived, decimal Balance, decimal MonthIn,
    decimal MonthOut, int TransactionCount);

public sealed record ReconcileDto(AccountDto Account, decimal Balance, decimal Adjustment);

/// <summary>
/// The owner's bank accounts, their balances, reconciliation and per-account statement import, plus the combined
/// net worth overview. Audit entries carry ids and counts only, never names, balances or amounts.
/// </summary>
internal static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/finance");

        group.MapGet("/wealth", async (IWealthService wealth, IFinanceService finance, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            return Results.Ok(await wealth.OverviewAsync(currentUser.OwnerId, today, ct));
        }).WithName("FinanceWealth");

        group.MapGet("/accounts", async (bool? archived, IAccountService accounts, IFinanceService finance,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            return Results.Ok((await accounts.ListAsync(currentUser.OwnerId, today, archived == true, ct))
                .Select(ToDto).ToArray());
        }).WithName("ListAccounts");

        group.MapGet("/accounts/{id:guid}", async (Guid id, IAccountService accounts, IFinanceService finance,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var summary = await accounts.GetAsync(id, currentUser.OwnerId, today, ct);
            return summary is null ? Results.NotFound() : Results.Ok(ToDto(summary));
        }).WithName("GetAccount");

        group.MapPost("/accounts", async (AccountRequest request, IAccountService accounts, IFinanceService finance,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var result = await accounts.CreateAsync(currentUser.OwnerId, ToDraft(request), today, ct);
            if (!result.Succeeded) return Problem(result);
            await AuditAsync(audit, logger, currentUser, "account.created", result.Value!.Id, ct);
            var summary = await accounts.GetAsync(result.Value.Id, currentUser.OwnerId, today, ct);
            return Results.Created($"/api/v1/finance/accounts/{result.Value.Id}", ToDto(summary!));
        }).WithName("CreateAccount");

        group.MapPut("/accounts/{id:guid}", async (Guid id, AccountRequest request, IAccountService accounts,
            IFinanceService finance, IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await accounts.UpdateAsync(id, currentUser.OwnerId, ToDraft(request), ct);
            if (!result.Succeeded) return Problem(result);
            await AuditAsync(audit, logger, currentUser, "account.updated", id, ct);
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            return Results.Ok(ToDto((await accounts.GetAsync(id, currentUser.OwnerId, today, ct))!));
        }).WithName("UpdateAccount");

        group.MapDelete("/accounts/{id:guid}", async (Guid id, IAccountService accounts, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await accounts.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "account.deleted", id, ct, "moderate");
            return Results.NoContent();
        }).WithName("DeleteAccount");

        group.MapPost("/accounts/{id:guid}/reconcile", async (Guid id, ReconcileRequest request,
            IAccountService accounts, IFinanceService finance, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var result = await accounts.ReconcileAsync(id, currentUser.OwnerId, request.Balance, today, ct);
            if (!result.Succeeded) return Problem(result);
            await AuditAsync(audit, logger, currentUser, "account.reconciled", id, ct);
            var summary = await accounts.GetAsync(id, currentUser.OwnerId, today, ct);
            return Results.Ok(new ReconcileDto(ToDto(summary!), result.Value!.Balance, result.Value.Adjustment));
        }).WithName("ReconcileAccount");

        // commit=false previews without saving anything.
        group.MapPost("/accounts/{id:guid}/import", async (Guid id, AccountImportRequest request,
            IAccountService accounts, IFinanceService finance, IAuditEventStore audit, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Csv)) return EndpointHelpers.Invalid("csv", "Choose a CSV file.");
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var result = await accounts.ImportAsync(id, currentUser.OwnerId, request.Csv, request.Commit == true,
                today, ct);
            if (!result.Succeeded) return Problem(result);
            var report = result.Value!;
            if (report.Committed)
                await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "finance",
                    "account.imported", "moderate", true, null,
                    JsonSerializer.Serialize(new { resourceId = id, imported = report.Imported,
                        duplicates = report.Duplicates, source = "app" }), ct);
            return Results.Ok(report);
        }).WithName("ImportAccountStatement");

        return api;
    }

    internal static AccountDto ToDto(AccountSummary x) => new(x.Account.Id, x.Account.Name, x.Account.Type,
        x.Account.Currency, x.Account.Institution, x.Account.Last4, x.Account.OpeningBalance, x.Account.OpeningOn,
        x.Account.Archived, x.Balance, x.MonthIn, x.MonthOut, x.TransactionCount);

    private static AccountDraft ToDraft(AccountRequest r) => new(r.Name, r.Type, r.Currency, r.Institution, r.Last4,
        r.OpeningBalance, r.OpeningOn, r.Archived);

    private static IResult Problem<T>(FinanceOperation<T> result) => result.Failure == FinanceFailure.NotFound
        ? Results.NotFound()
        : ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.");

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid id, CancellationToken ct, string risk = "low") =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "finance", action, risk, true, null,
            JsonSerializer.Serialize(new { resourceId = id, source = "app" }), ct);
}
