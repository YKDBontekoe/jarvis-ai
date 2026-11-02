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

  testWidgets('End stays on screen while long captions scroll above it', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MediaQuery(
        data: const MediaQueryData(
          size: Size(360, 640),
          textScaler: TextScaler.linear(2.5),
        ),
        child: _host(
          VoiceStage(
            phase: 'speaking',
            voiceName: 'Cove',
            handsFree: true,
            captions: true,
            caption: List.filled(
              40,
              'A long answer that keeps going.',
            ).join(' '),
            captionRole: 'assistant',
            canStart: true,
            onPrimary: () {},
            onToggleMute: () {},
          ),
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 300));

    for (final key in ['voice-primary', 'voice-mute']) {
      final rect = tester.getRect(find.byKey(Key(key)));
      expect(rect.bottom, lessThanOrEqualTo(640), reason: '$key is off screen');
      expect(rect.top, greaterThanOrEqualTo(0));
    }
  });

  testWidgets('captions can be hidden and shown during a call', (tester) async {
    var captions = true;
    late StateSetter update;
    await tester.pumpWidget(
      _host(
        StatefulBuilder(
          builder: (context, setState) {
            update = setState;
            return VoiceStage(
              phase: 'listening',
              voiceName: 'Cove',
              handsFree: true,
              captions: captions,
              caption: 'Hello there',
              captionRole: 'assistant',
              canStart: true,
              onPrimary: () {},
              onToggleCaptions: () => update(() => captions = !captions),
            );
          },
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.text('Hello there'), findsOneWidget);

    await tester.tap(find.byKey(const Key('voice-captions')));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.text('Hello there'), findsNothing);
    expect(find.text('Show captions'), findsOneWidget);
  });
}
