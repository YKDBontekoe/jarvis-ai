import 'dart:convert';
import 'dart:typed_data';

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

/// A photo sent with a user message. [bytes] is set for photos sent from
/// this device; older photos are fetched by [fileId] when shown.
class MessagePhoto {
  const MessagePhoto({
    required this.fileId,
    required this.fileName,
    this.bytes,
  });

  final String fileId;
  final String fileName;
  final Uint8List? bytes;

  static List<MessagePhoto> listFromJson(Object? value) {
    if (value is! List) return const [];
    return [
      for (final item in value)
        if (item is Map &&
            item['fileId'] is String &&
            (item['contentType'] is! String ||
                (item['contentType'] as String).startsWith('image/')))
          MessagePhoto(
            fileId: item['fileId'] as String,
            fileName: item['fileName'] is String
                ? item['fileName'] as String
                : 'Photo',
          ),
    ];
  }
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
    this.photos = const [],
    this.outboxId,
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

  /// Photos the user sent with this message.
  final List<MessagePhoto> photos;

  /// Set while this message waits on the device for a connection.
  final String? outboxId;

  bool get queued => outboxId != null;

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
    photos: photos,
    outboxId: outboxId,
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

/// What the "Jarvis is working" indicator says while a reply has no text yet.
/// Built only from tool names Jarvis already shows, never from prompt content.
String thinkingLabel(List<ChatEntry> entries) {
  final lastUser = entries.lastIndexWhere(
    (entry) => entry is MessageEntry && entry.isUser,
  );
  final steps = [
    for (final entry in entries.skip(lastUser + 1))
      if (entry is ToolRunEntry) ...entry.steps,
  ];
  if (steps.isEmpty) return 'Thinking';
  if (steps.any((step) => step.status == ToolStepStatus.running)) {
    return 'Working';
  }
  final done = steps.length;
  return done == 1
      ? 'Working out the next step'
      : 'Working out the next step · $done steps done';
}

enum ToolStepStatus { running, completed, failed }

class ToolStep {
  const ToolStep(this.tool, this.status, {this.refs = const []});

  final String tool;
  final ToolStepStatus status;

  /// What a completed call made or changed, as `type:id` refs, shown as cards.
  final List<String> refs;
}

/// Tools Jarvis used while working on one reply.
class ToolRunEntry extends ChatEntry {
  const ToolRunEntry(this.steps);

  final List<ToolStep> steps;

  bool get running =>
      steps.any((step) => step.status == ToolStepStatus.running);

  ToolRunEntry started(String tool) =>
      ToolRunEntry([...steps, ToolStep(tool, ToolStepStatus.running)]);

  ToolRunEntry finished(
    String tool, {
    required bool success,
    List<String> refs = const [],
  }) {
    final index = steps.lastIndexWhere(
      (step) => step.tool == tool && step.status == ToolStepStatus.running,
    );
    final status = success ? ToolStepStatus.completed : ToolStepStatus.failed;
    final done = ToolStep(tool, status, refs: success ? refs : const []);
    if (index < 0) return ToolRunEntry([...steps, done]);
    return ToolRunEntry([
      for (var i = 0; i < steps.length; i++) i == index ? done : steps[i],
    ]);
  }

  ToolRunEntry settle() => ToolRunEntry([
    for (final step in steps)
      step.status == ToolStepStatus.running
          ? ToolStep(step.tool, ToolStepStatus.completed, refs: step.refs)
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
    this.categoryLabel,
    this.canRememberCategory = false,
  });

  final String id;
  final String toolName;
  final String argumentsJson;
  final ApprovalStatus status;
  final bool? decision;
  final String? error;

  /// The decision was recorded earlier but resuming the agent failed.
  final bool retry;

  /// Owner-facing name of the action category, such as "Forgetting memories".
  final String? categoryLabel;

  /// The server can store a standing grant for this category.
  final bool canRememberCategory;

  static ApprovalEntry? fromJson(Object? value) {
    if (value is! Map) return null;
    final id = value['id'] ?? value['Id'];
    final tool = value['toolName'] ?? value['ToolName'];
    if (id is! String || tool is! String) return null;
    final arguments = value['argumentsJson'] ?? value['ArgumentsJson'];
    final status = value['status'];
    final approved = value['approved'];
    final label = value['categoryLabel'] ?? value['CategoryLabel'];
    return ApprovalEntry(
      id: id,
      toolName: tool,
      argumentsJson: arguments is String ? arguments : '{}',
      retry: status is String && status != 'pending',
      decision: approved is bool ? approved : null,
      categoryLabel: label is String && label.trim().isNotEmpty
          ? label.trim()
          : null,
      canRememberCategory:
          value['canRememberCategory'] == true ||
          value['CanRememberCategory'] == true,
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
    categoryLabel: categoryLabel,
    canRememberCategory: canRememberCategory,
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
  return value
      .map(MessageCitation.fromJson)
      .whereType<MessageCitation>()
      .toList();
}

class BrowserStepItem {
  const BrowserStepItem({
    required this.tool,
    required this.summary,
    required this.success,
    this.ordinal,
    this.hasScreenshot = false,
  });

  final String tool;
  final String summary;
  final bool success;

  /// Position in the session; with [hasScreenshot] it addresses the step's
  /// screenshot (computer sessions).
  final int? ordinal;
  final bool hasScreenshot;

  static BrowserStepItem fromJson(Map<Object?, Object?> json) =>
      BrowserStepItem(
        tool: asJsonString(json['tool']) ?? 'browser',
        summary: asJsonString(json['summary']) ?? '',
        success: json['success'] != false,
        ordinal: json['ordinal'] is int ? json['ordinal'] as int : null,
        hasScreenshot: json['hasScreenshot'] == true,
      );
}

/// Isolated browser/computer-use timeline for one goal.
class BrowserSessionEntry extends ChatEntry {
  const BrowserSessionEntry({
    required this.id,
    required this.goal,
    required this.steps,
    this.kind = 'browser',
    this.status = 'active',
    this.controlMode = 'agent',
  });

  final String id;
  final String goal;
  final List<BrowserStepItem> steps;

  /// `browser` (headless BrowseTheWeb) or `computer` (the sandbox desktop).
  final String kind;
  final String status;

  /// `agent` while Jarvis drives the computer, `user` after taking over.
  final String controlMode;

  bool get isComputer => kind == 'computer';
  bool get isLive => status == 'active';
  bool get userHasControl => controlMode == 'user';

  /// The newest step that has a screenshot, if any.
  BrowserStepItem? get latestScreenshot {
    for (final step in steps.reversed) {
      if (step.hasScreenshot && step.ordinal != null) return step;
    }
    return null;
  }

  BrowserSessionEntry withStep(BrowserStepItem step) =>
      copyWith(steps: [...steps, step]);

  BrowserSessionEntry copyWith({
    String? goal,
    List<BrowserStepItem>? steps,
    String? kind,
    String? status,
    String? controlMode,
  }) => BrowserSessionEntry(
    id: id,
    goal: goal ?? this.goal,
    steps: steps ?? this.steps,
    kind: kind ?? this.kind,
    status: status ?? this.status,
    controlMode: controlMode ?? this.controlMode,
  );
}
