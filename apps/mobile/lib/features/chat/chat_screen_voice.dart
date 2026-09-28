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
    if (conversationId == null || !_connected) {
      setState(() {
        _error = 'Connect to Jarvis before starting voice.';
        if (_selectedDestination == 2) _selectedDestination = 0;
      });
      return;
    }

    final voiceGeneration = ++_voiceGeneration;
    _voiceUserStop = false;
    setState(() {
      _voiceStarting = true;
      _voicePhase = 'connecting';
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
      final sessionResponse = await _http.post<Map<String, dynamic>>(
        '/api/v1/voice/session',
        data: {'conversationId': conversationId},
      );
      if (!isCurrent()) return;
      final session = sessionResponse.data;
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
    } catch (error) {
      await _abandonVoiceRoom(room);
      if (isCurrent()) setState(() => _error = 'Could not start voice: $error');
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
        final userStop = _voiceUserStop;
        unawaited(_stopVoice(leave: userStop));
        if (mounted && !userStop) {
          setState(
            () => _error ??=
                'The voice session ended. Start it again to continue.',
          );
        }
      })
      ..on<ParticipantAttributesChanged>((event) {
        if (!identical(_voiceRoom, room) || !mounted) return;
        final next = event.participant.attributes['jarvis.voice.phase'];
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
        _voiceCaption = null;
        _voiceCaptionRole = null;
        if (leave && _selectedDestination == 2) _selectedDestination = 0;
      });
    }
  }
}
