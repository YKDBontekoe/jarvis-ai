part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenRealtime on _ChatScreenController {
  Map<Object?, Object?>? _payload(List<Object?>? arguments) {
    if (arguments == null || arguments.isEmpty) return null;
    final value = arguments.first;
    return value is Map<Object?, Object?> ? value : null;
  }

  bool _hubIsCurrent(
    HubConnection hub,
    String conversationId,
    int generation,
  ) =>
      mounted &&
      !_signedOut &&
      identical(_hub, hub) &&
      _conversationId == conversationId &&
      _realtimeGeneration == generation;

  void _onHub(
    HubConnection hub,
    String event,
    void Function(List<Object?>? arguments) handler,
  ) {
    hub.on(event, (arguments) {
      try {
        // Apply buffered text first so events keep their order.
        if (event != 'message.delta') _flushDeltas();
        handler(arguments);
      } catch (error, stack) {
        reportError(error, stack, context: 'realtime $event');
      }
    });
  }

  void _flushDeltas() {
    _deltaTimer?.cancel();
    _deltaTimer = null;
    if (_deltaBuffer.isEmpty) return;
    final text = _deltaBuffer.toString();
    _deltaBuffer.clear();
    if (!mounted) return;
    final last = _entries.isEmpty ? null : _entries.last;
    if (!_showHome && last is MessageEntry && !last.isUser && last.pending) {
      // Streaming into the visible reply: rebuild only the transcript.
      _appendDelta(text);
      _transcriptTick.value++;
    } else {
      setState(() {
        _showHome = false;
        _appendDelta(text);
      });
    }
    _scrollToBottom(jump: true);
  }

  void _discardDeltas() {
    _deltaTimer?.cancel();
    _deltaTimer = null;
    _deltaBuffer.clear();
  }

  Future<void> _connectRealtime([int? generation]) async {
    final expectedGeneration = generation ?? _realtimeGeneration;
    final conversationId = _conversationId;
    if (conversationId == null) return;
    final hub = HubConnectionBuilder()
        .withUrl(
          '$apiBaseUrl/hubs/events',
          options: HttpConnectionOptions(
            accessTokenFactory: () async {
              final token = await _auth.accessToken();
              if (_auth.enabled && (token == null || token.isEmpty)) {
                throw StateError('Missing access token for realtime updates.');
              }
              return token ?? '';
            },
          ),
        )
        .withAutomaticReconnect(reconnectPolicy: _RealtimeRetryPolicy())
        .build();
    _onHub(hub, 'message.delta', (arguments) {
      final event = _payload(arguments);
      final delta = asJsonString(event?['delta']) ?? '';
      if (delta.isEmpty ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      _deltaBuffer.write(delta);
      _deltaTimer ??= Timer(const Duration(milliseconds: 60), _flushDeltas);
    });
    _onHub(hub, 'message.completed', (arguments) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      final payload = _payload(arguments);
      final content = asJsonString(payload?['content']) ?? '';
      setState(() {
        _completeAssistant(
          content,
          id: asJsonString(payload?['id']),
          citations: parseMessageCitations(payload?['citations']),
        );
        if (content.trim().isNotEmpty) {
          _settleSubmittingApprovals();
        }
        _finishRemoteQuery();
      });
      _scrollToBottom();
    });
    _onHub(hub, 'tool.started', (arguments) {
      final tool = asJsonString(_payload(arguments)?['tool']);
      if (tool == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() => _toolEvent(tool));
      _scrollToBottom();
    });
    _onHub(hub, 'tool.completed', (arguments) {
      final tool = asJsonString(_payload(arguments)?['tool']);
      if (tool == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _toolEvent(tool, success: true);
        _settleSubmittingApprovals();
      });
    });
    _onHub(hub, 'tool.failed', (arguments) {
      final tool = asJsonString(_payload(arguments)?['tool']);
      if (tool == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _toolEvent(tool, success: false);
        _settleSubmittingApprovals();
      });
    });
    _onHub(hub, 'tool.approval_required', (arguments) {
      final approval = ApprovalEntry.fromJson(_payload(arguments));
      if (approval == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _showHome = false;
        _addApprovals([approval]);
        _finishRemoteQuery();
      });
      _scrollToBottom();
    });
    _onHub(hub, 'notification.created', (arguments) {
      final event = _payload(arguments);
      if (event == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() => _unreadNotifications++);
      if (event['type'] == 'task.completed' ||
          event['type'] == 'task.failed' ||
          event['type'] == 'approval.required') {
        setState(() => _homeRevision++);
      }
      if (event['type'] != 'approval.required') return;
      final notificationId = asJsonString(event['notificationId']);
      final approvalId = asJsonString(event['sourceId']);
      final inline = _entries.any(
        (entry) => entry is ApprovalEntry && entry.id == approvalId,
      );
      if (inline && _selectedDestination == 0 && !_showHome) return;
      if (notificationId != null &&
          !_shownPushNotifications.add(notificationId)) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            asJsonString(event['body']) ?? 'Jarvis needs your approval.',
          ),
          action: SnackBarAction(
            label: 'Review',
            onPressed: () => _openUtility('approvals'),
          ),
        ),
      );
    });
    _onHub(hub, 'agent.completed', (_) {
      if (_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        setState(() {
          _settleToolRuns();
          _settleSubmittingApprovals();
        });
      }
    });
    _onHub(hub, 'agent.failed', (arguments) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      final event = _payload(arguments);
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        _settleSubmittingApprovals(fallback: ApprovalStatus.failed);
        _finishRemoteQuery();
        _error =
            asJsonString(event?['message']) ??
            'Jarvis could not complete this response.';
      });
    });
    _onHub(hub, 'voice.transcript', (arguments) {
      final transcript = asJsonString(_payload(arguments)?['text']) ?? '';
      if (transcript.isEmpty ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _showHome = false;
        final last = _entries.isEmpty ? null : _entries.last;
        final duplicate =
            last is MessageEntry && last.isUser && last.content == transcript;
        if (!duplicate) {
          _entries.add(MessageEntry(role: 'user', content: transcript));
        }
      });
      _scrollToBottom();
    });
    _onHub(hub, 'voice.failed', (arguments) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      final event = _payload(arguments);
      setState(
        () =>
            _error = asJsonString(event?['message']) ?? 'Voice session failed.',
      );
      unawaited(_stopVoice());
    });
    _onHub(hub, 'ui.surface', (arguments) {
      final surface = UiSurfaceEntry.fromJson(_payload(arguments));
      if (surface == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _showHome = false;
        _upsertSurface(surface);
      });
      _scrollToBottom();
    });
    _onHub(hub, 'browser.session', (arguments) {
      final event = _payload(arguments);
      final id = asJsonString(event?['id']);
      final goal = asJsonString(event?['goal']);
      if (id == null ||
          goal == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _showHome = false;
        _upsertBrowserSession(
          BrowserSessionEntry(id: id, goal: goal, steps: const []),
        );
      });
      _scrollToBottom();
    });
    _onHub(hub, 'browser.step', (arguments) {
      final event = _payload(arguments);
      final sessionId = asJsonString(event?['sessionId']);
      final summary = asJsonString(event?['summary']);
      if (sessionId == null ||
          summary == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _appendBrowserStep(
          sessionId,
          BrowserStepItem(
            tool: asJsonString(event?['tool']) ?? 'browser',
            summary: summary,
            success: event?['success'] != false,
          ),
        );
      });
      _scrollToBottom();
    });
    _onHub(hub, 'device.invoke', (arguments) {
      final event = _payload(arguments);
      final invokeId = asJsonString(event?['invokeId']);
      final capability = asJsonString(event?['capability']);
      if (invokeId == null ||
          capability == null ||
          !mounted ||
          _signedOut ||
          !identical(_hub, hub)) {
        return;
      }
      unawaited(_handleDeviceInvoke(invokeId, capability, event));
    });
    _onHub(hub, 'voice.caption', (arguments) {
      final event = _payload(arguments);
      final text = asJsonString(event?['text']) ?? '';
      if (!_voiceCaptions ||
          text.isEmpty ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      final role = asJsonString(event?['role']) ?? 'user';
      final captionFinal = asJsonBool(event?['final']);
      setState(() {
        _voiceCaption = text;
        _voiceCaptionRole = role;
        if (!_voiceActive && !_voiceStarting) return;
        if (role == 'assistant') {
          _voicePhase = 'speaking';
        } else if (captionFinal) {
          _voicePhase = 'thinking';
        } else if (_voicePhase != 'speaking') {
          _voicePhase = 'listening';
        }
      });
    });
    hub.onreconnecting(({error}) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      // Keep voice and the streamed text alive through a blip; onclose and
      // the catch-up after reconnecting clean up if the run really ended.
      setState(() => _connected = false);
    });
    hub.onreconnected(({connectionId}) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      unawaited(_restoreRealtime(hub, conversationId));
    });
    hub.onclose(({error}) {
      if (!identical(_hub, hub)) return;
      unawaited(_stopVoice());
      if (mounted) {
        setState(() {
          _connected = false;
          _selectedDestination = _selectedDestination == 2
              ? 0
              : _selectedDestination;
          if (!_remoteQuery) {
            _removePlaceholder();
            _settleToolRuns();
            _error ??= 'Realtime updates disconnected. Retry the connection.';
          }
        });
      }
    });
    if (_conversationId != conversationId ||
        _realtimeGeneration != expectedGeneration ||
        _signedOut) {
      await hub.stop();
      return;
    }
    _hub = hub;
    try {
      await hub.start();
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        if (identical(_hub, hub)) _hub = null;
        await hub.stop();
        return;
      }
      await hub.invoke('JoinConversation', args: [conversationId]);
      try {
        await hub.invoke(
          'RegisterDevice',
          args: [
            'Jarvis app',
            ['battery', 'open_url', 'notify', 'clipboard', 'location'],
          ],
        );
        unawaited(postDeviceTelemetry(_http));
      } catch (_) {
        // Older servers without device nodes still stream chat.
      }
      if (_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        setState(() => _connected = true);
      }
    } catch (_) {
      if (identical(_hub, hub)) _hub = null;
      await hub.stop();
      rethrow;
    }
  }

  Future<void> _restoreRealtime(
    HubConnection hub,
    String conversationId,
  ) async {
    final generation = _realtimeGeneration;
    try {
      await hub.invoke('JoinConversation', args: [conversationId]);
      try {
        await hub.invoke(
          'RegisterDevice',
          args: [
            'Jarvis app',
            ['battery', 'open_url', 'notify', 'clipboard', 'location'],
          ],
        );
        unawaited(postDeviceTelemetry(_http));
      } catch (_) {
        // Older servers without device nodes still stream chat.
      }
      if (mounted &&
          _hub == hub &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(() {
          _connected = true;
          _homeRevision++;
        });
        if (_remoteQuery) {
          unawaited(_catchUpRemoteQuery(conversationId));
        } else {
          await _reloadConversationEntries(conversationId, generation);
        }
      }
    } catch (_) {
      if (mounted &&
          _hub == hub &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(() {
          _connected = false;
          _error = 'Could not restore realtime updates. Retry the connection.';
        });
      }
    }
  }
}

class _RealtimeRetryPolicy implements IRetryPolicy {
  @override
  int? nextRetryDelayInMilliseconds(RetryContext retryContext) =>
      realtimeReconnectDelay(retryContext.previousRetryCount).inMilliseconds;
}
