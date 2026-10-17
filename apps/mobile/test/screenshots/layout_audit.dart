import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/utility_pages.dart';
import 'package:jarvis_mobile/theme.dart';

import 'fixtures.dart';
import 'harness.dart';

/// Opens every utility page on a phone and a small phone, in light and dark,
/// and at the largest text size, and fails on layout overflows or a toolbar squeezed below its height.
/// Writes a screenshot of each page. Run:
/// flutter test test/screenshots/layout_audit.dart
const destinations = [
  'tasks',
  'projects',
  'project:p2',
  'comparisons',
  'comparison:c1',
  'meetings',
  'meeting:m1',
  'goals',
  'scenarios',
  'opportunities',
  'documents',
  'document:d1',
  'setup',
  'notification-settings',
  'stories',
  'story:s1',
  'lesson:i1',
  'find-app',
  'goal:g1',
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

  // "large-text" is the biggest accessibility text size on a small phone.
  for (final (name, size, textScale) in [
    ('phone', const Size(393, 852), 1.0),
    ('small', const Size(320, 640), 1.0),
    ('large-text', const Size(320, 640), 3.0),
    ('dark', const Size(393, 852), 1.0),
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
              builder: (context, child) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: TextScaler.linear(textScale)),
                child: child!,
              ),
              theme: buildJarvisTheme(
                brightness: name == 'dark' ? Brightness.dark : Brightness.light,
              ),
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
