part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenVoice on _ChatScreenController {
  Future<void> _toggleVoice() async {
    if (_voiceActive || _voiceStarting) {
      await _stopVoice();
      return;
    }
    if (_sending || _busy || _hasPendingApproval || _signedOut || _signingOut) {
      return;
    }
    final conversationId = _conversationId;
    // Audio runs over LiveKit, so voice can start even while live updates
    // (used only for captions) are reconnecting.
    if (conversationId == null) {
      setState(() {
        _error = 'Open a conversation before starting voice.';
        if (_selectedDestination == 2) _selectedDestination = 0;
      });
      return;
    }

    final voiceGeneration = ++_voiceGeneration;
    _voiceUserStop = false;
    setState(() {
      _voiceStarting = true;
      _voicePhase = 'connecting';
      _voiceReconnecting = false;
      _voiceCaption = null;
      _voiceCaptionRole = null;
      _selectedDestination = 2;
      _error = null;
    });
    Room? room;
    final generation = _realtimeGeneration;
    bool isCurrent() =>
        mounted &&
        voiceGeneration == _voiceGeneration &&
        !_signedOut &&
        !_signingOut &&
        _conversationId == conversationId &&
        _realtimeGeneration == generation;
    try {
      final sessionResponse = await _http.post<dynamic>(
        '/api/v1/voice/session',
        data: {'conversationId': conversationId},
        // Codex realtime can take up to a minute to answer; the default
        // receive timeout would abandon a session the server is still building.
        options: Options(receiveTimeout: const Duration(seconds: 75)),
      );
      if (!isCurrent()) return;
      final session = jsonObject(sessionResponse.data);
      if (session == null) {
        throw StateError('Jarvis returned no voice session.');
      }
      final serverUrl = asJsonString(session['serverUrl']);
      final token = asJsonString(session['token']);
      if (serverUrl == null || token == null) {
        throw StateError('Jarvis returned incomplete LiveKit credentials.');
      }
      _voiceHandsFree = session['handsFree'] != false;
      _voiceCaptions = session['captions'] != false;
      _voiceName = asJsonString(session['voice']) ?? '';
      _voiceMuted = !_voiceHandsFree;
      if (!_voiceCaptions) {
        _voiceCaption = null;
        _voiceCaptionRole = null;
      }

      await AudioManager.instance.setSpeakerOutputPreferred(true);
      if (!isCurrent()) return;
      room = Room();
      _voiceRoom = room;
      _listenToVoiceRoom(room);
      await room.connect(serverUrl, token).timeout(const Duration(seconds: 20));
      if (!isCurrent() || _voiceRoom != room) {
        await _abandonVoiceRoom(room);
        return;
      }
      await room.localParticipant?.setMicrophoneEnabled(
        _voiceHandsFree,
        audioCaptureOptions: _voiceCapture,
      );
      if (!isCurrent() || _voiceRoom != room) {
        await _abandonVoiceRoom(room);
        return;
      }
      setState(() {
        _voiceActive = true;
        _voicePhase = 'listening';
        _selectedDestination = 2;
      });
    } on DioException catch (error) {
      if (isCurrent()) setState(() => _error = describeApiError(error));
      await _abandonVoiceRoom(room);
    } catch (error, stack) {
      await _abandonVoiceRoom(room);
      if (isCurrent()) {
        reportError(error, stack, context: 'voice start');
        setState(() => _error = describeVoiceStartError(error));
      }
    } finally {
      if (mounted && voiceGeneration == _voiceGeneration) {
        setState(() {
          _voiceStarting = false;
          if (!_voiceActive && _selectedDestination == 2) {
            _selectedDestination = 0;
          }
        });
      }
    }
  }

  Future<void> _abandonVoiceRoom(Room? room) async {
    if (room == null) return;
    final owned = identical(_voiceRoom, room);
    if (owned) {
      _voiceEvents?.dispose();
      _voiceEvents = null;
      _voiceRoom = null;
    } else {
      return;
    }
    try {
      await room.disconnect();
      await room.dispose();
    } catch (_) {}
    try {
      await AudioManager.instance.setSpeakerOutputPreferred(false);
    } catch (_) {}
  }

  void _listenToVoiceRoom(Room room) {
    _voiceEvents?.dispose();
    final listener = room.createListener();
    _voiceEvents = listener;
    listener
      ..on<RoomDisconnectedEvent>((event) {
        if (!identical(_voiceRoom, room)) return;
        _endVoiceWith(_voiceUserStop ? null : voiceConnectionLost);
      })
      // LiveKit retries on its own after a network change; say so instead of
      // pretending to listen while no audio gets through.
      ..on<RoomReconnectingEvent>((_) {
        if (!identical(_voiceRoom, room) || !mounted) return;
        setState(() => _voiceReconnecting = true);
      })
      ..on<RoomReconnectedEvent>((_) {
        if (!identical(_voiceRoom, room) || !mounted) return;
        setState(() => _voiceReconnecting = false);
      })
      // Without this the screen kept saying "Listening" after Jarvis had
      // already left the room. A full reconnect also reports every
      // participant as gone, so ignore departures while reconnecting.
      ..on<ParticipantDisconnectedEvent>((event) {
        if (!identical(_voiceRoom, room) ||
            _voiceReconnecting ||
            event.participant.identity != jarvisVoiceIdentity) {
          return;
        }
        _endVoiceWith(voiceEndedByJarvis);
      })
      ..on<ParticipantAttributesChanged>((event) {
        if (!identical(_voiceRoom, room) || !mounted) return;
        final attributes = event.participant.attributes;
        if (attributes['jarvis.voice.status'] == 'unavailable') {
          _endVoiceWith(voiceEndedByJarvis);
          return;
        }
        final next = attributes['jarvis.voice.phase'];
        if (next == null || next.isEmpty) return;
        setState(() => _voicePhase = next);
      })
      ..on<ActiveSpeakersChangedEvent>((event) {
        if (!identical(_voiceRoom, room) || !mounted) return;
        final localSid = room.localParticipant?.sid;
        final remoteSpeaking = event.speakers.any(
          (speaker) => speaker.sid != localSid,
        );
        if (remoteSpeaking) setState(() => _voicePhase = 'speaking');
      });
  }

  /// Ends the session but keeps the voice page open so [message] stays
  /// visible next to the start button.
  void _endVoiceWith(String? message) {
    unawaited(_stopVoice(leave: message == null));
    if (mounted && message != null) setState(() => _error ??= message);
  }

  Future<void> _toggleVoiceMute() async {
    final room = _voiceRoom;
    if (room == null || !_voiceActive) return;
    final muted = !_voiceMuted;
    try {
      await room.localParticipant?.setMicrophoneEnabled(
        !muted,
        audioCaptureOptions: _voiceCapture,
      );
      if (mounted && identical(_voiceRoom, room)) {
        setState(() => _voiceMuted = muted);
      }
    } catch (_) {}
  }

  Future<void> _stopVoice({bool leave = true}) async {
    _voiceUserStop = true;
    _voiceGeneration++;
    _voiceEvents?.dispose();
    _voiceEvents = null;
    final room = _voiceRoom;
    _voiceRoom = null;
    try {
      await room?.localParticipant?.setMicrophoneEnabled(false);
      await room?.disconnect();
      await room?.dispose();
    } catch (_) {}
    try {
      await AudioManager.instance.setSpeakerOutputPreferred(false);
    } catch (_) {}
    if (mounted &&
        (_voiceActive || _voiceStarting || _selectedDestination == 2)) {
      setState(() {
        _voiceActive = false;
        _voiceStarting = false;
        _voicePhase = 'idle';
        _voiceMuted = false;
        _voiceReconnecting = false;
        _voiceCaption = null;
        _voiceCaptionRole = null;
        if (leave && _selectedDestination == 2) _selectedDestination = 0;
      });
    }
  }
}
