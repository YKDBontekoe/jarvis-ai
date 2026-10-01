part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

/// Messages typed while Jarvis cannot be reached wait on the device and go
/// out by themselves, in order, once the connection is back.
mixin _ChatScreenOutbox on _ChatScreenController {
  static const _retryEvery = Duration(seconds: 15);
  int _outboxSerial = 0;

  Future<void> _openOutbox() async {
    final stored = await OutboxStore.open();
    if (!mounted || _signedOut) return;
    _outbox = stored;
    final id = _conversationId;
    if (id == null) return;
    final missing = _queuedEntries(id)
        .where(
          (entry) => !_entries.any(
            (item) => item is MessageEntry && item.outboxId == entry.outboxId,
          ),
        )
        .toList();
    if (missing.isNotEmpty) setState(() => _entries.addAll(missing));
    _scheduleOutboxRetry();
  }

  List<MessageEntry> _queuedEntries(String conversationId) => [
    for (final item in _outbox.forConversation(conversationId))
      MessageEntry(
        role: 'user',
        content: item.content,
        outboxId: item.id,
        photos: [
          for (final photo in item.photos)
            MessagePhoto(fileId: photo.fileId, fileName: photo.fileName),
        ],
      ),
  ];

  /// Keeps [message] for later and shows it as waiting in the transcript.
  Future<void> _queueMessage(
    MessageEntry message,
    String conversationId,
  ) async {
    final item = OutboxMessage(
      id: 'outbox-${DateTime.now().microsecondsSinceEpoch}-${++_outboxSerial}',
      conversationId: conversationId,
      content: message.content,
      queuedAt: DateTime.now(),
      photos: [
        for (final photo in message.photos)
          (fileId: photo.fileId, fileName: photo.fileName),
      ],
    );
    final index = _entries.lastIndexOf(message);
    final queued = MessageEntry(
      role: 'user',
      content: message.content,
      photos: message.photos,
      outboxId: item.id,
    );
    if (index >= 0) {
      _entries[index] = queued;
    } else {
      _entries.add(queued);
    }
    await _outbox.add(item);
    _scheduleOutboxRetry();
  }

  void _scheduleOutboxRetry() {
    _outboxTimer?.cancel();
    if (_outbox.isEmpty) return;
    _outboxTimer = Timer(_retryEvery, () => unawaited(_flushOutbox()));
  }

  /// Sends waiting messages of the open conversation. A message the server
  /// already has (the connection dropped after it arrived) is not sent twice.
  Future<void> _flushOutbox() async {
    final conversationId = _conversationId;
    if (_flushingOutbox ||
        conversationId == null ||
        _signedOut ||
        _signingOut ||
        _outbox.forConversation(conversationId).isEmpty) {
      _scheduleOutboxRetry();
      return;
    }
    if (_busy || _hasPendingApproval || _voiceActive || _voiceStarting) {
      _scheduleOutboxRetry();
      return;
    }
    _flushingOutbox = true;
    try {
      for (final item in _outbox.forConversation(conversationId)) {
        if (!mounted || _conversationId != conversationId) return;
        final List<Map<String, dynamic>> recent;
        try {
          final response = await _http.get<dynamic>(
            '/api/v1/conversations/$conversationId/messages',
            queryParameters: const {'limit': 20},
          );
          recent = jsonMaps(jsonObject(response.data)?['items']);
        } on DioException catch (error) {
          if (error.response == null) return; // Still offline.
          rethrow;
        }
        if (!mounted || _conversationId != conversationId) return;
        final arrived = recent.any((message) {
          final created = jsonDate(message['createdAt']);
          return message['role'] == 'user' &&
              message['content'] == item.content &&
              created != null &&
              !created.isBefore(
                item.queuedAt.toUtc().subtract(const Duration(minutes: 1)),
              );
        });
        await _outbox.remove(item.id);
        if (!mounted) return;
        setState(
          () => _entries.removeWhere(
            (entry) => entry is MessageEntry && entry.outboxId == item.id,
          ),
        );
        if (arrived) {
          await _openConversation(conversationId);
          continue;
        }
        _lastSendQueued = false;
        await _send(item.content, [
          for (final photo in item.photos)
            MessagePhoto(fileId: photo.fileId, fileName: photo.fileName),
        ]);
        if (_lastSendQueued) return;
      }
    } catch (_) {
      // Leave what is still queued for the next attempt.
    } finally {
      _flushingOutbox = false;
      _scheduleOutboxRetry();
    }
  }

  /// Drops a waiting message and puts its text back in an empty composer.
  Future<void> _cancelQueued(MessageEntry entry) async {
    final id = entry.outboxId;
    if (id == null) return;
    await _outbox.remove(id);
    if (!mounted) return;
    setState(() => _entries.remove(entry));
    if (_input.text.trim().isEmpty && !_photoOnlyContent(entry)) {
      _input.value = TextEditingValue(
        text: entry.content,
        selection: TextSelection.collapsed(offset: entry.content.length),
      );
    }
    _scheduleOutboxRetry();
  }

  bool _photoOnlyContent(MessageEntry entry) =>
      entry.photos.isNotEmpty &&
      RegExp(r'^Shared (a photo|\d+ photos)\.$').hasMatch(entry.content);
}
