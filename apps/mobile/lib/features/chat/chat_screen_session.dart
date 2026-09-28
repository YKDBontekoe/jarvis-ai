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

  void _openUtility(String destination) {
    if (_signedOut || _signingOut) return;
    _dismissKeyboard();
    final page = utilityPageFor(destination, _http);
    if (destination == 'sign_out') {
      unawaited(_signOut());
    } else if (page != null) {
      unawaited(_openUtilityPage(destination, page));
    }
  }

  Future<void> _openUtilityPage(String destination, Widget page) async {
    await Navigator.of(
      context,
    ).push<void>(MaterialPageRoute<void>(builder: (_) => page));
    if (!mounted || _signedOut || _signingOut) return;
    if (destination == 'approvals') {
      await _syncConversationApprovals();
    }
    if (!mounted || _signedOut || _signingOut) return;
    if (destination == 'tasks' ||
        destination == 'approvals' ||
        destination == 'watches' ||
        destination == 'reminders') {
      setState(() => _homeRevision++);
    }
  }

  void _selectDestination(int index) {
    if (index == 2) {
      if (_busy ||
          _hasPendingApproval ||
          _signedOut ||
          _signingOut ||
          !_connected ||
          _conversationId == null) {
        if (!_connected || _conversationId == null) {
          setState(() => _error = 'Connect to Jarvis before starting voice.');
        }
        return;
      }
      _dismissKeyboard();
      unawaited(_toggleVoice());
      return;
    }
    _dismissKeyboard();
    if (_voiceActive || _voiceStarting) unawaited(_stopVoice());
    setState(() => _selectedDestination = index);
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

  Future<void> _signIn() async {
    if (_signingOut) return;
    final email = _email.text.trim();
    final password = _password.text;
    final formError = signInFormError(
      email: email,
      password: password,
      creatingAccount: _creatingAccount,
    );
    if (formError != null) {
      setState(() => _error = formError);
      return;
    }
    setState(() {
      _authBusy = true;
      _error = null;
    });
    try {
      if (_creatingAccount) {
        await _auth.register(email, password);
      } else {
        await _auth.signIn(email, password);
      }
      _password.clear();
      if (mounted) {
        setState(() {
          _signedOut = false;
          _restoringSession = true;
        });
      }
      await _initialize();
    } on DioException catch (error) {
      if (mounted) {
        setState(() {
          _error = accountErrorMessage(error);
          _signedOut = true;
          _restoringSession = false;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(() {
          _error = 'Sign in failed: $error';
          _signedOut = true;
          _restoringSession = false;
        });
      }
    } finally {
      if (mounted) setState(() => _authBusy = false);
    }
  }

  Future<void> _signOut() async {
    if (_signingOut) return;
    _signingOut = true;
    _signedOut = true;
    _initGeneration++;
    _realtimeGeneration++;
    final runningConversation = _conversationId;
    _stopRequested = true;
    _finishRemoteQuery();
    _runCancel?.cancel();
    if (runningConversation != null) {
      unawaited(_cancelServerRun(runningConversation));
    }
    if (mounted) {
      ScaffoldMessenger.of(context).clearSnackBars();
      Navigator.of(context).popUntil((route) => route.isFirst);
      setState(() {
        _signedOut = true;
        _restoringSession = false;
        _connected = false;
        _sending = false;
        _conversationId = null;
        _error = null;
        _entries.clear();
        _recent = [];
        _password.clear();
      });
    }
    try {
      await _stopVoice();
      final hub = _hub;
      _hub = null;
      final pushToken = _pushToken;
      if (pushToken != null) {
        try {
          await _http.delete<void>(
            '/api/v1/push-devices',
            data: {'token': pushToken},
          );
        } catch (_) {
          // The token is owner-scoped on the server; stale registrations expire at Firebase.
        }
        _pushToken = null;
      }
      await _pushTokenSubscription?.cancel();
      _pushTokenSubscription = null;
      await _pushOpenedSubscription?.cancel();
      _pushOpenedSubscription = null;
      await _pushForegroundSubscription?.cancel();
      _pushForegroundSubscription = null;
      if (Firebase.apps.isNotEmpty) {
        try {
          await FirebaseMessaging.instance.deleteToken();
        } on FirebaseException {
          // The server registration is also removed above when the API is reachable.
        }
      }
      await _auth.signOut();
      await hub?.stop();
    } finally {
      _signingOut = false;
      if (mounted) setState(() {});
    }
  }

  Future<void> _retryConnection() async {
    final conversationId = _conversationId;
    if (conversationId != null) {
      try {
        await _openConversation(conversationId, showHome: _showHome);
        if (mounted &&
            !_signedOut &&
            !_signingOut &&
            _conversationId == conversationId) {
          setState(() => _error = null);
        }
      } on DioException catch (error) {
        if (mounted &&
            !_signedOut &&
            !_signingOut &&
            _conversationId == conversationId) {
          setState(() {
            _error = describeApiError(error);
            if (isAuthExpired(error, authEnabled: _auth.enabled)) {
              _signedOut = true;
            }
          });
        }
      } catch (error) {
        if (mounted &&
            !_signedOut &&
            !_signingOut &&
            _conversationId == conversationId) {
          setState(() => _error = 'Could not connect to Jarvis: $error');
        }
      }
      return;
    }
    await _initialize();
  }

  Future<void> _loadRecent() async {
    if (_signedOut || _signingOut) return;
    final revision = ++_recentRevision;
    try {
      final response = await _http.get<dynamic>('/api/v1/conversations');
      if (!mounted ||
          _signedOut ||
          _signingOut ||
          revision != _recentRevision) {
        return;
      }
      setState(
        () => _recent = jsonMaps(
          response.data,
        ).where((item) => item['id'] is String).toList(),
      );
    } on DioException {
      // The sidebar keeps its last known list while the API is unreachable.
    } catch (_) {
      // The sidebar keeps its last known list when the payload is malformed.
    }
  }

  void _openSettings() => unawaited(
    Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => Scaffold(
          appBar: AppBar(title: const Text('Settings')),
          body: _settingsBody(),
        ),
      ),
    ),
  );

  void _startNewChat() {
    _dismissKeyboard();
    if (_selectedDestination != 0) setState(() => _selectedDestination = 0);
    if (!_hasMessages && _conversationId != null) {
      setState(() => _showHome = true);
      return;
    }
    unawaited(_createAndOpenConversation());
  }
}
