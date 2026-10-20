import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/generative_ui.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

import 'fixtures.dart';
import 'harness.dart';

/// Every kind of card Jarvis can render in chat, light, dark and at the
/// largest text on the smallest phone.
/// Run: flutter test test/screenshots/generative_cards.dart
void main() {
  setUpAll(loadAppFonts);
  setUp(mockPlatformChannels);

  final cards = <String, UiSurfaceEntry Function()>{
    'custom': () => UiSurfaceEntry.fromJson(weekSurface())!,
    'compare': () => UiSurfaceEntry.fromJson(laptopSurface())!,
    'form': () => UiSurfaceEntry.fromJson(planSurface())!,
    'multi': () => UiSurfaceEntry.fromJson(daysSurface())!,
    'list': () => UiSurfaceEntry.fromJson(billsSurface())!,
    'question': () => UiSurfaceEntry.fromJson(questionSurface())!,
    'answered': () => UiSurfaceEntry.fromJson({
      ...questionSurface(),
      'status': 'completed',
      'answer': {
        'actionId': 'hard',
        'values': {'choice': 'hard', 'label': 'Hard push'},
      },
    })!,
  };

  for (final (name, size, scale, dark) in [
    ('light', const Size(393, 852), 1.0, false),
    ('dark', const Size(393, 852), 1.0, true),
    ('large-text', const Size(320, 640), 3.0, false),
  ]) {
    for (final card in cards.entries) {
      screenshotTest('${card.key} card on $name', (tester) async {
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
                body: SafeArea(
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
                    children: [
                      ContentWidth(
                        maxWidth: 760,
                        child: UiSurfaceCard(
                          surface: card.value(),
                          onAction: (_, _) async {},
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        );
        for (var i = 0; i < 10; i++) {
          await tester.pump(const Duration(milliseconds: 100));
        }
        expect(tester.takeException(), isNull);
        await capture(tester, 'card-${card.key}-$name');
      });
    }
  }
}
