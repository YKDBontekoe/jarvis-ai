part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenNav on _ChatScreenController {
  void _openUtility(String destination) {
    if (_signedOut || _signingOut) return;
    _dismissKeyboard();
    final page = utilityPageFor(
      destination,
      _http,
      onAskInChat: destination == 'integrations'
          ? (prompt) => unawaited(_send(prompt))
          : null,
    );
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
        destination == 'reminders' ||
        destination == 'automations' ||
        destination == 'usage' ||
        destination == 'integrations' ||
        destination == 'coding' ||
        destination == 'graph' ||
        destination == 'channels') {
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
      await RecentSearchesStore.clearAll();
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
