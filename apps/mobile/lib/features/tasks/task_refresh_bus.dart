import 'package:flutter/foundation.dart';

/// Tells open task pages that something happened in a conversation, so a task
/// page can refresh without waiting for its next poll.
class TaskRefreshBus extends ChangeNotifier {
  TaskRefreshBus();

  static final instance = TaskRefreshBus();

  String? _lastConversationId;

  /// The conversation the latest event belonged to.
  String? get lastConversationId => _lastConversationId;

  void bump(String? conversationId) {
    _lastConversationId = conversationId;
    notifyListeners();
  }
}

/// Task statuses that never change again.
bool isTerminalTaskStatus(String? status) =>
    status == 'completed' || status == 'failed' || status == 'cancelled';
