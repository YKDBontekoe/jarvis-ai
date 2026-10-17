using System.Reflection;
using Jarvis.Agents;
using Jarvis.Application.Conversations;
using Jarvis.Application.Setup;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// Guided setup in product words and an honest capability status. Reading looks at what exists and sends or creates
/// nothing; the only thing stored is which optional steps the owner chose to skip.
/// </summary>
internal static class SetupEndpoints
{
    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/setup").WithTags("Setup");

        group.MapGet("", async (ISetupService setup, CodexSignIn signIn, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await ViewAsync(setup, signIn, user, ct))).WithName("GetSetup");

        group.MapPost("/goals/{goalId}/decline", async (string goalId, ISetupService setup, CodexSignIn signIn,
            ICurrentUser user, CancellationToken ct) =>
            await ChangedAsync(await setup.DeclineAsync(user.OwnerId, goalId, ct), setup, signIn, user, ct))
            .WithName("DeclineSetupGoal");

        group.MapPost("/goals/{goalId}/reopen", async (string goalId, ISetupService setup, CodexSignIn signIn,
            ICurrentUser user, CancellationToken ct) =>
            await ChangedAsync(await setup.ReopenAsync(user.OwnerId, goalId, ct), setup, signIn, user, ct))
            .WithName("ReopenSetupGoal");

        // Reviewable before anyone shares it: versions and on/off states only.
        group.MapGet("/support-bundle", async (ISetupService setup, CodexSignIn signIn, TimeProvider clock,
            ICurrentUser user, CancellationToken ct) =>
            Results.Ok(SetupRules.Bundle(await ViewAsync(setup, signIn, user, ct),
                typeof(SetupEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion.Split('+')[0] ?? "unknown", clock.GetUtcNow()))).WithName("GetSupportBundle");

        return api;
    }

    private static async Task<SetupView> ViewAsync(ISetupService setup, CodexSignIn signIn, ICurrentUser user,
        CancellationToken ct)
    {
        var signedIn = false;
        string? problem = null;
        try
        {
            var status = await signIn.GetStatusAsync(ct);
            signedIn = status.SignedIn;
            problem = status.Error is null ? null : "sign-in";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            problem = "unavailable";
        }
        return await setup.GetAsync(user.OwnerId, signedIn, problem, ct);
    }

    private static async Task<IResult> ChangedAsync(SetupOperation change, ISetupService setup, CodexSignIn signIn,
        ICurrentUser user, CancellationToken ct) =>
        change.Succeeded
            ? Results.Ok(await ViewAsync(setup, signIn, user, ct))
            : EndpointHelpers.Invalid(change.Field ?? "goalId", change.Message ?? "The request is invalid.");
}
