using Jarvis.Application.Conversations;
using Jarvis.Application.Learning;
using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jarvis.Infrastructure.Persistence;

/// <summary>
/// Writes learning signals and run traces in the background, in a scope of its own, so a slow or failing database never
/// touches the turn that produced them. Like <see cref="ModelUsageRecorder"/>, failures are logged and dropped.
/// Nothing is stored for a conversation whose assistant profile does not contribute to learning, or when the owner
/// switched capture off; the profile's id is copied onto the row at write time.
/// </summary>
public sealed class LearningRecorder(IServiceScopeFactory scopes, ILogger<LearningRecorder> logger,
    TimeProvider? timeProvider = null) : ILearningRecorder
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public void RecordSignal(Guid ownerId, Guid conversationId, string kind, Guid? messageId = null,
        string? tool = null, string? category = null, string? errorKind = null) =>
        _ = Task.Run(() => StoreSignalAsync(ownerId, conversationId, kind, messageId, tool, category, errorKind));

    public void RecordTrace(Guid ownerId, TurnTraceDraft trace)
    {
        if (!trace.IsWorthKeeping) return;
        _ = Task.Run(() => StoreTraceAsync(ownerId, trace));
    }

    public async Task StoreSignalAsync(Guid ownerId, Guid conversationId, string kind, Guid? messageId, string? tool,
        string? category, string? errorKind)
    {
        if (!LearningSignalKinds.All.Contains(kind)) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await ResolveProfileAsync(scope.ServiceProvider, ownerId, conversationId) is not { } profile) return;
            await scope.ServiceProvider.GetRequiredService<ILearningStore>().AddSignalAsync(
                new LearningSignalRecord(Guid.CreateVersion7(), ownerId, kind, conversationId, messageId,
                    profile.ProfileId, tool, category, errorKind, _clock.GetUtcNow()), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not record a {Kind} learning signal.", kind);
        }
    }

    public async Task StoreTraceAsync(Guid ownerId, TurnTraceDraft trace)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await ResolveProfileAsync(scope.ServiceProvider, ownerId, trace.ConversationId) is not { } profile)
                return;
            await scope.ServiceProvider.GetRequiredService<ILearningStore>().AddTraceAsync(
                new TurnTraceRecord(Guid.CreateVersion7(), ownerId, trace.ConversationId, trace.MessageId,
                    profile.ProfileId, trace.Kind, trace.MemoryIds, trace.Skills, trace.Tools, trace.TotalMs,
                    trace.Outcome, _clock.GetUtcNow()), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not record a run trace.");
        }
    }

    /// <summary>The profile to file the row under, or null when nothing may be stored for this conversation.</summary>
    private static async Task<CaptureTarget?> ResolveProfileAsync(IServiceProvider services, Guid ownerId,
        Guid conversationId)
    {
        var settings = await services.GetRequiredService<IOwnerSettingsStore>()
            .GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, CancellationToken.None)
            ?? LearningSettings.Default;
        if (!settings.CaptureSignals) return null;
        var conversation = await services.GetRequiredService<IConversationStore>()
            .GetAsync(conversationId, ownerId, CancellationToken.None);
        if (conversation is null || !ProfileScope.ContributesToLearning(conversation.ProfileSnapshotJson))
            return null;
        return new CaptureTarget(conversation.ProfileId);
    }

    private sealed record CaptureTarget(Guid? ProfileId);
}
