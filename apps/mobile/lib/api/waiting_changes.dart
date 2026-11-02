import 'package:flutter/foundation.dart';

import '../features/chat/outbox_store.dart';

/// Everything waiting for a connection, in one place: queued changes (see
/// [MutationQueue]) and chat messages that could not be sent yet. The chat
/// screen registers itself here so the offline banner and the overview page can
/// reach the messages, open the chat they belong to, and discard them.
class WaitingChanges {
  WaitingChanges();

  static final instance = WaitingChanges();

  /// Chat messages waiting to be sent.
  final messages = ValueNotifier<List<OutboxMessage>>(const []);

  /// Opens the overview page; null until the app shell is ready.
  final ValueNotifier<VoidCallback?> openOverview = ValueNotifier(null);

  /// Opens a conversation by id.
  void Function(String conversationId)? openConversation;

  /// Opens a utility page such as `reminders` or `habit:<id>`.
  void Function(String destination)? openDestination;

  /// Removes a waiting chat message for good.
  Future<void> Function(String messageId)? discardMessage;

  OutboxStore? _store;

  void attach(OutboxStore store) {
    _store?.changes.removeListener(_copy);
    _store = store..changes.addListener(_copy);
    _copy();
  }

  void _copy() => messages.value = _store?.changes.value ?? const [];

  /// Forgets everything, for example on sign-out.
  void reset() {
    _store?.changes.removeListener(_copy);
    _store = null;
    messages.value = const [];
    openConversation = null;
    openDestination = null;
    discardMessage = null;
    openOverview.value = null;
  }
}
