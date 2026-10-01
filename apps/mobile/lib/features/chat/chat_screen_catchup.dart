part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenCatchUp on _ChatScreenController {
  void _finishRemoteQuery() {
    _remoteQuery = false;
    _pendingQueryText = null;
    _catchUpGeneration++;
    _catchUpTimer?.cancel();
    _catchUpTimer = null;
    _sending = false;
  }

  void _ensurePlaceholder() {
    final pending = _entries.any(
      (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
    );
    if (!pending) {
      _entries.add(
        const MessageEntry(role: 'assistant', content: '', pending: true),
      );
    }
  }

  void _scheduleCatchUp(
    String conversationId,
    int generation,
    int realtime, {
    Duration delay = const Duration(seconds: 2),
    int failedAttempts = 0,
  }) {
    _catchUpTimer?.cancel();
    _catchUpTimer = Timer(delay, () {
      if (!mounted ||
          !_remoteQuery ||
          generation != _catchUpGeneration ||
          realtime != _realtimeGeneration ||
          _conversationId != conversationId ||
          _signedOut ||
          _signingOut) {
        return;
      }
      unawaited(
        _catchUpRemoteQuery(conversationId, failedAttempts: failedAttempts),
      );
    });
  }

  void _failCatchUp(String message) {
    setState(() {
      _removePlaceholder();
      _settleToolRuns();
      _error = message;
      _finishRemoteQuery();
    });
  }

  Future<void> _catchUpRemoteQuery(
    String conversationId, {
    int failedAttempts = 0,
  }) async {
    final generation = ++_catchUpGeneration;
    final realtime = _realtimeGeneration;
    bool current() =>
        mounted &&
        _remoteQuery &&
        generation == _catchUpGeneration &&
        realtime == _realtimeGeneration &&
        _conversationId == conversationId &&
        !_signedOut &&
        !_signingOut;
    try {
      final details = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId',
        queryParameters: const {'includeMessages': false},
      );
      if (!current()) return;
      var payload = jsonObject(details.data);
      var messageResponse = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId/messages',
        queryParameters: const {'limit': 50},
      );
      if (!current()) return;
      var messagePage = jsonObject(messageResponse.data);
      var stored = jsonMaps(messagePage?['items']);
      if (asJsonBool(payload?['responding']) ||
          !serverStoredReply(stored, _pendingQueryText)) {
        if (asJsonBool(payload?['responding'])) {
          setState(() {
            _sending = true;
            _error = null;
            _ensurePlaceholder();
          });
          _scheduleCatchUp(conversationId, generation, realtime);
          return;
        }
        // The running flag drops after the reply is stored. Read once more so a
        // completion between those two reads is not shown as a failure.
        await Future<void>.delayed(const Duration(milliseconds: 300));
        if (!current()) return;
        final confirmed = await _http.get<dynamic>(
          '/api/v1/conversations/$conversationId',
          queryParameters: const {'includeMessages': false},
        );
        if (!current()) return;
        payload = jsonObject(confirmed.data);
        messageResponse = await _http.get<dynamic>(
          '/api/v1/conversations/$conversationId/messages',
          queryParameters: const {'limit': 50},
        );
        if (!current()) return;
        messagePage = jsonObject(messageResponse.data);
        stored = jsonMaps(messagePage?['items']);
        if (asJsonBool(payload?['responding'])) {
          setState(() {
            _sending = true;
            _error = null;
            _ensurePlaceholder();
          });
          _scheduleCatchUp(conversationId, generation, realtime);
          return;
        }
      }
      final sent = _pendingQueryText;
      stored = jsonMaps(messagePage?['items']);
      final accepted =
          sent == null ||
          stored.any(
            (message) =>
                message['role'] == 'user' && message['content'] == sent,
          );
      if (!accepted) {
        setState(() {
          _removePlaceholder();
          _settleToolRuns();
          final index = _entries.lastIndexWhere(
            (entry) =>
                entry is MessageEntry && entry.isUser && entry.content == sent,
          );
          if (index >= 0) {
            _entries[index] = (_entries[index] as MessageEntry).copyWith(
              failed: true,
            );
          }
          _error = 'Could not reach Jarvis. The message was not sent.';
          _finishRemoteQuery();
        });
        return;
      }
      final approvals = await _loadConversationApprovals(conversationId);
      if (!current()) return;
      setState(() {
        _replaceTranscript(messagePage, approvals);
        _remoteQuery = false;
        _sending = false;
        _pendingQueryText = null;
        _catchUpTimer?.cancel();
        if (!_hasPendingApproval) {
          final index = _entries.lastIndexWhere(
            (entry) => entry is MessageEntry,
          );
          if (index >= 0 && (_entries[index] as MessageEntry).isUser) {
            _entries[index] = (_entries[index] as MessageEntry).copyWith(
              failed: true,
            );
            _error = 'Jarvis could not complete this response.';
          }
        }
      });
      if (approvals == null) unawaited(_syncConversationApprovals());
      unawaited(_loadConversationSurfaces(conversationId));
      unawaited(_loadRecent());
    } on DioException catch (error) {
      if (!current()) return;
      final delay = catchUpRetryDelay(
        failedAttempts: failedAttempts + 1,
        error: error,
        stopRequested: _stopRequested,
      );
      if (delay == null) {
        _failCatchUp(
          queryContinuesRemotely(error, stopRequested: false)
              ? 'Lost the connection while Jarvis was still working. Open this conversation again to catch up.'
              : describeApiError(error),
        );
        return;
      }
      _scheduleCatchUp(
        conversationId,
        generation,
        realtime,
        delay: delay,
        failedAttempts: failedAttempts + 1,
      );
    } catch (_) {
      if (!current()) return;
      _failCatchUp('Jarvis returned an unexpected conversation.');
    }
  }

  void _replaceTranscript(
    Map<String, dynamic>? details,
    List<ApprovalEntry>? approvals,
  ) {
    final knownApprovals =
        approvals ?? _entries.whereType<ApprovalEntry>().toList();
    _entries
      ..clear()
      ..addAll(
        jsonMaps(details?['items'])
            .where(
              (message) =>
                  message['role'] is String && message['content'] is String,
            )
            .map(
              (message) => MessageEntry(
                role: message['role'] as String,
                content: message['content'] as String,
                id: asJsonString(message['id']),
                photos: MessagePhoto.listFromJson(message['attachments']),
              ),
            ),
      );
    if (_conversationId case final id?) _entries.addAll(_queuedEntries(id));
    _messageCursor = asJsonString(details?['nextCursor']);
    _hasOlderMessages = asJsonBool(details?['hasMore']);
    _addApprovals(knownApprovals);
  }

  Future<void> _cancelServerRun(String conversationId) async {
    try {
      await _http.post<void>('/api/v1/conversations/$conversationId/cancel');
    } on DioException {
      // Stopping locally still applies when the server is unreachable.
    } catch (_) {
      // Stopping locally still applies when cancel cannot be confirmed.
    }
  }
}
