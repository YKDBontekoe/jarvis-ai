import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/voice/voice_stage.dart';
import 'package:jarvis_mobile/theme.dart';

import 'harness.dart';

/// The voice stage with a companion card, light, dark and at the largest text.
/// Run: flutter test test/screenshots/voice_cards.dart
void main() {
  setUpAll(loadAppFonts);
  setUp(mockPlatformChannels);

  for (final (name, size, scale, dark) in [
    ('phone', const Size(393, 852), 1.0, false),
    ('dark', const Size(393, 852), 1.0, true),
    ('large-text', const Size(320, 640), 3.0, false),
  ]) {
    screenshotTest('voice card on $name', (tester) async {
      usePhone(tester, size: size);
      await tester.pumpWidget(
        RepaintBoundary(
          key: screenshotKey,
          child: MaterialApp(
            debugShowCheckedModeBanner: false,
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(scale)),
              child: child!,
            ),
            theme: buildJarvisTheme(
              brightness: dark ? Brightness.dark : Brightness.light,
            ),
            home: Scaffold(
              body: VoiceStage(
                phase: 'listening',
                voiceName: 'Juniper',
                handsFree: true,
                captions: true,
                caption: 'Three things need you today. The first is the kitchen sync.',
                captionRole: 'assistant',
                canStart: true,
                onPrimary: () {},
                cards: const [
                  {
                    'id': 'c1',
                    'kind': 'needs_you',
                    'title': 'What needs you',
                    'state': 'shown',
                    'items': [
                      {
                        'title': 'Prepare for “Kitchen sync”',
                        'detail': 'It starts in about 2 hours and has no prep note yet.',
                        'destination': 'meeting:m1',
                      },
                      {
                        'title': 'Review Netflix before it renews',
                        'detail': 'Decide by 20 Oct.',
                        'destination': 'subscription:s1',
                      },
                    ],
                  },
                ],
              ),
            ),
          ),
        ),
      );
      for (var i = 0; i < 6; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await capture(tester, 'voice-card-$name');
    });
  }
}
