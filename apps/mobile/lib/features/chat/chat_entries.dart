import 'dart:convert';

import '../../json_maps.dart';

/// One row in the chat transcript.
sealed class ChatEntry {
  const ChatEntry();
}

class MessageCitation {
  const MessageCitation({
    required this.fileId,
    required this.displayName,
    required this.chunkId,
    required this.chunkIndex,
    required this.excerpt,
    this.pageNumber,
    this.sourceStatus = 'available',
  });

  final String fileId;
  final String displayName;
  final String chunkId;
  final int chunkIndex;
  final String excerpt;
  final int? pageNumber;
  final String sourceStatus;

  static MessageCitation? fromJson(Object? value) {
    if (value is! Map) return null;
    final fileId = asJsonString(value['fileId'] ?? value['FileId']);
    final chunkId = asJsonString(value['chunkId'] ?? value['ChunkId']);
    final name = value['displayName'] ?? value['DisplayName'];
    if (fileId == null || chunkId == null || name is! String) return null;
    final index = value['chunkIndex'] ?? value['ChunkIndex'];
    final excerpt = value['excerpt'] ?? value['Excerpt'];
    final page = value['pageNumber'] ?? value['PageNumber'];
    final status = value['sourceStatus'] ?? value['SourceStatus'];
    return MessageCitation(
      fileId: fileId,
      displayName: name,
      chunkId: chunkId,
      chunkIndex: index is int ? index : int.tryParse('$index') ?? 0,
      excerpt: excerpt is String ? excerpt : '',
      pageNumber: page is int ? page : int.tryParse('$page'),
      sourceStatus: status is String ? status : 'available',
    );
  }
}

class ConversationSourceChip {
  const ConversationSourceChip({
    required this.id,
    required this.label,
    required this.kind,
  });

  final String id;
  final String label;
  final String kind;
}

class MessageEntry extends ChatEntry {
  const MessageEntry({
    required this.role,
    required this.content,
    this.pending = false,
    this.failed = false,
    this.id,
    this.rating,
    this.citations = const [],
  });

  final String role;
  final String content;
  final bool pending;

  /// Server message id, known once the reply is stored; needed for feedback.
  final String? id;

  /// The owner's feedback on an assistant reply: `up`, `down`, or null.
  final String? rating;

  /// Structured file citations for assistant replies.
  final List<MessageCitation> citations;

  /// A user message whose request did not complete and can be retried.
  final bool failed;

  bool get isUser => role == 'user';

  MessageEntry copyWith({
    String? content,
    bool? pending,
    bool? failed,
    String? id,
    String? rating,
    List<MessageCitation>? citations,
  }) => MessageEntry(
    role: role,
    content: content ?? this.content,
    pending: pending ?? this.pending,
    failed: failed ?? this.failed,
    id: id ?? this.id,
    rating: rating ?? this.rating,
    citations: citations ?? this.citations,
  );
}

/// Continues the in-flight assistant reply even when approval cards sit after it.
void appendAssistantDelta(List<ChatEntry> entries, String delta) {
  final pendingIndex = entries.lastIndexWhere(
    (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
  );
  if (pendingIndex >= 0) {
    final pending = entries[pendingIndex] as MessageEntry;
    entries[pendingIndex] = pending.copyWith(
      content: '${pending.content}$delta',
    );
    return;
  }
  final lastAssistant = entries.lastIndexWhere(
    (entry) => entry is MessageEntry && !entry.isUser,
  );
  final lastUser = entries.lastIndexWhere(
    (entry) => entry is MessageEntry && entry.isUser,
  );
  if (lastAssistant > lastUser) return;
  entries.add(MessageEntry(role: 'assistant', content: delta, pending: true));
}

enum ToolStepStatus { running, completed, failed }

class ToolStep {
  const ToolStep(this.tool, this.status);

  final String tool;
  final ToolStepStatus status;
}

/// Tools Jarvis used while working on one reply.
class ToolRunEntry extends ChatEntry {
  const ToolRunEntry(this.steps);

  final List<ToolStep> steps;

  bool get running =>
      steps.any((step) => step.status == ToolStepStatus.running);

  ToolRunEntry started(String tool) =>
      ToolRunEntry([...steps, ToolStep(tool, ToolStepStatus.running)]);

  ToolRunEntry finished(String tool, {required bool success}) {
    final index = steps.lastIndexWhere(
      (step) => step.tool == tool && step.status == ToolStepStatus.running,
    );
    final status = success ? ToolStepStatus.completed : ToolStepStatus.failed;
    if (index < 0) return ToolRunEntry([...steps, ToolStep(tool, status)]);
    return ToolRunEntry([
      for (var i = 0; i < steps.length; i++)
        i == index ? ToolStep(tool, status) : steps[i],
    ]);
  }

  ToolRunEntry settle() => ToolRunEntry([
    for (final step in steps)
      step.status == ToolStepStatus.running
          ? ToolStep(step.tool, ToolStepStatus.completed)
          : step,
  ]);
}

enum ApprovalStatus { pending, submitting, approved, denied, failed }

/// Turns a client-side "submitting" card into a stable terminal state when the
/// HTTP decision call finishes out of band (realtime events, stale responses).
ApprovalEntry resolveSubmittingApproval(
  ApprovalEntry entry, {
  ApprovalStatus? fallback,
}) {
  if (entry.status != ApprovalStatus.submitting) return entry;
  if (fallback != null) {
    return entry.copyWith(status: fallback);
  }
  final decision = entry.decision;
  if (decision == null) {
    return entry.copyWith(status: ApprovalStatus.pending, clearDecision: true);
  }
  return entry.copyWith(
    status: decision ? ApprovalStatus.approved : ApprovalStatus.denied,
  );
}

/// A tool call waiting for the user's decision, shown inline in the chat.
class ApprovalEntry extends ChatEntry {
  const ApprovalEntry({
    required this.id,
    required this.toolName,
    required this.argumentsJson,
    this.status = ApprovalStatus.pending,
    this.decision,
    this.error,
    this.retry = false,
  });

  final String id;
  final String toolName;
  final String argumentsJson;
  final ApprovalStatus status;
  final bool? decision;
  final String? error;

  /// The decision was recorded earlier but resuming the agent failed.
  final bool retry;

  static ApprovalEntry? fromJson(Object? value) {
    if (value is! Map) return null;
    final id = value['id'] ?? value['Id'];
    final tool = value['toolName'] ?? value['ToolName'];
    if (id is! String || tool is! String) return null;
    final arguments = value['argumentsJson'] ?? value['ArgumentsJson'];
    final status = value['status'];
    final approved = value['approved'];
    return ApprovalEntry(
      id: id,
      toolName: tool,
      argumentsJson: arguments is String ? arguments : '{}',
      retry: status is String && status != 'pending',
      decision: approved is bool ? approved : null,
    );
  }

  Map<String, Object?> get arguments {
    try {
      final decoded = jsonDecode(argumentsJson);
      if (decoded is! Map) return const {};
      return {
        for (final entry in decoded.entries)
          if (entry.key is String) entry.key as String: entry.value,
      };
    } catch (_) {
      // Malformed payloads render as no arguments instead of crashing the card.
    }
    return const {};
  }

  ApprovalEntry copyWith({
    ApprovalStatus? status,
    bool? decision,
    String? error,
    bool clearError = false,
    bool clearDecision = false,
  }) => ApprovalEntry(
    id: id,
    toolName: toolName,
    argumentsJson: argumentsJson,
    status: status ?? this.status,
    decision: clearDecision ? null : decision ?? this.decision,
    error: clearError ? null : error ?? this.error,
    retry: retry,
  );
}

/// A native card Jarvis rendered with RenderUi.
class UiSurfaceEntry extends ChatEntry {
  const UiSurfaceEntry({
    required this.id,
    required this.title,
    required this.status,
    required this.schema,
  });

  final String id;
  final String title;
  final String status;
  final Map<String, dynamic> schema;

  static UiSurfaceEntry? fromJson(Object? value) {
    if (value is! Map) return null;
    final id = value['id']?.toString();
    if (id == null || id.isEmpty) return null;
    final schema = value['schema'];
    return UiSurfaceEntry(
      id: id,
      title: value['title'] is String ? value['title'] as String : '',
      status: value['status'] is String ? value['status'] as String : 'open',
      schema: jsonObject(schema) ?? const <String, dynamic>{},
    );
  }

  UiSurfaceEntry copyWith({String? status}) => UiSurfaceEntry(
    id: id,
    title: title,
    status: status ?? this.status,
    schema: schema,
  );
}

List<MessageCitation> parseMessageCitations(Object? value) {
  if (value is! List) return const [];
  return value.map(MessageCitation.fromJson).whereType<MessageCitation>().toList();
}

class BrowserStepItem {
  const BrowserStepItem({
    required this.tool,
    required this.summary,
    required this.success,
  });

  final String tool;
  final String summary;
  final bool success;
}

/// Isolated browser/computer-use timeline for one goal.
class BrowserSessionEntry extends ChatEntry {
  const BrowserSessionEntry({
    required this.id,
    required this.goal,
    required this.steps,
  });

  final String id;
  final String goal;
  final List<BrowserStepItem> steps;

  BrowserSessionEntry withStep(BrowserStepItem step) =>
      BrowserSessionEntry(id: id, goal: goal, steps: [...steps, step]);
}
