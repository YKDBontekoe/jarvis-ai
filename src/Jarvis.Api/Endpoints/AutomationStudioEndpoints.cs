using System.Text;
using System.Text.Json;
using Jarvis.Api.Security;
using Jarvis.Application.Audit;
using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Endpoints;

public sealed record SampleEventRequest(string? Kind, string? Title, string? Detail, string? Source);

public sealed record SimulateRequest(JsonElement Definition, SampleEventRequest? Sample);

public sealed record WebhookCreateRequest(string? Name);

public sealed record AutomationTemplateDto(string Id, string Title, string Description, string Category,
    string Trigger, int Actions, bool NeedsApproval);

public sealed record WebhookDto(Guid Id, string Name, string TokenHint, int UseCount, DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt);

public sealed record WebhookCreatedDto(WebhookDto Webhook, string Token, string Url);

/// <summary>
/// The automation studio: templates, what-would-happen simulation, event kinds, and webhooks that start
/// event-triggered automations. The public webhook endpoint answers 404 for any unknown token.
/// </summary>
internal static class AutomationStudioEndpoints
{
    public static RouteGroupBuilder MapAutomationStudioEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/automations").WithTags("Automation studio");

        group.MapGet("/event-kinds", () => Results.Ok(AutomationEventKinds.All.Select(kind =>
            new { kind, description = AutomationEventKinds.Describe(kind) }))).WithName("ListAutomationEventKinds");

        group.MapGet("/templates", () => Results.Ok(AutomationTemplates.All.Select(ToDto)))
            .WithName("ListAutomationTemplates");

        group.MapPost("/templates/{templateId}/create", async (string templateId, IAutomationRuleService automations,
            IDailyBriefingRepository briefings, ICurrentUser user, CancellationToken ct) =>
        {
            var template = AutomationTemplates.Find(templateId);
            if (template is null) return Results.NotFound();
            var zoneId = (await briefings.GetAsync(user.OwnerId, ct))?.TimeZoneId;
            var definition = template.Build(LocalClock.TryFind(zoneId, out var zone) ? zone.Id : "UTC");
            var json = JsonDocument.Parse(AutomationDefinitionJson.Serialize(definition)).RootElement.Clone();
            var rule = await automations.CreateAsync(user.OwnerId, new SaveAutomationRuleRequest(template.Title, json), ct);
            return Results.Created($"/api/v1/automations/{rule.Id}", OwnerAutomationEndpoints.ToDto(rule));
        }).WithName("CreateAutomationFromTemplate");

        // Nothing is saved, sent or started: the answer lists what each action would do for the sample event.
        group.MapPost("/simulate", (SimulateRequest request, TimeProvider clock) =>
        {
            try
            {
                var definition = AutomationDefinitionJson.Deserialize(request.Definition.GetRawText());
                AutomationRuleValidator.Validate(definition);
                return Results.Ok(AutomationSimulator.Simulate(definition, ToEvent(request.Sample, definition),
                    clock.GetUtcNow()));
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                return EndpointHelpers.Invalid("definition", exception.Message);
            }
        }).WithName("SimulateAutomation");

        group.MapPost("/{id:guid}/simulate", async (Guid id, SampleEventRequest? sample,
            IAutomationRuleService automations, TimeProvider clock, ICurrentUser user, CancellationToken ct) =>
        {
            var rule = await automations.GetAsync(id, user.OwnerId, ct);
            if (rule is null) return Results.NotFound();
            var definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson);
            return Results.Ok(AutomationSimulator.Simulate(definition, ToEvent(sample, definition), clock.GetUtcNow()));
        }).WithName("SimulateSavedAutomation");

        var hooks = group.MapGroup("/webhooks");

        hooks.MapGet("", async (IAutomationWebhookService webhooks, ICurrentUser user, CancellationToken ct) =>
            Results.Ok((await webhooks.ListAsync(user.OwnerId, ct)).Select(ToDto))).WithName("ListAutomationWebhooks");

        hooks.MapPost("", async (WebhookCreateRequest request, HttpContext http, IAutomationWebhookService webhooks,
            IAuditEventStore audit, ICurrentUser user, CancellationToken ct) =>
        {
            var (created, error) = await webhooks.CreateAsync(user.OwnerId, request.Name, ct);
            if (created is null) return EndpointHelpers.Invalid("name", error ?? "Could not create the webhook.");
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, user.OwnerId, "automations", "webhook.created",
                "moderate", true, null, JsonSerializer.Serialize(new { resourceId = created.Webhook.Id }), ct);
            var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            return Results.Created($"/api/v1/automations/webhooks/{created.Webhook.Id}",
                new WebhookCreatedDto(ToDto(created.Webhook), created.Token, $"{baseUrl}/api/v1/hooks/{created.Token}"));
        }).WithName("CreateAutomationWebhook");

        hooks.MapDelete("/{id:guid}", async (Guid id, IAutomationWebhookService webhooks, IAuditEventStore audit,
            ICurrentUser user, CancellationToken ct) =>
        {
            if (!await webhooks.DeleteAsync(id, user.OwnerId, ct)) return Results.NotFound();
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, user.OwnerId, "automations", "webhook.deleted",
                "moderate", true, null, JsonSerializer.Serialize(new { resourceId = id }), ct);
            return Results.NoContent();
        }).WithName("DeleteAutomationWebhook");

        // Public: the long random token in the path is the credential. The body is capped and read as text.
        api.MapPost("/hooks/{token}", async (string token, HttpRequest request, IAutomationWebhookService webhooks,
            CancellationToken ct) =>
        {
            if (request.ContentLength > AutomationEventLimits.MaxWebhookBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var buffer = new char[AutomationEventLimits.MaxWebhookBodyBytes + 1];
            var read = await reader.ReadBlockAsync(buffer, ct);
            if (read > AutomationEventLimits.MaxWebhookBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var started = await webhooks.IngestAsync(token, new string(buffer, 0, read), ct);
            return started is null ? Results.NotFound() : Results.Accepted(value: new { started });
        }).WithName("CallAutomationWebhook").AllowAnonymous().DisableAntiforgery()
            .RequireRateLimiting(ApiRateLimiting.PublicPolicy);

        return api;
    }

    private static AutomationEvent? ToEvent(SampleEventRequest? sample, AutomationRuleDefinition definition)
    {
        if (sample is null || string.IsNullOrWhiteSpace(sample.Title)) return null;
        var kind = AutomationEventKinds.IsValid(sample.Kind)
            ? sample.Kind
            : (definition.Trigger as EventTriggerDefinition)?.EventKind ?? AutomationEventKinds.Webhook;
        return new AutomationEvent(kind, sample.Title, sample.Detail, sample.Source, null, DateTimeOffset.UtcNow)
            .Normalize();
    }

    private static AutomationTemplateDto ToDto(AutomationTemplate template)
    {
        var definition = template.Build("UTC");
        return new AutomationTemplateDto(template.Id, template.Title, template.Description, template.Category,
            AutomationSimulator.DescribeTrigger(definition.Trigger), definition.Actions.Count,
            definition.Actions.Any(AutomationActionPolicy.RequiresApproval));
    }

    private static WebhookDto ToDto(AutomationWebhookRecord x) =>
        new(x.Id, x.Name, x.TokenHint, x.UseCount, x.LastUsedAt, x.CreatedAt);
}
