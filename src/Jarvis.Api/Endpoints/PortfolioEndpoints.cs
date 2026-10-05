using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Finance;
using Jarvis.Domain.Finance;

namespace Jarvis.Api.Endpoints;

public sealed record HoldingRequest(string? Symbol, string? Name, string? AssetType, string? Currency, Guid? AccountId);

public sealed record TradeRequest(string? Kind, DateOnly? TradedOn, decimal? Quantity, decimal? Price, decimal? Fees,
    string? Note);

public sealed record PriceRequest(decimal? Price);

public sealed record HoldingDto(Guid Id, Guid? AccountId, string Symbol, string Name, string AssetType,
    string Currency, decimal? LastPrice, DateTimeOffset? LastPriceAt, string? PriceSource, decimal Quantity,
    decimal CostBasis, decimal AverageCost, decimal RealizedProfit, decimal Dividends, decimal Fees,
    decimal? MarketValue, decimal? UnrealizedProfit, decimal? UnrealizedPercent, bool PriceIsStale);

public sealed record TradeDto(Guid Id, Guid HoldingId, string Kind, DateOnly TradedOn, decimal Quantity,
    decimal Price, decimal Fees, string? Note);

public sealed record PortfolioDto(string Currency, decimal TotalValue, decimal TotalCost, decimal UnrealizedProfit,
    decimal RealizedProfit, decimal Dividends, IReadOnlyList<HoldingDto> Holdings,
    IReadOnlyList<AllocationSlice> Allocation, IReadOnlyList<CurrencyValue> OtherCurrencies, bool QuotesConfigured);

/// <summary>
/// Holdings, trades and prices. Audit entries carry ids only, never symbols, quantities or prices.
/// </summary>
internal static class PortfolioEndpoints
{
    public static RouteGroupBuilder MapPortfolioEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/finance/portfolio");

        group.MapGet("", async (IPortfolioService portfolio, IFinanceService finance, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            // Quotes are a bonus: a slow or failing provider never blocks showing the portfolio.
            await portfolio.RefreshPricesAsync(currentUser.OwnerId, today, false, ct);
            var summary = await portfolio.SummaryAsync(currentUser.OwnerId, today, ct);
            return Results.Ok(new PortfolioDto(summary.Currency, summary.TotalValue, summary.TotalCost,
                summary.UnrealizedProfit, summary.RealizedProfit, summary.Dividends,
                summary.Holdings.Select(ToDto).ToArray(), summary.Allocation, summary.OtherCurrencies,
                summary.QuotesConfigured));
        }).WithName("GetPortfolio");

        group.MapPost("/refresh", async (IPortfolioService portfolio, IFinanceService finance,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var changed = await portfolio.RefreshPricesAsync(currentUser.OwnerId, today, true, ct);
            return Results.Ok(new { updated = changed });
        }).WithName("RefreshPortfolioPrices");

        group.MapPost("/holdings", async (HoldingRequest request, IPortfolioService portfolio, IFinanceService finance,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await portfolio.AddHoldingAsync(currentUser.OwnerId,
                new HoldingDraft(request.Symbol, request.Name, request.AssetType, request.Currency, request.AccountId), ct);
            if (!result.Succeeded) return Problem(result);
            await AuditAsync(audit, logger, currentUser, "holding.added", result.Value!.Id, ct);
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var summary = await portfolio.GetHoldingAsync(result.Value.Id, currentUser.OwnerId, today, ct);
            return Results.Created($"/api/v1/finance/portfolio/holdings/{result.Value.Id}", ToDto(summary!));
        }).WithName("AddHolding");

        group.MapGet("/holdings/{id:guid}", async (Guid id, IPortfolioService portfolio, IFinanceService finance,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var summary = await portfolio.GetHoldingAsync(id, currentUser.OwnerId, today, ct);
            return summary is null ? Results.NotFound() : Results.Ok(ToDto(summary));
        }).WithName("GetHolding");

        group.MapPut("/holdings/{id:guid}", async (Guid id, HoldingRequest request, IPortfolioService portfolio,
            IFinanceService finance, IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await portfolio.UpdateHoldingAsync(id, currentUser.OwnerId,
                new HoldingDraft(request.Symbol, request.Name, request.AssetType, request.Currency, request.AccountId), ct);
            if (!result.Succeeded) return Problem(result);
            await AuditAsync(audit, logger, currentUser, "holding.updated", id, ct);
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            return Results.Ok(ToDto((await portfolio.GetHoldingAsync(id, currentUser.OwnerId, today, ct))!));
        }).WithName("UpdateHolding");

        group.MapDelete("/holdings/{id:guid}", async (Guid id, IPortfolioService portfolio, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await portfolio.DeleteHoldingAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "holding.deleted", id, ct, "moderate");
            return Results.NoContent();
        }).WithName("DeleteHolding");

        group.MapPut("/holdings/{id:guid}/price", async (Guid id, PriceRequest request, IPortfolioService portfolio,
            IFinanceService finance, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var result = await portfolio.SetPriceAsync(id, currentUser.OwnerId, request.Price, today, ct);
            if (!result.Succeeded) return Problem(result);
            return Results.Ok(ToDto((await portfolio.GetHoldingAsync(id, currentUser.OwnerId, today, ct))!));
        }).WithName("SetHoldingPrice");

        group.MapGet("/holdings/{id:guid}/history", async (Guid id, int? days, IPortfolioService portfolio,
            ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await portfolio.HistoryAsync(id, currentUser.OwnerId, days ?? 180, ct)))
            .WithName("HoldingPriceHistory");

        group.MapGet("/holdings/{id:guid}/trades", async (Guid id, IPortfolioService portfolio,
            ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok((await portfolio.ListTradesAsync(currentUser.OwnerId, id, ct)).Select(ToDto).ToArray()))
            .WithName("ListTrades");

        group.MapPost("/holdings/{id:guid}/trades", async (Guid id, TradeRequest request, IPortfolioService portfolio,
            IFinanceService finance, IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var today = await finance.TodayAsync(currentUser.OwnerId, ct);
            var result = await portfolio.AddTradeAsync(id, currentUser.OwnerId,
                new TradeDraft(request.Kind, request.TradedOn, request.Quantity, request.Price, request.Fees,
                    request.Note), today, ct);
            if (!result.Succeeded) return Problem(result);
            await AuditAsync(audit, logger, currentUser, "trade.recorded", result.Value!.Id, ct);
            return Results.Created($"/api/v1/finance/portfolio/holdings/{id}/trades", ToDto(result.Value));
        }).WithName("AddTrade");

        group.MapDelete("/trades/{id:guid}", async (Guid id, IPortfolioService portfolio, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await portfolio.DeleteTradeAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "trade.deleted", id, ct, "moderate");
            return Results.NoContent();
        }).WithName("DeleteTrade");

        return api;
    }

    internal static HoldingDto ToDto(HoldingSummary x) => new(x.Holding.Id, x.Holding.AccountId, x.Holding.Symbol,
        x.Holding.Name, x.Holding.AssetType, x.Holding.Currency, x.Holding.LastPrice, x.Holding.LastPriceAt,
        x.Holding.PriceSource, x.Position.Quantity, x.Position.CostBasis, x.Position.AverageCost,
        x.Position.RealizedProfit, x.Position.Dividends, x.Position.Fees, x.MarketValue, x.UnrealizedProfit,
        x.UnrealizedPercent, x.PriceIsStale);

    internal static TradeDto ToDto(InvestmentTrade x) => new(x.Id, x.HoldingId, x.Kind, x.TradedOn, x.Quantity,
        x.Price, x.Fees, x.Note);

    private static IResult Problem<T>(FinanceOperation<T> result) => result.Failure == FinanceFailure.NotFound
        ? Results.NotFound()
        : ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.");

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid id, CancellationToken ct, string risk = "low") =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "finance", action, risk, true, null,
            JsonSerializer.Serialize(new { resourceId = id, source = "app" }), ct);
}
