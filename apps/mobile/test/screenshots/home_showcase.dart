import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/main.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'fixtures.dart';
import 'harness.dart';

/// Renders Home with live tiles and the ways to use them into
/// build/screenshots (or $SCREENSHOT_DIR).
/// Run: flutter test test/screenshots/home_showcase.dart
void main() {
  late ScreenshotHttp http;

  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    http = ScreenshotHttp(fixtureRoutes());
    // Habits with one still open, and a reminder to finish.
    http.routes['GET /api/v1/habits'] = habits();
    debugJarvisHttpAdapter = http;
  });
  tearDown(() => debugJarvisHttpAdapter = null);

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 30; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  void layout(List<(String, String)> tiles) {
    SharedPreferences.setMockInitialValues({
      'home.tiles.v1': jsonEncode([
        for (final (id, size) in tiles) {'id': id, 'size': size},
      ]),
    });
  }

  Future<void> start(WidgetTester tester, {bool dark = false}) async {
    tester.platformDispatcher.platformBrightnessTestValue = dark
        ? Brightness.dark
        : Brightness.light;
    addTearDown(tester.platformDispatcher.clearPlatformBrightnessTestValue);
    await tester.pumpWidget(
      RepaintBoundary(
        key: screenshotKey,
        child: const JarvisApp(skipAuthentication: true),
      ),
    );
    await settle(tester);
  }

  const live = [
    ('today', 'wide'),
    ('reminders', 'square'),
    ('habits', 'square'),
    ('expenses', 'square'),
    ('approvals', 'square'),
    ('devices', 'square'),
    ('chats', 'wide'),
    ('voice', 'icon'),
    ('memory', 'icon'),
    ('journal', 'icon'),
    ('people', 'icon'),
  ];

  for (final dark in [false, true]) {
    final mode = dark ? 'dark' : 'light';

    screenshotTest('live tiles $mode', (tester) async {
      usePhone(tester, size: const Size(393, 1500));
      layout(live);
      await start(tester, dark: dark);
      await capture(tester, '40-live-tiles-$mode');
    });
  }

  screenshotTest('check in a habit', (tester) async {
    usePhone(tester);
    layout(const [('habits', 'square'), ('reminders', 'square')]);
    await start(tester);
    await capture(tester, '41-habit-before');
    await tester.tap(find.byKey(const Key('tile-action-check')));
    await tester.pump(const Duration(milliseconds: 120));
    await capture(tester, '42-habit-checking');
    await settle(tester);
    await capture(tester, '43-habit-done');
  });

  screenshotTest('long-press menu', (tester) async {
    usePhone(tester);
    layout(const [('reminders', 'square'), ('expenses', 'square')]);
    await start(tester);
    await tester.longPress(find.text('Reminders'));
    await settle(tester);
    await capture(tester, '44-tile-menu');
  });

  screenshotTest('spending bar touched', (tester) async {
    usePhone(tester);
    layout(const [('expenses', 'square'), ('devices', 'square')]);
    await start(tester);
    await tester.tap(find.byKey(const Key('bar-5')));
    await settle(tester);
    await capture(tester, '45-bar-touched');
  });

  screenshotTest('timeline touched', (tester) async {
    usePhone(tester);
    layout(const [('today', 'wide'), ('reminders', 'wide')]);
    await start(tester);
    await tester.tap(find.byKey(const Key('span-0')));
    await settle(tester);
    await capture(tester, '46-timeline-touched');
  });

  screenshotTest('tile placeholders', (tester) async {
    usePhone(tester);
    layout(const [
      ('today', 'wide'),
      ('habits', 'square'),
      ('expenses', 'square'),
    ]);
    tester.platformDispatcher.platformBrightnessTestValue = Brightness.light;
    addTearDown(tester.platformDispatcher.clearPlatformBrightnessTestValue);
    await tester.pumpWidget(
      RepaintBoundary(
        key: screenshotKey,
        child: const JarvisApp(skipAuthentication: true),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 30));
    await capture(tester, '47-placeholders');
    await settle(tester);
  });
}
