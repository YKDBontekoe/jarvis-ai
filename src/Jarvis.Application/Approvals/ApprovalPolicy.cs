using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Approvals;

/// <summary>
/// One approval-gated call to decide on. <paramref name="BackgroundTask"/> is true inside a durable task run, where
/// Jarvis acts without the owner watching. <paramref name="McpReadOnlyHint"/> is true when the tool is an integration
/// tool whose server declared it read-only.
/// </summary>
/// <param name="CategoryKey">The call's <see cref="ApprovalCategories"/> key, which the autonomous level's allow-list
/// is written in; null when unknown.</param>
public sealed record ApprovalPolicyRequest(Guid OwnerId, string ToolName, bool BackgroundTask,
    bool McpReadOnlyHint, Guid? ConversationId, string? CategoryKey = null, Guid? TaskId = null);

public sealed record ApprovalDecision(bool AutoApprove, string Reason, ToolRisk Risk);

/// <summary>
/// Decides whether an approval-gated call may run without asking, from the tool's risk class and the owner's
/// <see cref="AutonomySettings"/>. Standing per-category grants are a separate, older mechanism that stays in force.
/// </summary>
public interface IApprovalPolicy
{
    Task<ApprovalDecision> EvaluateAsync(ApprovalPolicyRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// How many calls the policy approved on one UTC day, for the daily limit, and how many of those reached outside
/// Jarvis at the autonomous level.
/// </summary>
public sealed record AutonomyUsage(string Day, int Count, int Outbound = 0);

public sealed class ApprovalPolicy(IOwnerSettingsStore settings, IAuditEventStore audit, TimeProvider? timeProvider = null,
    Events.IJarvisEventBus? events = null)
    : IApprovalPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<ApprovalDecision> EvaluateAsync(ApprovalPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var autonomy = await settings.GetAsync<AutonomySettings>(request.OwnerId, SettingsSections.Autonomy,
            cancellationToken) ?? AutonomySettings.Default;
        var risk = request.McpReadOnlyHint ? ToolRisk.ReadOnly : ToolRiskPolicy.Classify(request.ToolName);

        var refusal = Refuse(autonomy, request, risk);
        if (refusal is not null) return new ApprovalDecision(false, refusal, risk);

        var today = _clock.GetUtcNow().ToString("yyyy-MM-dd");
        var usage = await settings.GetAsync<AutonomyUsage>(request.OwnerId, SettingsSections.AutonomyUsage,
            cancellationToken);
        var used = usage?.Day == today ? usage.Count : 0;
        var outbound = usage?.Day == today ? usage.Outbound : 0;
        if (used >= autonomy.MaxAutoApprovalsPerDay)
            return new ApprovalDecision(false, "The daily limit for automatic approvals is used up.", risk);
        if (risk == ToolRisk.Outbound && outbound >= autonomy.MaxAutonomousOutboundPerDay)
            return new ApprovalDecision(false, "The daily limit for unattended outside actions is used up.", risk);

        await settings.SaveAsync(request.OwnerId, SettingsSections.AutonomyUsage,
            new AutonomyUsage(today, used + 1, outbound + (risk == ToolRisk.Outbound ? 1 : 0)), cancellationToken);
        var reason = Reason(risk, request);
        await TryAuditAsync(request, risk, reason, cancellationToken);
        if (risk != ToolRisk.ReadOnly)
            await Events.JarvisEventPublishing.TryPublishAsync(events, new Events.JarvisEvent(request.OwnerId,
                Events.JarvisEventKinds.AgentActed, $"Jarvis ran {request.ToolName} on its own",
                request.TaskId is { } taskId ? new Events.EntityRef(Events.EntityTypes.Task, taskId) : null,
                new Dictionary<string, string>
                {
                    ["tool"] = request.ToolName, ["risk"] = risk.ToString(), ["reason"] = reason,
                    ["category"] = request.CategoryKey ?? ""
                }, request.ConversationId, Events.EventOrigin.Agent, request.TaskId), cancellationToken);
        return new ApprovalDecision(true, reason, risk);
    }

    /// <summary>Null when the call is allowed by the owner's settings, otherwise why it still needs asking.</summary>
    internal static string? Refuse(AutonomySettings autonomy, ApprovalPolicyRequest request, ToolRisk risk)
    {
        if (!autonomy.Enabled) return "Autonomy is switched off.";
        if (autonomy.Level == AutonomyLevels.AskEverything) return "Everything is set to ask first.";
        if (autonomy.Level == AutonomyLevels.Autonomous) return RefuseAutonomous(autonomy, request, risk);
        if (!ToolRiskPolicy.CanAutoApprove(risk)) return $"This kind of action ({risk}) always asks.";
        if (risk == ToolRisk.ReadOnly)
        {
            var allowed = request.McpReadOnlyHint ? autonomy.AutoApproveMcpReadHints : autonomy.AutoApproveReadOnly;
            return allowed ? null : "Automatic approval of read-only tools is switched off.";
        }

        // Reversible changes only run unattended inside a background task, and only at the full level.
        return autonomy.Level == AutonomyLevels.Full && request.BackgroundTask
            ? null
            : "Changes ask first unless a background task runs at the full level.";
    }

    /// <summary>
    /// The autonomous level: read-only and reversible changes run anywhere; reaching outside Jarvis runs only in a
    /// background run and only in a category the owner allowed. Deleting, private reads and unknown tools always ask.
    /// </summary>
    private static string? RefuseAutonomous(AutonomySettings autonomy, ApprovalPolicyRequest request, ToolRisk risk)
    {
        switch (risk)
        {
            case ToolRisk.ReadOnly:
                var allowed = request.McpReadOnlyHint ? autonomy.AutoApproveMcpReadHints : autonomy.AutoApproveReadOnly;
                return allowed ? null : "Automatic approval of read-only tools is switched off.";
            case ToolRisk.ReversibleLocal:
                return null;
            case ToolRisk.Outbound:
                if (!request.BackgroundTask) return "Outside actions ask first while you are in the chat.";
                return AutonomousOutboundCategories.Allows(autonomy.AutonomousOutboundCategories, request.CategoryKey)
                    ? null
                    : "This kind of outside action is not allowed to run unattended.";
            default:
                return $"This kind of action ({risk}) always asks.";
        }
    }

    private static string Reason(ToolRisk risk, ApprovalPolicyRequest request) => risk switch
    {
        ToolRisk.ReadOnly => request.McpReadOnlyHint ? "Integration tool declared read-only" : "Read-only tool",
        ToolRisk.Outbound => "Outside action you allowed to run unattended",
        _ => request.BackgroundTask ? "Reversible change inside a background task" : "Reversible change (autonomous)"
    };

    private async Task TryAuditAsync(ApprovalPolicyRequest request, ToolRisk risk, string reason,
        CancellationToken cancellationToken)
    {
        var name = request.ToolName.Trim();
        if (name.Length > 120) name = name[..120];
        if (name.Length == 0) name = "approval_policy";
        try
        {
            // Names, risk and reason only: tool arguments can hold message text or credentials.
            await audit.AppendAsync(request.OwnerId, name, "approval.policy_auto_approved", "medium", true, null,
                JsonSerializer.Serialize(new
                {
                    risk = risk.ToString(), reason, background = request.BackgroundTask,
                    conversationId = request.ConversationId
                }, JsonOptions), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The approval is already counted; a failed audit write must not block the run.
        }
    }
}
