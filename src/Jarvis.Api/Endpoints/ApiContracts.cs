using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Jarvis.Api.Endpoints;

public sealed record CreateConversationRequest(string? Title, Guid? ProfileId = null, Guid? ProjectId = null);
public sealed record VoiceSessionRequest(Guid ConversationId);
public sealed record VoiceSessionDto(string ServerUrl, string Room, string Identity, string Token,
    DateTimeOffset ExpiresAt, bool HandsFree, bool Captions, string? Voice);
public sealed record CodexVoiceDto(string Id, string Name, bool IsDefault);
public sealed record VoiceSettingsDto(bool HandsFree, bool Captions, string? Voice, string? DefaultVoice,
    IReadOnlyList<CodexVoiceDto> Voices, string? CatalogError);
public sealed record VoiceWorkerTranscriptRequest(Guid OwnerId, string? Transcript);
public sealed record VoiceCaptionRequest(Guid OwnerId, string? Role, string? Text, bool Final);
public sealed record VoiceUtteranceRequest(Guid OwnerId, string? Role, string? Text);
public sealed record VoiceToolCallRequest(Guid OwnerId, JsonElement? Arguments);
public sealed record VoiceToolDto(string Name, string Description, JsonElement InputSchema, bool RequiresApproval);
public sealed record VoiceSessionBootstrapDto(string Instructions, IReadOnlyList<VoiceToolDto> Tools);
public sealed record VoiceToolCallResultDto(string Result, bool IsError, bool ApprovalRequired);
public sealed record SendMessageRequest([StringLength(32_000)] string? Content,
    IReadOnlyList<Guid>? ImageFileIds = null);
public sealed record MessageAttachmentDto(Guid FileId, string FileName, string ContentType);
public sealed record ConversationDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    Guid? ProfileId = null, string? ProfileName = null, int? ProfileVersion = null, bool ProfileDeleted = false,
    bool Pinned = false, Guid? ProjectId = null);
public sealed record UpdateConversationRequest(string? Title, bool? Pinned);
public sealed record ConversationDetailsDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<MessageDto> Messages, bool Responding, Guid? ProfileId = null, string? ProfileName = null,
    int? ProfileVersion = null, bool ProfileDeleted = false, bool Pinned = false, Guid? ProjectId = null);
public sealed record MessageDto(Guid Id, string Role, string Content, DateTimeOffset CreatedAt,
    IReadOnlyList<FileCitationDto>? Citations = null, IReadOnlyList<MessageAttachmentDto>? Attachments = null);
public sealed record FileCitationDto(Guid FileId, string DisplayName, Guid ChunkId, int ChunkIndex, string Excerpt,
    int? PageNumber, string SourceStatus = "available");
public sealed record MessagePageDto(IReadOnlyList<MessageDto> Items, string? NextCursor, bool HasMore);
public sealed record ApprovalDecisionRequest(bool Approved);

public sealed record OpenPullRequestRequest(string? Title, string? Body);
public sealed record ToolApprovalDto(Guid Id, Guid ConversationId, string ToolName, string ArgumentsJson,
    string Status, bool? Approved, string ResumeStatus, DateTimeOffset CreatedAt);
/// <summary>A timed reminder needs <c>DueAt</c>; a place reminder sends <c>Place</c> instead.</summary>
public sealed record ReminderRequest(string? Title, DateTimeOffset? DueAt, string? Recurrence = null,
    int Weekdays = 0, string? TimeZoneId = null, DateOnly? Until = null, TimeOnly? LocalTime = null,
    ReminderPlaceDto? Place = null);
public sealed record ReminderPlaceDto(string? Name, double? Latitude, double? Longitude, double? RadiusMeters = null,
    string? Trigger = null, bool Repeats = false);

/// <summary>Snooze for a number of minutes from now, or until an exact moment.</summary>
public sealed record SnoozeReminderRequest(int? Minutes = null, DateTimeOffset? Until = null);
public sealed record ReminderDto(Guid Id, string Title, DateTimeOffset DueAt, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string Recurrence, int Weekdays, string TimeZoneId, TimeOnly? LocalTime,
    DateOnly? Until, DateTimeOffset? LastDeliveredAt, Guid? ConversationId = null, ReminderPlaceDto? Place = null);
public sealed record ConditionWatchDto(Guid Id, string Title, string Url, string JsonPath, string Comparison,
    double Threshold, int IntervalMinutes, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? LastCheckedAt, double? LastValue, string Kind = "public_json", string? CredentialProvider = null,
    double? Latitude = null, double? Longitude = null, double? RadiusMeters = null, int? MinutesBefore = null);
public sealed record JarvisTaskDto(Guid Id, string Title, string Prompt, string Status, Guid ConversationId,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? Summary,
    Guid? ProfileId = null);
public sealed record NotificationDto(Guid Id, string Type, string Title, string Body, Guid? SourceId,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record PushDeviceRequest(string? Token, string? Platform);
public sealed record SaveIntegrationCredentialsRequest(Dictionary<string, string> Secrets);
public sealed record SaveIntegrationSecretRequest(string Value);
public sealed record AuditEventDto(Guid Id, Guid? AgentRunId, string Tool, string Action, string RiskClass,
    Guid? ApprovalId, DateTimeOffset Timestamp, bool Success, string? MetadataJson);
public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Sha256,
    DateTimeOffset CreatedAt, string ProcessingStatus);
public sealed record FileSearchHitDto(Guid FileId, string FileName, Guid ChunkId, int ChunkIndex, string Content,
    double Score, int ExcerptStart, int ExcerptEnd, int? PageNumber, string SanitizedExcerpt);
public sealed record ConversationSourcesDto(IReadOnlyList<ConversationFileSourceDto> Files,
    IReadOnlyList<ConversationCollectionSourceDto> Collections);
public sealed record ConversationFileSourceDto(Guid FileId, string FileName, string ProcessingStatus,
    DateTimeOffset AttachedAt);
public sealed record ConversationCollectionSourceDto(Guid CollectionId, string Name, DateTimeOffset AttachedAt);
public sealed record MemoryRequest(string? Kind, string? Content, float Importance = 0.5f, float Confidence = 0.8f,
    DateTimeOffset? ValidUntil = null, bool IsPinned = false);
public sealed record MemoryDto(Guid Id, string Kind, string Content, float Importance, float Confidence,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ValidUntil, bool IsPinned, string? SourceType);
public sealed record MemoryHitDto(MemoryDto Memory, double Score);

public sealed record JournalRequest(DateOnly? EntryDate, string? Content, string? Highlights, string? Gratitude,
    int? Rating, int? Mood, int? Energy, int? Stress, string[]? Tags, string? Source);
public sealed record JournalEntryDto(Guid Id, DateOnly EntryDate, string Source, string Content, string? Highlights,
    string? Gratitude, int? Rating, int? Mood, int? Energy, int? Stress, IReadOnlyList<string> Tags, Guid? MemoryId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record HabitRequest(string? Name, string? Icon, string? Cadence, int? TargetPerWeek, string? TimeZoneId);
public sealed record HabitArchiveRequest(bool Archived);
public sealed record HabitCheckInRequest(DateOnly? Date, bool? Done);
public sealed record HabitStatsDto(DateOnly Today, int CurrentStreak, int BestStreak, string StreakUnit, bool DoneToday,
    int ThisWeekCount, int TotalCheckIns, bool OpenToday, IReadOnlyList<DateOnly> RecentDates);
public sealed record HabitDto(Guid Id, string Name, string? Icon, string Cadence, int TargetPerWeek, bool Archived,
    DateTimeOffset? ArchivedAt, HabitStatsDto Stats, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record HabitsOverviewDto(DateOnly Today, IReadOnlyList<HabitDto> Habits,
    Jarvis.Application.Habits.HabitSettings Settings);
