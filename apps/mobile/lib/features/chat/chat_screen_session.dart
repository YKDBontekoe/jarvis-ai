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
      // Push setup can wait on a permission dialog or the network; the chat
      // must not.
      unawaited(_enablePush());
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
      unawaited(_refreshUnreadNotifications());
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
    } catch (error, stack) {
      // Not a sign-in problem (for example a secure-storage hiccup), so keep
      // the stored session and offer Retry instead of asking for a password.
      reportError(error, stack, context: 'startup');
      if (mounted && generation == _initGeneration) {
        setState(() {
          _error = 'Could not connect to Jarvis. Check your connection and retry.';
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
    _closeUtilityPane();
    bool isLatestOpen() =>
        mounted &&
        openGeneration == _openGeneration &&
        !_signedOut &&
        !_signingOut;
    // The three reads are independent, so run them side by side.
    final detailsRequest = _http.get<dynamic>(
      '/api/v1/conversations/$conversationId',
      queryParameters: const {'includeMessages': false},
    );
    final messagesRequest = _http.get<dynamic>(
      '/api/v1/conversations/$conversationId/messages',
      queryParameters: const {'limit': 50},
    );
    final approvalsRequest = _loadConversationApprovals(conversationId);
    final Response<dynamic> details;
    final Response<dynamic> messagePage;
    try {
      details = await detailsRequest;
      messagePage = await messagesRequest;
    } catch (_) {
      // Mark the sibling futures as handled so a second failure is not reported.
      unawaited(messagesRequest.then<void>((_) {}, onError: (_) {}));
      unawaited(approvalsRequest.then<void>((_) {}, onError: (_) {}));
      rethrow;
    }
    if (!isLatestOpen()) return;
    final body = jsonObject(details.data);
    final page = jsonObject(messagePage.data);
    final records = jsonMaps(page?['items']);
    final responding = asJsonBool(body?['responding']);
    final approvals = await approvalsRequest;
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
      _profileId = asJsonString(body?['profileId']);
      _profileName = asJsonString(body?['profileName']);
      _profileDeleted = asJsonBool(body?['profileDeleted']);
      if (_profileId != null) _preferredProfileId = _profileId;
      _messageCursor = asJsonString(page?['nextCursor']);
      _hasOlderMessages = asJsonBool(page?['hasMore']);
      _loadingOlderMessages = false;
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
      _profileId = null;
      _profileName = null;
      _profileDeleted = false;
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
        data: {
          'title': 'New conversation',
          if (_preferredProfileId != null) 'profileId': _preferredProfileId,
        },
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
      final messages = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId/messages',
        queryParameters: const {'limit': 50},
      );
      final approvals = await _loadConversationApprovals(conversationId);
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != expectedGeneration ||
          _signedOut ||
          _signingOut) {
        return;
      }
      setState(() => _replaceTranscript(jsonObject(messages.data), approvals));
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

  Future<void> _loadOlderMessages() async {
    final conversationId = _conversationId;
    final cursor = _messageCursor;
    if (conversationId == null || cursor == null || !_hasOlderMessages ||
        _loadingOlderMessages) {
      return;
    }
    _loadingOlderMessages = true;
    try {
      final response = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId/messages',
        queryParameters: {'limit': 50, 'cursor': cursor},
      );
      if (!mounted || _conversationId != conversationId ||
          _messageCursor != cursor) {
        return;
      }
      final page = jsonObject(response.data);
      final messages = jsonMaps(page?['items'])
          .map(_messageEntryFromJson)
          .whereType<MessageEntry>()
          .toList();
      final existingIds = _entries
          .whereType<MessageEntry>()
          .map((entry) => entry.id)
          .whereType<String>()
          .toSet();
      messages.removeWhere(
        (message) =>
            message.id != null && existingIds.contains(message.id),
      );
      final beforeExtent = _scroll.hasClients
          ? _scroll.position.maxScrollExtent
          : 0.0;
      setState(() {
        _entries.insertAll(0, messages);
        _messageCursor = asJsonString(page?['nextCursor']);
        _hasOlderMessages = asJsonBool(page?['hasMore']);
      });
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || !_scroll.hasClients) return;
        final addedExtent = _scroll.position.maxScrollExtent - beforeExtent;
        _scroll.jumpTo((_scroll.position.pixels + addedExtent)
            .clamp(0.0, _scroll.position.maxScrollExtent)
            .toDouble());
      });
    } on DioException {
      // Keep the current page and allow a later scroll to retry.
    } finally {
      _loadingOlderMessages = false;
    }
  }

  Future<void> _switchConversationProfile() async {
    if (_conversationId == null || _busy) return;
    try {
      final response = await _http.get<dynamic>('/api/v1/profiles');
      final profiles = jsonMaps(response.data)
          .where((item) => jsonString(item, 'id') != null)
          .toList();
      if (!mounted || profiles.isEmpty) return;
      final selected = await showModalBottomSheet<Map<String, dynamic>>(
        context: context,
        builder: (context) => SafeArea(
          child: ListView(
            shrinkWrap: true,
            children: [
              const ListTile(title: Text('Switch assistant profile')),
              for (final profile in profiles)
                ListTile(
                  leading: Icon(
                    asJsonBool(profile['isDefault'])
                        ? PhosphorIconsFill.userCircle
                        : PhosphorIconsRegular.userCircle,
                  ),
                  title: Text(asJsonString(profile['name']) ?? 'Profile'),
                  subtitle: asJsonString(profile['description']) == null
                      ? null
                      : Text(asJsonString(profile['description'])!),
                  selected: jsonString(profile, 'id') == _profileId,
                  onTap: () => Navigator.pop(context, profile),
                ),
            ],
          ),
        ),
      );
      if (selected == null || !mounted) return;
      final id = jsonString(selected, 'id');
      if (id == null || id == _profileId) return;
      var confirm = false;
      while (true) {
        try {
          final updated = await _http.put<dynamic>(
            '/api/v1/conversations/$_conversationId/profile',
            data: {'profileId': id, 'confirm': confirm},
          );
          final body = jsonObject(updated.data);
          if (!mounted) return;
          setState(() {
            _profileId = asJsonString(body?['profileId']) ?? id;
            _profileName = asJsonString(body?['profileName']) ??
                asJsonString(selected['name']);
            _profileDeleted = asJsonBool(body?['profileDeleted']);
            _preferredProfileId = _profileId;
          });
          return;
        } on DioException catch (error) {
          if (error.response?.statusCode != 409 || confirm) rethrow;
          final details = jsonStrings(jsonObject(error.response?.data)?['details']);
          final confirmed = await showJarvisConfirm(
            context,
            title: 'Switch profile?',
            message: details.isEmpty
                ? 'This changes the tools or knowledge Jarvis can use in this conversation.'
                : details.join('\n'),
            cancelLabel: 'Keep current profile',
            confirmLabel: 'Switch',
            icon: PhosphorIconsRegular.identificationCard,
          );
          if (!confirmed || !mounted) return;
          confirm = true;
        }
      }
    } on DioException catch (error) {
      if (mounted) setState(() => _error = describeApiError(error));
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Could not switch the assistant profile.');
      }
    }
  }

  MessageEntry? _messageEntryFromJson(Map<String, dynamic> message) {
    if (message['role'] is! String || message['content'] is! String) return null;
    return MessageEntry(
      role: message['role'] as String,
      content: message['content'] as String,
      id: asJsonString(message['id']),
    );
  }
}
