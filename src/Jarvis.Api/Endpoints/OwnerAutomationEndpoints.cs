using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Domain.Automations;

namespace Jarvis.Api.Endpoints;

internal static class OwnerAutomationEndpoints
{
    public static RouteGroupBuilder MapOwnerAutomationEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/automations").WithTags("Automations");

        group.MapGet("", async (IAutomationRuleService automations, ICurrentUser user, CancellationToken ct) =>
                Results.Ok((await automations.ListAsync(user.OwnerId, ct)).Select(ToDto)))
            .WithName("ListAutomations");

        group.MapGet("/{id:guid}", async (Guid id, IAutomationRuleService automations, ICurrentUser user,
            CancellationToken ct) =>
        {
            var rule = await automations.GetAsync(id, user.OwnerId, ct);
            return rule is null ? Results.NotFound() : Results.Ok(ToDto(rule));
        }).WithName("GetAutomation");

        group.MapPost("", async (SaveAutomationRuleRequest request, IAutomationRuleService automations,
            ICurrentUser user, CancellationToken ct) =>
        {
            try
            {
                var rule = await automations.CreateAsync(user.OwnerId, request, ct);
                return Results.Created($"/api/v1/automations/{rule.Id}", ToDto(rule));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("definition", exception.Message);
            }
        }).WithName("CreateAutomation");

        group.MapPut("/{id:guid}", async (Guid id, SaveAutomationRuleRequest request,
            IAutomationRuleService automations, ICurrentUser user, CancellationToken ct) =>
        {
            try
            {
                var rule = await automations.UpdateAsync(id, user.OwnerId, request, ct);
                return rule is null ? Results.NotFound() : Results.Ok(ToDto(rule));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("definition", exception.Message);
            }
        }).WithName("UpdateAutomation");

        group.MapPost("/validate", async (SaveAutomationRuleRequest request, IAutomationRuleService automations,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await automations.ValidateAsync(request.ParseDefinition()));
            }
            catch (JsonException exception)
            {
                return EndpointHelpers.Invalid("definition", exception.Message);
            }
        })
            .WithName("ValidateAutomation");

        group.MapPost("/{id:guid}/enable", async (Guid id, IAutomationRuleService automations, ICurrentUser user,
            CancellationToken ct) =>
        {
            try
            {
                var rule = await automations.EnableAsync(id, user.OwnerId, ct);
                return rule is null ? Results.NotFound() : Results.Ok(ToDto(rule));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not enable automation {AutomationId}.", id);
                return Results.Problem("Automation service is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("EnableAutomation");

        group.MapPost("/{id:guid}/disable", async (Guid id, IAutomationRuleService automations, ICurrentUser user,
            CancellationToken ct) =>
        {
            var rule = await automations.DisableAsync(id, user.OwnerId, ct);
            return rule is null ? Results.NotFound() : Results.Ok(ToDto(rule));
        }).WithName("DisableAutomation");

        group.MapPost("/{id:guid}/test-run", async (Guid id, IAutomationRuleService automations, ICurrentUser user,
            CancellationToken ct) =>
        {
            try
            {
                var run = await automations.TestRunAsync(id, user.OwnerId, ct);
                return run is null ? Results.NotFound() : Results.Ok(ToRunDto(run));
            }
            catch (InvalidOperationException exception)
            {
                return EndpointHelpers.Invalid("run", exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not test automation {AutomationId}.", id);
                return Results.Problem("Automation service is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("TestAutomation");

        group.MapPost("/{id:guid}/run", async (Guid id, IAutomationRuleService automations, ICurrentUser user,
            CancellationToken ct) =>
        {
            try
            {
                var run = await automations.ManualRunAsync(id, user.OwnerId, ct);
                return run is null ? Results.NotFound() : Results.Ok(ToRunDto(run));
            }
            catch (InvalidOperationException exception)
            {
                return EndpointHelpers.Invalid("run", exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not run automation {AutomationId}.", id);
                return Results.Problem("Automation service is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("RunAutomation");

        group.MapGet("/{id:guid}/runs", async (Guid id, IAutomationRunRepository runs, ICurrentUser user,
            CancellationToken ct) =>
        {
            var history = await runs.ListForRuleAsync(id, user.OwnerId, 100, ct);
            return Results.Ok(history.Select(ToRunDto));
        }).WithName("ListAutomationRuns");

        group.MapPost("/runs/{runId:guid}/approve", async (Guid runId, IAutomationRunRepository runs,
            IAutomationScheduler scheduler, ICurrentUser user, CancellationToken ct) =>
        {
            var run = await runs.GetAsync(runId, user.OwnerId, ct);
            if (run is null) return Results.NotFound();
            if (run.Status != AutomationRunStatuses.WaitingApproval) return Results.Conflict();
            await scheduler.SignalApprovalResolvedAsync(run.WorkflowId, true, ct);
            return Results.Accepted();
        }).WithName("ApproveAutomationRun");

        group.MapPost("/runs/{runId:guid}/decline", async (Guid runId, IAutomationRunRepository runs,
            IAutomationScheduler scheduler, ICurrentUser user, CancellationToken ct) =>
        {
            var run = await runs.GetAsync(runId, user.OwnerId, ct);
            if (run is null) return Results.NotFound();
            if (run.Status != AutomationRunStatuses.WaitingApproval) return Results.Conflict();
            await scheduler.SignalApprovalResolvedAsync(run.WorkflowId, false, ct);
            await runs.FailAsync(runId, "Declined by owner.", "[]", ct);
            return Results.NoContent();
        }).WithName("DeclineAutomationRun");

        return api;
    }

    private static object ToDto(AutomationRuleRecord rule) => new
    {
        rule.Id,
        rule.Name,
        rule.SchemaVersion,
        definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson),
        rule.Status,
        rule.LastRunAt,
        rule.NextRunAt,
        rule.CooldownUntil,
        rule.CreatedAt,
        rule.UpdatedAt,
        rule.ConversationId
    };

    private static object ToRunDto(AutomationRunRecord run) => new
    {
        run.Id,
        run.RuleId,
        run.TriggerKind,
        run.TriggerReason,
        run.TestRun,
        run.Status,
        run.ActionResultsJson,
        run.FailureSummary,
        run.StartedAt,
        run.CompletedAt
    };
}
