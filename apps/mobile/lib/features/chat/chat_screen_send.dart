part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenSend on _ChatScreenController {
  /// Puts an earlier message back in the composer so it can be changed and
  /// sent again. Asks first when that would replace an unsent draft.
  @override
  Future<void> _editAsNewMessage(String text) async {
    final current = _input.text.trim();
    if (current.isNotEmpty && current != text.trim()) {
      final replace = await showJarvisConfirm(
        context,
        title: 'Replace your draft?',
        message: 'The message box already has text you have not sent.',
        cancelLabel: 'Keep draft',
        confirmLabel: 'Replace',
        icon: PhosphorIconsRegular.pencilSimple,
      );
      if (!replace || !mounted) return;
    }
    _input.value = TextEditingValue(
      text: text,
      selection: TextSelection.collapsed(offset: text.length),
    );
    _composerFocus.value++;
  }

  Future<bool> _send([String? text]) async {
    final content = (text ?? _input.text).trim();
    final conversationId = _conversationId;
    final generation = _realtimeGeneration;
    if (content.isEmpty ||
        conversationId == null ||
        _busy ||
        _hasPendingApproval ||
        _voiceActive ||
        _voiceStarting ||
        _signedOut ||
        _signingOut) {
      return false;
    }
    if (text == null) _input.clear();
    _stopRequested = false;
    _pendingQueryText = content;
    final userMessage = MessageEntry(role: 'user', content: content);
    setState(() {
      _sending = true;
      _showHome = false;
      _selectedDestination = 0;
      _error = null;
      _entries.add(userMessage);
      _entries.add(
        const MessageEntry(role: 'assistant', content: '', pending: true),
      );
    });
    _scrollToBottom(force: true);
    final run = CancelToken();
    _runCancel = run;
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/conversations/$conversationId/messages',
        data: {'content': content},
        cancelToken: run,
        options: longRunningOptions(),
      );
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return true;
      }
      _finishRemoteQuery();
      setState(() => _applyRunResult(response));
      unawaited(_loadRecent());
      return true;
    } on DioException catch (error) {
      if (mounted &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        if (queryContinuesRemotely(error, stopRequested: _stopRequested)) {
          setState(() {
            _remoteQuery = true;
            _sending = true;
            _error = null;
            _ensurePlaceholder();
          });
          unawaited(_catchUpRemoteQuery(conversationId));
        } else {
          setState(() {
            _removePlaceholder();
            _settleToolRuns();
            final index = _entries.lastIndexOf(userMessage);
            if (index >= 0) {
              _entries[index] = userMessage.copyWith(failed: true);
            }
            _error = error.type == DioExceptionType.cancel
                ? null
                : describeApiError(error);
          });
        }
      }
      return true;
    } catch (_) {
      if (mounted &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(() {
          _removePlaceholder();
          _settleToolRuns();
          final index = _entries.lastIndexOf(userMessage);
          if (index >= 0) {
            _entries[index] = userMessage.copyWith(failed: true);
          }
          _error = 'Jarvis could not send that message.';
        });
      }
      return true;
    } finally {
      if (identical(_runCancel, run)) _runCancel = null;
      if (mounted &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation &&
          !_remoteQuery) {
        setState(() => _sending = false);
      }
      _scrollToBottom();
    }
  }

  void _applyRunResult(Response<dynamic> response) {
    final data = response.data;
    if (response.statusCode == 202 && data is List) {
      _addApprovals(
        data.map(ApprovalEntry.fromJson).whereType<ApprovalEntry>(),
      );
      return;
    }
    final content = data is Map ? asJsonString(data['content']) ?? '' : '';
    _completeAssistant(
      content,
      id: data is Map ? asJsonString(data['id']) : null,
    );
  }

  void _upsertSurface(UiSurfaceEntry surface) {
    if (surface.status == 'open') {
      for (var index = 0; index < _entries.length; index++) {
        final entry = _entries[index];
        if (entry is UiSurfaceEntry &&
            entry.id != surface.id &&
            entry.status == 'open') {
          _entries[index] = entry.copyWith(status: 'replaced');
        }
      }
      if (_surfaceErrorFor != null && _surfaceErrorFor != surface.id) {
        _surfaceError = null;
        _surfaceErrorFor = null;
      }
    }
    final existing = _entries.indexWhere(
      (entry) => entry is UiSurfaceEntry && entry.id == surface.id,
    );
    if (existing >= 0) {
      _entries[existing] = surface;
    } else {
      _entries.add(surface);
    }
    _collapseStaleSurfaces();
  }

  void _collapseStaleSurfaces() {
    final liveId = _liveSurface?.id;
    if (liveId == null) return;
    for (var index = 0; index < _entries.length; index++) {
      final entry = _entries[index];
      if (entry is UiSurfaceEntry &&
          entry.status == 'open' &&
          entry.id != liveId) {
        _entries[index] = entry.copyWith(status: 'replaced');
      }
    }
  }

  void _upsertBrowserSession(BrowserSessionEntry session) {
    final existing = _entries.indexWhere(
      (entry) => entry is BrowserSessionEntry && entry.id == session.id,
    );
    if (existing >= 0) {
      final current = _entries[existing] as BrowserSessionEntry;
      _entries[existing] = BrowserSessionEntry(
        id: session.id,
        goal: session.goal,
        steps: current.steps,
      );
    } else {
      _entries.add(session);
    }
  }

  void _appendBrowserStep(String sessionId, BrowserStepItem step) {
    final existing = _entries.indexWhere(
      (entry) => entry is BrowserSessionEntry && entry.id == sessionId,
    );
    if (existing >= 0) {
      _entries[existing] = (_entries[existing] as BrowserSessionEntry).withStep(
        step,
      );
    } else {
      _entries.add(
        BrowserSessionEntry(id: sessionId, goal: 'Browser task', steps: [step]),
      );
    }
  }

  Future<void> _submitSurface(
    UiSurfaceEntry surface,
    String action,
    Map<String, String> values,
  ) async {
    _pendingQueryText = null;
    if (mounted && _surfaceErrorFor == surface.id) {
      setState(() {
        _surfaceError = null;
        _surfaceErrorFor = null;
      });
    }
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/ui-surfaces/${surface.id}/actions',
        data: {'actionId': action, 'values': values},
        options: longRunningOptions(),
      );
      if (!mounted) return;
      setState(() {
        _upsertSurface(surface.copyWith(status: 'completed'));
        _surfaceError = null;
        _surfaceErrorFor = null;
        _applyRunResult(response);
      });
      _scrollToBottom();
    } on DioException catch (error) {
      if (!mounted) return;
      final conversationId = _conversationId;
      if (conversationId != null &&
          queryContinuesRemotely(error, stopRequested: _stopRequested)) {
        setState(() {
          _remoteQuery = true;
          _sending = true;
          _error = null;
          _surfaceError = null;
          _upsertSurface(surface.copyWith(status: 'completed'));
          _ensurePlaceholder();
        });
        unawaited(_catchUpRemoteQuery(conversationId));
        return;
      }
      final used = error.response?.statusCode == 409;
      final message = used
          ? 'This card was already used.'
          : describeApiError(error);
      setState(() {
        if (used) _upsertSurface(surface.copyWith(status: 'completed'));
        _surfaceError = message;
        _surfaceErrorFor = surface.id;
        if (used) _error = message;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _surfaceError = 'Jarvis could not use this card. Try again.';
        _surfaceErrorFor = surface.id;
      });
    }
  }

  Future<void> _loadConversationSurfaces(String conversationId) async {
    try {
      final response = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId/surfaces',
      );
      final sessions = await _http.get<dynamic>(
        '/api/v1/conversations/$conversationId/browser-sessions',
      );
      if (!mounted || _conversationId != conversationId) return;
      setState(() {
        for (final surface in jsonMaps(response.data)) {
          final entry = UiSurfaceEntry.fromJson(surface);
          if (entry != null) _upsertSurface(entry);
        }
        for (final session in jsonMaps(sessions.data)) {
          final id = asJsonString(session['id']);
          final goal = asJsonString(session['goal']);
          if (id == null || goal == null) continue;
          _upsertBrowserSession(
            BrowserSessionEntry(
              id: id,
              goal: goal,
              steps: [
                for (final step in jsonMaps(session['steps']))
                  BrowserStepItem(
                    tool: asJsonString(step['tool']) ?? 'browser',
                    summary: asJsonString(step['summary']) ?? '',
                    success: step['success'] != false,
                  ),
              ],
            ),
          );
        }
      });
    } on DioException {
      // Chat still works if surfaces cannot be refreshed.
    } catch (_) {
      // Chat still works if surface payloads are malformed.
    }
  }

  Future<void> _rate(MessageEntry message, String rating) async {
    final conversationId = _conversationId;
    final messageId = message.id;
    if (conversationId == null || messageId == null) return;
    String? note;
    if (rating == 'down') {
      note = await showFeedbackNoteDialog(context);
      if (note == null) return;
    }
    try {
      await _http.post<void>(
        '/api/v1/conversations/$conversationId/messages/$messageId/feedback',
        data: {
          'rating': rating,
          if (note != null && note.isNotEmpty) 'note': note,
        },
      );
      if (!mounted) return;
      setState(() {
        final index = _entries.indexWhere(
          (entry) => entry is MessageEntry && entry.id == messageId,
        );
        if (index >= 0) {
          _entries[index] = (_entries[index] as MessageEntry).copyWith(
            rating: rating,
          );
        }
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            rating == 'up'
                ? 'Thanks — Jarvis will keep doing this.'
                : 'Thanks — Jarvis will learn from this.',
          ),
        ),
      );
    } on DioException catch (error) {
      if (mounted) setState(() => _error = describeApiError(error));
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Jarvis could not save that rating.');
      }
    }
  }

  Future<void> _retry(MessageEntry message) async {
    if (_busy ||
        _hasPendingApproval ||
        _voiceActive ||
        _voiceStarting ||
        _signedOut ||
        _signingOut) {
      return;
    }
    setState(() => _entries.remove(message));
    final sent = await _send(message.content);
    if (!sent && mounted && !_entries.contains(message)) {
      setState(() => _entries.add(message));
    }
  }

  Future<void> _decide(ApprovalEntry approval, bool approved) async {
    final conversationId = _conversationId;
    if (conversationId == null ||
        _busy ||
        _voiceActive ||
        _voiceStarting ||
        _signedOut ||
        _signingOut) {
      return;
    }
    final generation = _realtimeGeneration;
    _stopRequested = false;
    _pendingQueryText = null;
    void replace(ApprovalEntry Function(ApprovalEntry current) update) {
      final index = _entries.indexWhere(
        (entry) => entry is ApprovalEntry && entry.id == approval.id,
      );
      if (index >= 0) {
        _entries[index] = update(_entries[index] as ApprovalEntry);
      }
    }

    setState(() {
      _error = null;
      replace(
        (current) => current.copyWith(
          status: ApprovalStatus.submitting,
          decision: approved,
          clearError: true,
        ),
      );
      _entries.add(
        const MessageEntry(role: 'assistant', content: '', pending: true),
      );
    });
    _scrollToBottom();
    final run = CancelToken();
    _runCancel = run;
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/approvals/${approval.id}/decision',
        data: {'approved': approved},
        cancelToken: run,
        options: longRunningOptions(),
      );
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return;
      }
      _finishRemoteQuery();
      setState(() {
        replace(
          (current) => current.copyWith(
            status: approved ? ApprovalStatus.approved : ApprovalStatus.denied,
          ),
        );
        _applyRunResult(response);
      });
      setState(() => _homeRevision++);
    } on DioException catch (error) {
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return;
      }
      if (queryContinuesRemotely(error, stopRequested: _stopRequested)) {
        setState(() {
          _remoteQuery = true;
          _sending = true;
          _error = null;
          _ensurePlaceholder();
        });
        unawaited(_catchUpRemoteQuery(conversationId));
        return;
      }
      if (error.type == DioExceptionType.cancel) {
        setState(() {
          _removePlaceholder();
          _settleToolRuns();
          replace(
            (current) => current.copyWith(
              status: ApprovalStatus.pending,
              clearError: true,
            ),
          );
        });
        await _syncConversationApprovals();
        return;
      }
      final status = error.response?.statusCode;
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        replace(
          (current) => status == 404
              ? current.copyWith(
                  status: ApprovalStatus.denied,
                  error: 'This approval is no longer pending.',
                )
              : status == 409
              ? current.copyWith(
                  status: ApprovalStatus.pending,
                  clearDecision: true,
                  error:
                      'Decide the earlier pending tool call first, or this one was already handled.',
                )
              : current.copyWith(
                  status: ApprovalStatus.failed,
                  error: 'Jarvis could not finish this step. You can retry.',
                ),
        );
      });
      if (status == 409) await _syncConversationApprovals();
    } catch (_) {
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return;
      }
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        replace(
          (current) => current.copyWith(
            status: ApprovalStatus.failed,
            error: 'Jarvis could not finish this step. You can retry.',
          ),
        );
      });
    } finally {
      if (identical(_runCancel, run)) _runCancel = null;
      if (mounted &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(_settleSubmittingApprovals);
      }
      _scrollToBottom();
    }
  }
}
