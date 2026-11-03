import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/main.dart';

import 'screenshots/fixtures.dart';
import 'screenshots/harness.dart';

/// Runs Flutter's accessibility guidelines on the main screens with sample
/// data, in light and dark: text contrast (WCAG AA), 44pt tap targets (the
/// playbook's size; the dock tabs are 44 wide, under Android's 48dp) and
/// labelled tap targets for screen readers.
/// Part of the normal `flutter test` run.
void main() {
  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    debugJarvisHttpAdapter = ScreenshotHttp(fixtureRoutes(withApproval: true));
  });
  tearDown(() => debugJarvisHttpAdapter = null);

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 30; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  const guidelines = <String, AccessibilityGuideline>{
    'contrast': textContrastGuideline,
    'ios tap target': iOSTapTargetGuideline,
    'labelled tap target': labeledTapTargetGuideline,
  };

  final screens = <String, Future<void> Function(WidgetTester)>{
    'home': (tester) async {},
    'chats': (tester) => tester.tap(find.byKey(const Key('tab-chats'))),
    'everything': (tester) =>
        tester.tap(find.byKey(const Key('tab-everything'))),
    'you': (tester) => tester.tap(find.byKey(const Key('tab-you'))),
    'chat': (tester) => tester.tap(find.byKey(const Key('tab-jarvis'))),
  };

  for (final brightness in Brightness.values) {
    for (final screen in screens.entries) {
      testWidgets('${brightness.name} ${screen.key} meets accessibility '
          'guidelines', (tester) async {
        usePhone(tester);
        tester.platformDispatcher.platformBrightnessTestValue = brightness;
        addTearDown(tester.platformDispatcher.clearAllTestValues);
        final semantics = tester.ensureSemantics();
        await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
        await settle(tester);
        await screen.value(tester);
        await settle(tester);
        final failures = <String>[];
        for (final guideline in guidelines.entries) {
          final result = await guideline.value.evaluate(tester);
          if (!result.passed) {
            failures.add('[${guideline.key}] ${result.reason}');
          }
        }
        semantics.dispose();
        expect(failures, isEmpty, reason: failures.join('\n'));
      });
    }
  }
}
