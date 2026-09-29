using System.ComponentModel;
using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Domain.Automations;

namespace Jarvis.Agents;

internal sealed class AutomationAgentTools(IAutomationRuleService automations, ICurrentUser currentUser)
{
    [Description("List the owner's automation rules with status, trigger kind, and last run time.")]
    public async Task<string> ListAutomationsAsync(CancellationToken cancellationToken = default)
    {
        var rules = await automations.ListAsync(currentUser.OwnerId, cancellationToken);
        if (rules.Count == 0) return "No automations are configured.";
        return string.Join("\n", rules.Select(rule =>
        {
            var trigger = AutomationDefinitionJson.Deserialize(rule.DefinitionJson).Trigger.Kind;
            return $"- {rule.Name} ({rule.Status}, trigger {trigger}, id {rule.Id})";
        }));
    }

    [Description("Create a draft automation from a typed JSON definition. Use schemaVersion 1 with trigger, optional conditions, and actions. Enable it separately.")]
    public async Task<string> CreateAutomationAsync(
        [Description("Short automation name.")] string name,
        [Description("Typed automation definition JSON (schemaVersion, trigger, conditions, actions, limits).")] string definitionJson,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var definition = AutomationDefinitionJson.Parse(definitionJson);
            var rule = await automations.CreateAsync(currentUser.OwnerId, new SaveAutomationRuleRequest(name, definition),
                cancellationToken);
            return $"Created draft automation '{rule.Name}' ({rule.Id}). Enable it when ready.";
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return $"Could not create automation: {exception.Message}";
        }
    }

    [Description("Enable a draft or disabled automation so Temporal can run its trigger.")]
    public async Task<string> EnableAutomationAsync(
        [Description("Automation rule id.")] Guid automationId,
        CancellationToken cancellationToken = default)
    {
        var rule = await automations.EnableAsync(automationId, currentUser.OwnerId, cancellationToken);
        return rule is null ? "Automation was not found." : $"Enabled automation '{rule.Name}'.";
    }

    [Description("Disable an enabled automation and stop its durable trigger workflow.")]
    public async Task<string> DisableAutomationAsync(
        [Description("Automation rule id.")] Guid automationId,
        CancellationToken cancellationToken = default)
    {
        var rule = await automations.DisableAsync(automationId, currentUser.OwnerId, cancellationToken);
        return rule is null ? "Automation was not found." : $"Disabled automation '{rule.Name}'.";
    }

    [Description("Run an automation once without waiting for its trigger. Destructive actions still require owner approval.")]
    public async Task<string> RunAutomationAsync(
        [Description("Automation rule id.")] Guid automationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var run = await automations.ManualRunAsync(automationId, currentUser.OwnerId, cancellationToken);
            return run is null
                ? "Automation was not found or is not enabled."
                : $"Started automation run {run.Id} (status {run.Status}).";
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }
}
