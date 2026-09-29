part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenSession on _ChatScreenController {
  Future<void> _initialize() async {
    final generation = ++_initGeneration;
    bool stale() =>
        !mounted || generation != _initGeneration || _signedOut || _signingOut;
    try {
      if (stale()) return;
      if (_auth.enabled && await _auth.accessToken() == null) {
        if (mounted && generation == _initGeneration) {
          setState(() {
            _signedOut = true;
            _restoringSession = false;
          });
        }
        return;
      }
      if (stale()) return;
      await _enablePush();
      if (stale()) return;
      final list = await _http.get<dynamic>('/api/v1/conversations');
      if (stale()) return;
      final items = jsonMaps(list.data);
      String? conversationId;
      if (items.isNotEmpty && items.first['id'] is String) {
        conversationId = items.first['id'] as String;
      } else {
        final created = await _http.post<dynamic>(
          '/api/v1/conversations',
          data: const {'title': 'New conversation'},
        );
        conversationId = asJsonString(jsonObject(created.data)?['id']);
      }
      if (conversationId == null || conversationId.isEmpty) {
        throw const FormatException('Missing conversation ID.');
      }
      if (stale()) return;
      await _openConversation(conversationId, showHome: true);
      if (stale()) return;
      if (Firebase.apps.isNotEmpty) {
        final initialPush = await FirebaseMessaging.instance
            .getInitialMessage();
        if (initialPush != null && !stale()) {
          _handlePushPayload(initialPush.data);
        }
      }
      if (mounted && generation == _initGeneration) {
        setState(() {
          _error = null;
          _signedOut = false;
          _restoringSession = false;
        });
      }
    } on DioException catch (error) {
      if (mounted && generation == _initGeneration) {
        setState(() {
          _error = describeApiError(error);
          _restoringSession = false;
          if (isAuthExpired(error, authEnabled: _auth.enabled)) {
            _signedOut = true;
          }
        });
      }
    } on FormatException {
      if (mounted && generation == _initGeneration) {
        setState(() {
          _error = 'Jarvis returned an invalid conversation.';
          _restoringSession = false;
        });
      }
    } catch (error) {
      if (mounted && generation == _initGeneration) {
        setState(() {
          _error = 'Could not connect to Jarvis: $error';
          _signedOut = true;
          _restoringSession = false;
        });
      }
    }
  }

  Future<void> _openConversation(
    String conversationId, {
    bool showHome = false,
  }) async {
    final openGeneration = ++_openGeneration;
    bool isLatestOpen() =>
        mounted &&
        openGeneration == _openGeneration &&
        !_signedOut &&
        !_signingOut;
    final details = await _http.get<dynamic>(
      '/api/v1/conversations/$conversationId',
    );
    if (!isLatestOpen()) return;
    final body = jsonObject(details.data);
    final records = jsonMaps(body?['messages']);
    final responding = asJsonBool(body?['responding']);
    final approvals = await _loadConversationApprovals(conversationId);
    if (!isLatestOpen()) return;
    final knownApprovals = approvals ?? const <ApprovalEntry>[];

    _runCancel?.cancel();
    _catchUpTimer?.cancel();
    _catchUpGeneration++;
    _pendingQueryText = null;
    final generation = ++_realtimeGeneration;
    bool isCurrent() =>
        mounted &&
        generation == _realtimeGeneration &&
        !_signedOut &&
        !_signingOut;
    await _stopVoice();
    if (!isCurrent() || !isLatestOpen()) return;
    final previous = _hub;
    _hub = null;
    await previous?.stop();
    if (!isCurrent() || !isLatestOpen()) return;
    setState(() {
      _conversationId = conversationId;
      _connected = false;
      _sending = responding;
      _remoteQuery = responding;
      _selectedDestination = 0;
      _showHome =
          showHome &&
          !knownApprovals.any(
            (entry) =>
                entry.status == ApprovalStatus.pending ||
                entry.status == ApprovalStatus.failed,
          );
      _entries
        ..clear()
        ..addAll(
          records
              .where(
                (message) =>
                    message['role'] is String && message['content'] is String,
              )
              .map(
                (message) => MessageEntry(
                  role: message['role'] as String,
                  content: message['content'] as String,
                  id: asJsonString(message['id']),
                  citations: parseMessageCitations(message['citations']),
                ),
              ),
        )
        ..addAll(knownApprovals);
      if (responding) _ensurePlaceholder();
      _error = null;
    });
    if (responding) unawaited(_catchUpRemoteQuery(conversationId));
    _scrollToBottom(jump: true);
    unawaited(_loadRecent());
    unawaited(_loadConversationSources(conversationId));
    if (!isCurrent() || !isLatestOpen() || _conversationId != conversationId) {
      return;
    }
    try {
      await _connectRealtime(generation);
      if (isCurrent() && isLatestOpen() && _conversationId == conversationId) {
        unawaited(_loadConversationSurfaces(conversationId));
      }
      if (approvals == null &&
          isCurrent() &&
          isLatestOpen() &&
          _conversationId == conversationId) {
        unawaited(_syncConversationApprovals());
      }
    } catch (error) {
      if (!isCurrent() ||
          !isLatestOpen() ||
          _conversationId != conversationId) {
        return;
      }
      setState(() {
        _connected = false;
        _error = error is DioException
            ? describeApiError(error)
            : 'Could not connect realtime updates.';
      });
    }
  }

  Future<List<ApprovalEntry>?> _loadConversationApprovals(
    String conversationId,
  ) async {
    try {
      final response = await _http.get<dynamic>('/api/v1/approvals');
      return jsonMaps(response.data)
          .where((item) => item['conversationId'] == conversationId)
          .map(ApprovalEntry.fromJson)
          .whereType<ApprovalEntry>()
          .toList();
    } on DioException {
      return null;
    } catch (_) {
      return null;
    }
  }

  Future<void> _chooseConversation() async {
    _dismissKeyboard();
    final selection = await Navigator.of(context)
        .push<ConversationPickerResult>(
          MaterialPageRoute<ConversationPickerResult>(
            builder: (_) => ConversationsScreen(
              http: _http,
              selectedConversationId: _conversationId,
            ),
          ),
        );
    if (!mounted || _signedOut || _signingOut) return;
    unawaited(_loadRecent());
    if (selection?.deletedCurrent == true) {
      if (!mounted || _signedOut || _signingOut) return;
      await _clearCurrentConversation();
      if (!mounted || _signedOut || _signingOut) return;
      await _createAndOpenConversation();
    } else if (selection?.conversationId != null) {
      if (selection!.conversationId == _conversationId) {
        setState(() => _showHome = false);
        return;
      }
      if (!mounted || _signedOut || _signingOut) return;
      try {
        await _openConversation(selection.conversationId!);
      } on DioException catch (error) {
        if (mounted) setState(() => _error = describeApiError(error));
      } catch (_) {
        if (mounted) {
          setState(() => _error = 'Could not open that conversation.');
        }
      }
    }
  }

  Future<void> _clearCurrentConversation() async {
    _finishRemoteQuery();
    _runCancel?.cancel();
    _openGeneration++;
    _realtimeGeneration++;
    await _stopVoice();
    if (!mounted || _signedOut || _signingOut) return;
    final previous = _hub;
    _hub = null;
    await previous?.stop();
    if (!mounted || _signedOut || _signingOut) return;
    setState(() {
      _conversationId = null;
      _connected = false;
      _sending = false;
      _entries.clear();
      _attachedSources = [];
      _error = null;
    });
  }

  Future<void> _createAndOpenConversation() async {
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/conversations',
        data: const {'title': 'New conversation'},
      );
      final id = asJsonString(jsonObject(response.data)?['id']);
      if (id == null || id.isEmpty) {
        throw const FormatException('Missing conversation ID.');
      }
      await _openConversation(id);
    } on DioException catch (error) {
      if (mounted) setState(() => _error = describeApiError(error));
    } on FormatException {
      if (mounted) {
        setState(() => _error = 'Jarvis returned an invalid conversation.');
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Could not start a new conversation.');
      }
    }
  }

  Future<void> _reloadConversationEntries(
    String conversationId, [
    int? generation,
  ]) async {
    final expectedGeneration = generation ?? _realtimeGeneration;
    try {
      final details = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId',
      );
      final approvals = await _loadConversationApprovals(conversationId);
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != expectedGeneration ||
          _signedOut ||
          _signingOut) {
        return;
      }
      setState(() => _replaceTranscript(jsonObject(details.data), approvals));
      if (approvals == null) {
        unawaited(_syncConversationApprovals());
      }
      unawaited(_loadConversationSurfaces(conversationId));
    } on DioException {
      // Keep the current transcript if history cannot be refreshed.
    } catch (_) {
      // Keep the current transcript if history is malformed.
    }
  }
}
