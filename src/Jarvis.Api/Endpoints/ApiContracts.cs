using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Jarvis.Api.Endpoints;

public sealed record CreateConversationRequest(string? Title, Guid? ProfileId = null);
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
public sealed record SendMessageRequest([Required, StringLength(32_000, MinimumLength = 1)] string? Content);
public sealed record ConversationDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    Guid? ProfileId = null, string? ProfileName = null, int? ProfileVersion = null, bool ProfileDeleted = false);
public sealed record ConversationDetailsDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<MessageDto> Messages, bool Responding, Guid? ProfileId = null, string? ProfileName = null,
    int? ProfileVersion = null, bool ProfileDeleted = false);
public sealed record MessageDto(Guid Id, string Role, string Content, DateTimeOffset CreatedAt,
    IReadOnlyList<FileCitationDto>? Citations = null);
public sealed record FileCitationDto(Guid FileId, string DisplayName, Guid ChunkId, int ChunkIndex, string Excerpt,
    int? PageNumber, string SourceStatus = "available");
public sealed record MessagePageDto(IReadOnlyList<MessageDto> Items, string? NextCursor, bool HasMore);
public sealed record ApprovalDecisionRequest(bool Approved);
public sealed record ToolApprovalDto(Guid Id, Guid ConversationId, string ToolName, string ArgumentsJson,
    string Status, bool? Approved, string ResumeStatus, DateTimeOffset CreatedAt);
public sealed record ReminderRequest(string? Title, DateTimeOffset DueAt, string? Recurrence = null,
    int Weekdays = 0, string? TimeZoneId = null, DateOnly? Until = null, TimeOnly? LocalTime = null);
public sealed record ReminderDto(Guid Id, string Title, DateTimeOffset DueAt, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string Recurrence, int Weekdays, string TimeZoneId, TimeOnly? LocalTime,
    DateOnly? Until, DateTimeOffset? LastDeliveredAt, Guid? ConversationId = null);
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
