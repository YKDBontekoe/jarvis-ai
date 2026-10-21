using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Improvements;

namespace Jarvis.Application.Improvements;

/// <summary>
/// The unattended half of self-evolution: once a night, when the owner allowed it, Jarvis picks the capability it lacked
/// most (gaps the owner accepted first), writes and tests a self-built app for it, and files the tested draft on the
/// proposal. Accepting the proposal is what switches the app on; nothing here does.
/// </summary>
public sealed class CapabilityEvolutionService(
    IImprovementRepository repository,
    ICapabilityBuilder builder,
    IOwnerSettingsStore settings,
    INotificationRepository notifications,
    IAuditEventStore audit,
    TimeProvider? timeProvider = null) : ICapabilityEvolutionService
{
    public const string NotificationType = "improvement.capability_built";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<int> EvolveAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var owner = await settings.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, cancellationToken)
                    ?? LearningSettings.Default;
        if (!owner.AutonomousEvolution || !owner.ProposeImprovements) return 0;
        var now = _clock.GetUtcNow();
        var next = PickNext(await repository.ListAsync(ownerId, cancellationToken), now);
        if (next is null) return 0;
        var (proposal, gap) = next.Value;

        var result = await builder.BuildAsync(ownerId, gap, cancellationToken);
        var payload = gap with
        {
            BuildStatus = result.Status,
            CustomServerId = result.CustomServerId ?? gap.CustomServerId,
            Sha256 = result.Status == CapabilityBuildStatuses.Built ? result.Sha256 : null,
            AppName = result.AppName ?? gap.AppName,
            Tools = result.Tools ?? gap.Tools,
            Keys = result.Keys ?? gap.Keys,
            Note = result.Note,
            Attempts = gap.Attempts + 1,
            LastAttemptAt = now
        };
        var built = payload.IsBuilt;
        await repository.UpdateAsync(proposal with
        {
            Title = Limit(CapabilityText.Title(payload), 300),
            Evidence = Limit(CapabilityText.Evidence(payload), 600),
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            // An owner who already said yes still has to say yes to this exact code.
            Status = built ? ImprovementStatuses.Pending : proposal.Status,
            ResultingRef = built ? null : proposal.ResultingRef,
            UpdatedAt = now
        }, cancellationToken);
        await AuditAsync(ownerId, proposal.Id, payload, cancellationToken);
        if (!built) return 0;
        try
        {
            await notifications.CreateAsync(ownerId, NotificationType,
                $"Jarvis built a draft app: {payload.AppName}",
                $"For {CapabilityGapRules.Readable(payload.Slug)}. It passed a test; review it in Settings → Learning to switch it on.",
                proposal.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The draft is filed on the proposal; a failed push must not lose it.
        }

        return 1;
    }

    /// <summary>
    /// The open gap to build next: one the owner accepted before one still waiting, then the most wanted. A gap is
    /// skipped once built, judged not feasible, out of attempts, or tried within the last week.
    /// </summary>
    public static (ImprovementProposal Proposal, CapabilityPayload Gap)? PickNext(
        IReadOnlyList<ImprovementProposal> proposals, DateTimeOffset now) =>
        proposals
            .Where(item => item.Kind == ImprovementKinds.Capability &&
                           item.Status is ImprovementStatuses.Pending or ImprovementStatuses.Accepted)
            .Select(item => (Proposal: item, Gap: ImprovementMiner.ReadCapability(item.PayloadJson)))
            .Where(pair => pair.Gap is { } gap &&
                           gap.BuildStatus is CapabilityBuildStatuses.None or CapabilityBuildStatuses.Failed &&
                           gap.Attempts < CapabilityGapRules.MaxBuildAttempts &&
                           (gap.LastAttemptAt is null || now - gap.LastAttemptAt >= CapabilityGapRules.RetryAfter))
            .OrderByDescending(pair => pair.Proposal.Status == ImprovementStatuses.Accepted)
            .ThenByDescending(pair => pair.Gap!.Conversations).ThenByDescending(pair => pair.Gap!.Reports)
            .ThenBy(pair => pair.Proposal.CreatedAt)
            .Select(pair => ((ImprovementProposal, CapabilityPayload)?)(pair.Proposal, pair.Gap!))
            .FirstOrDefault();

    private async Task AuditAsync(Guid ownerId, Guid proposalId, CapabilityPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            // Ids and outcome only; the slug is a label but stays out of the log like other proposal text.
            await audit.AppendAsync(ownerId, "improvements", "improvement.capability_attempted", "moderate", true,
                null, JsonSerializer.Serialize(new
                {
                    proposalId, status = payload.BuildStatus, customServerId = payload.CustomServerId,
                    attempts = payload.Attempts
                }, JsonOptions), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
