using Jarvis.Application.Conversations;
using Jarvis.Application.Usage;

namespace Jarvis.Api.Endpoints;

internal static class UsageEndpoints
{
    public static RouteGroupBuilder MapUsageEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/usage", async (string? period, IUsageDashboard dashboard, ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                if (!UsagePeriods.TryNormalize(period, out var normalized))
                    return EndpointHelpers.Invalid("period", "Period must be today, 7d, 30d, or all.");
                return Results.Ok(await dashboard.GetAsync(currentUser.OwnerId, normalized, cancellationToken));
            })
            .WithName("GetUsageDashboard");
        return api;
    }
}
