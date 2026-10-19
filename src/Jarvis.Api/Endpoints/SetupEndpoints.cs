using System.Reflection;
using Jarvis.Agents;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
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

        group.MapGet("", async (ISetupService setup, CodexSignIn signIn, ICurrentUser user, IServiceProvider services,
            IConfiguration configuration, CancellationToken ct) =>
            Results.Ok(await ViewAsync(setup, signIn, user, ct, await EnvironmentAsync(services, configuration, ct))))
            .WithName("GetSetup");

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
            ICurrentUser user, IServiceProvider services, IConfiguration configuration, CancellationToken ct) =>
            Results.Ok(SetupRules.Bundle(await ViewAsync(setup, signIn, user, ct, await EnvironmentAsync(services, configuration, ct)),
                typeof(SetupEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion.Split('+')[0] ?? "unknown", clock.GetUtcNow()))).WithName("GetSupportBundle");

        return api;
    }

    /// <summary>
    /// Harmless checks only: the scanner is shown a few harmless bytes (it is local and nothing is kept), and storage is
    /// asked for a key that does not exist. Neither creates anything an owner would see.
    /// </summary>
    private static async Task<SetupEnvironment> EnvironmentAsync(IServiceProvider services, IConfiguration configuration,
        CancellationToken ct)
    {
        var pushConfigured = !string.IsNullOrWhiteSpace(configuration["Push:GoogleServiceAccountFile"]) &&
                             !string.IsNullOrWhiteSpace(configuration["Push:FirebaseProjectId"]);
        return new SetupEnvironment(pushConfigured,
            await ProbeAsync(async token =>
            {
                if (services.GetService<IFileMalwareScanner>() is not { } scanner) return (bool?)null;
                await scanner.ScanAsync(new MemoryStream("jarvis setup check"u8.ToArray()), token);
                return true;
            }, ct),
            await ProbeAsync(async token =>
            {
                if (services.GetService<IObjectStorage>() is not { } storage) return (bool?)null;
                await using var found = await storage.GetAsync("setup-check/does-not-exist", token) as IAsyncDisposable;
                return true;
            }, ct));
    }

    private static async Task<bool?> ProbeAsync(Func<CancellationToken, Task<bool?>> probe, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            return await probe(timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<SetupView> ViewAsync(ISetupService setup, CodexSignIn signIn, ICurrentUser user,
        CancellationToken ct, SetupEnvironment? environment = null)
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
        return await setup.GetAsync(user.OwnerId, signedIn, problem, ct, environment);
    }

    private static async Task<IResult> ChangedAsync(SetupOperation change, ISetupService setup, CodexSignIn signIn,
        ICurrentUser user, CancellationToken ct) =>
        change.Succeeded
            ? Results.Ok(await ViewAsync(setup, signIn, user, ct))
            : EndpointHelpers.Invalid(change.Field ?? "goalId", change.Message ?? "The request is invalid.");
}
