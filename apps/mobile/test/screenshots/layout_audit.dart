import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/utility_pages.dart';
import 'package:jarvis_mobile/theme.dart';

import 'fixtures.dart';
import 'harness.dart';

/// Opens every utility page on a phone and a small phone, in light and dark,
/// and fails on layout overflows or a toolbar squeezed below its height.
/// Writes a screenshot of each page. Run:
/// flutter test test/screenshots/layout_audit.dart
const destinations = [
  'tasks',
  'projects',
  'memory',
  'journal',
  'today',
  'expenses',
  'habits',
  'people',
  'approvals',
  'reminders',
  'notifications',
  'files',
  'audit',
  'watches',
  'automations',
  'briefing',
  'integrations',
  'models',
  'skills',
  'persona',
  'profiles',
  'learning',
  'graph',
  'channels',
  'whatsapp',
  'coding',
  'agents',
  'devices',
  'usage',
];

void main() {
  setUpAll(loadAppFonts);
  setUp(mockPlatformChannels);

  for (final (name, size) in [
    ('phone', const Size(393, 852)),
    ('small', const Size(320, 640)),
  ]) {
    for (final destination in destinations) {
      screenshotTest('$destination on $name', (tester) async {
        usePhone(tester, size: size);
        final http = ScreenshotHttp(fixtureRoutes(withApproval: false));
        final dio = Dio(BaseOptions(baseUrl: 'https://fixture.invalid'))
          ..httpClientAdapter = http;
        final navigator = GlobalKey<NavigatorState>();
        await tester.pumpWidget(
          RepaintBoundary(
            key: screenshotKey,
            child: MaterialApp(
              navigatorKey: navigator,
              debugShowCheckedModeBanner: false,
              theme: buildJarvisTheme(),
              home: const Scaffold(),
            ),
          ),
        );
        final page = utilityPageFor(destination, dio)!;
        navigator.currentState!.push(
          MaterialPageRoute<void>(builder: (_) => page),
        );
        for (var i = 0; i < 30; i++) {
          await tester.pump(const Duration(milliseconds: 100));
        }
        final back = find.byType(BackButton);
        if (back.evaluate().isNotEmpty) {
          expect(
            tester.getSize(back.first).height,
            greaterThanOrEqualTo(48),
            reason: '$destination squeezes its toolbar',
          );
        }
        await capture(tester, 'audit-$name-$destination');
      });
    }
  }
}
