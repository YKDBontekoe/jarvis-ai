import 'dart:async' as async;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/voice/voice_errors.dart';
import 'package:jarvis_mobile/features/voice/voice_stage.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:livekit_client/livekit_client.dart' as lk;

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: child),
);

void main() {
  group('describeVoiceStartError', () {
    test('points to microphone settings when permission is denied', () {
      final message = describeVoiceStartError(
        lk.TrackCreateException('Microphone permission is not granted'),
      );
      expect(message, contains('microphone access'));
    });

    test('blames the connection for timeouts and connect failures', () {
      for (final error in <Object>[
        async.TimeoutException('slow'),
        lk.MediaConnectException('ice failed'),
      ]) {
        expect(describeVoiceStartError(error), contains('voice server'));
      }
    });

    test('never shows raw exception text', () {
      final message = describeVoiceStartError(StateError('boom 0x42'));
      expect(message, isNot(contains('boom')));
    });
  });

  testWidgets('voice stage says it is reconnecting and can still end', (
    tester,
  ) async {
    var ended = false;
    await tester.pumpWidget(
      _host(
        VoiceStage(
          phase: 'reconnecting',
          voiceName: 'Cove',
          handsFree: true,
          captions: true,
          canStart: true,
          onPrimary: () => ended = true,
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 300));

    expect(find.text('Reconnecting'), findsOneWidget);
    expect(find.textContaining('reconnects'), findsOneWidget);
    expect(find.text('End voice chat'), findsOneWidget);
    await tester.tap(find.byKey(const Key('voice-primary')));
    expect(ended, isTrue);
  });
}
