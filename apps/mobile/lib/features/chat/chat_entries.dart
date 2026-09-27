import 'dart:convert';

/// One row in the chat transcript.
sealed class ChatEntry {
  const ChatEntry();
}

class MessageEntry extends ChatEntry {
  const MessageEntry({
    required this.role,
    required this.content,
    this.pending = false,
    this.failed = false,
    this.id,
    this.rating,
  });

  final String role;
  final String content;
  final bool pending;

  /// Server message id, known once the reply is stored; needed for feedback.
  final String? id;

  /// The owner's feedback on an assistant reply: `up`, `down`, or null.
  final String? rating;

  /// A user message whose request did not complete and can be retried.
  final bool failed;

  bool get isUser => role == 'user';

  MessageEntry copyWith({
    String? content,
    bool? pending,
    bool? failed,
    String? id,
    String? rating,
  }) => MessageEntry(
    role: role,
    content: content ?? this.content,
    pending: pending ?? this.pending,
    failed: failed ?? this.failed,
    id: id ?? this.id,
    rating: rating ?? this.rating,
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
      if (decoded is Map) return Map<String, Object?>.from(decoded);
    } on FormatException {
      // Fall through to an empty argument list for malformed payloads.
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
      schema: schema is Map
          ? Map<String, dynamic>.from(schema)
          : const <String, dynamic>{},
    );
  }

  UiSurfaceEntry copyWith({String? status}) => UiSurfaceEntry(
    id: id,
    title: title,
    status: status ?? this.status,
    schema: schema,
  );
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

  BrowserSessionEntry withStep(BrowserStepItem step) => BrowserSessionEntry(
    id: id,
    goal: goal,
    steps: [...steps, step],
  );
}
