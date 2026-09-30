import 'dart:async' as async;

import 'package:livekit_client/livekit_client.dart';

/// Identity the Jarvis voice runtime uses in every LiveKit room.
const jarvisVoiceIdentity = 'jarvis-voice';

/// Plain-language reason a voice session could not start. Raw exception text
/// (timeouts, platform codes) never reaches the screen.
String describeVoiceStartError(Object error) {
  final text = error.toString().toLowerCase();
  if (text.contains('permission') ||
      text.contains('notallowed') ||
      text.contains('not allowed')) {
    return 'Jarvis needs microphone access for voice. Allow the microphone '
        'for Jarvis in your phone settings, then try again.';
  }
  if (error is AudioSessionException) {
    return 'Your phone would not let Jarvis use the microphone right now. '
        'End any call or other recording app, then try again.';
  }
  if (error is async.TimeoutException ||
      error is TimeoutException ||
      error is ConnectException ||
      error is MediaConnectException) {
    return 'Could not reach the voice server. Check your connection and try '
        'again.';
  }
  return 'Voice could not start. Try again in a moment.';
}

/// Message shown when Jarvis leaves an active voice session on its own.
const voiceEndedByJarvis =
    'Jarvis ended the voice session. Start it again to keep talking.';

/// Message shown when the phone lost the voice session and could not rejoin.
const voiceConnectionLost =
    'The voice connection dropped. Start it again to keep talking.';
