part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenSend on _ChatScreenController {
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
    _scrollToBottom();
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
      final response = await _http.get<List<dynamic>>(
        '/api/v1/conversations/$conversationId/surfaces',
      );
      final sessions = await _http.get<List<dynamic>>(
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
      final details = await _http.get<Map<String, dynamic>>(
        '/api/v1/conversations/$conversationId',
      );
      if (!current()) return;
      var payload = details.data;
      var stored = jsonMaps(payload?['messages']);
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
        final confirmed = await _http.get<Map<String, dynamic>>(
          '/api/v1/conversations/$conversationId',
        );
        if (!current()) return;
        payload = confirmed.data;
        stored = jsonMaps(payload?['messages']);
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
      stored = jsonMaps(payload?['messages']);
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
        _replaceTranscript(payload, approvals);
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
        jsonMaps(details?['messages'])
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
      );
    _addApprovals(knownApprovals);
  }

  Future<void> _cancelServerRun(String conversationId) async {
    try {
      await _http.post<void>('/api/v1/conversations/$conversationId/cancel');
    } on DioException {
      // Stopping locally still applies when the server is unreachable.
    }
  }

  Future<void> _cancelActiveRun() async {
    final conversationId = _conversationId;
    _stopRequested = true;
    _finishRemoteQuery();
    _runCancel?.cancel();
    if (mounted) {
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        final index = _entries.lastIndexWhere(
          (entry) => entry is MessageEntry && entry.isUser,
        );
        if (index >= 0) {
          _entries[index] = (_entries[index] as MessageEntry).copyWith(
            failed: true,
          );
        }
      });
    }
    if (conversationId != null) await _cancelServerRun(conversationId);
  }

  Future<void> _handleDeviceInvoke(
    String invokeId,
    String capability,
    Map<Object?, Object?>? event,
  ) async {
    final outcome = await performDeviceCapability(
      capability: capability,
      event: event,
      mounted: mounted,
      context: context,
    );
    final result = outcome.result;
    final error = outcome.error;
    try {
      await _http.post<void>(
        '/api/v1/devices/invoke/$invokeId/result',
        data: {'result': result, 'error': error},
      );
    } on DioException {
      try {
        await _hub?.invoke(
          'CompleteDeviceInvoke',
          args: [invokeId, result ?? '', error ?? ''],
        );
      } catch (_) {}
    }
  }

  int get _placeholderIndex {
    if (_entries.isEmpty) return -1;
    final last = _entries.last;
    return last is MessageEntry &&
            !last.isUser &&
            last.pending &&
            last.content.isEmpty
        ? _entries.length - 1
        : -1;
  }

  void _removePlaceholder() {
    final index = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
    );
    if (index < 0) return;
    final message = _entries[index] as MessageEntry;
    if (message.content.isEmpty) {
      _entries.removeAt(index);
    } else {
      _entries[index] = message.copyWith(pending: false);
    }
  }

  void _appendDelta(String delta) => appendAssistantDelta(_entries, delta);

  void _completeAssistant(String content, {String? id}) {
    final index = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
    );
    if (content.trim().isEmpty) {
      if (index >= 0) {
        final pending = _entries[index] as MessageEntry;
        if (pending.content.isEmpty) {
          _entries.removeAt(index);
        } else {
          _entries[index] = pending.copyWith(pending: false);
        }
      }
      _settleToolRuns();
      return;
    }
    final message = MessageEntry(role: 'assistant', content: content, id: id);
    if (index >= 0) {
      _entries[index] = message;
    } else {
      final last = _entries.isEmpty ? null : _entries.last;
      if (last is MessageEntry && !last.isUser && last.content == content) {
        if (last.id == null && id != null) {
          _entries[_entries.length - 1] = last.copyWith(id: id);
        }
        _settleToolRuns();
        return;
      }
      _entries.add(message);
    }
    _settleToolRuns();
  }

  void _toolEvent(String tool, {bool? success}) {
    final runIndex = _entries.lastIndexWhere((entry) => entry is ToolRunEntry);
    final lastUser = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && entry.isUser,
    );
    final lastApproval = _entries.lastIndexWhere(
      (entry) => entry is ApprovalEntry,
    );
    final current =
        runIndex > lastUser && runIndex > lastApproval && runIndex >= 0
        ? _entries[runIndex] as ToolRunEntry
        : null;
    final updated = success == null
        ? (current ?? const ToolRunEntry([])).started(tool)
        : (current ?? const ToolRunEntry([])).finished(tool, success: success);
    if (current != null) {
      _entries[runIndex] = updated;
    } else {
      final placeholder = _placeholderIndex;
      if (placeholder >= 0) {
        _entries.insert(placeholder, updated);
      } else {
        _entries.add(updated);
      }
    }
  }

  void _settleToolRuns() {
    for (var i = 0; i < _entries.length; i++) {
      final entry = _entries[i];
      if (entry is ToolRunEntry && entry.running) _entries[i] = entry.settle();
    }
  }

  void _settleSubmittingApprovals({ApprovalStatus? fallback}) {
    for (var i = 0; i < _entries.length; i++) {
      final entry = _entries[i];
      if (entry is! ApprovalEntry) continue;
      final resolved = resolveSubmittingApproval(entry, fallback: fallback);
      if (!identical(resolved, entry)) _entries[i] = resolved;
    }
  }

  void _addApprovals(Iterable<ApprovalEntry> approvals) {
    final placeholder = _placeholderIndex;
    if (placeholder >= 0) _entries.removeAt(placeholder);
    _settleToolRuns();
    for (final approval in approvals) {
      final existing = _entries.indexWhere(
        (entry) => entry is ApprovalEntry && entry.id == approval.id,
      );
      if (existing >= 0) {
        final current = _entries[existing] as ApprovalEntry;
        if (current.status == ApprovalStatus.submitting) {
          if (approval.retry) {
            _entries[existing] = current.copyWith(
              status: ApprovalStatus.failed,
              decision: approval.decision ?? current.decision,
            );
          }
          continue;
        }
        if (current.status != ApprovalStatus.pending &&
            approval.status == ApprovalStatus.pending &&
            !approval.retry) {
          continue;
        }
        _entries[existing] = approval;
      } else {
        _entries.add(approval);
      }
    }
  }

  Future<void> _syncConversationApprovals() async {
    final conversationId = _conversationId;
    if (conversationId == null) return;
    final approvals = await _loadConversationApprovals(conversationId);
    if (approvals == null ||
        !mounted ||
        _signedOut ||
        _signingOut ||
        _conversationId != conversationId) {
      return;
    }
    setState(() {
      _entries.removeWhere((entry) {
        if (entry is! ApprovalEntry) return false;
        if (entry.status != ApprovalStatus.pending &&
            entry.status != ApprovalStatus.failed) {
          return false;
        }
        return !approvals.any((approval) => approval.id == entry.id);
      });
      for (var i = 0; i < _entries.length; i++) {
        final entry = _entries[i];
        if (entry is! ApprovalEntry ||
            entry.status != ApprovalStatus.submitting) {
          continue;
        }
        if (approvals.any((approval) => approval.id == entry.id)) continue;
        _entries[i] = resolveSubmittingApproval(entry);
      }
      _addApprovals(approvals);
    });
  }
}
