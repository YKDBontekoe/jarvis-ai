import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/main.dart';

import 'fixtures.dart';
import 'harness.dart';

/// Renders the main screens with sample data into build/screenshots (or
/// $SCREENSHOT_DIR). Run: flutter test test/screenshots/app_screenshots.dart
void main() {
  late ScreenshotHttp http;

  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    http = ScreenshotHttp(fixtureRoutes(withApproval: false));
    debugJarvisHttpAdapter = http;
  });
  tearDown(() {
    debugJarvisHttpAdapter = null;
    // ignore: avoid_print
    if (http.missing.isNotEmpty) print('MISSING ${http.missing.join('\n')}');
  });

  Widget app() => RepaintBoundary(
    key: screenshotKey,
    child: const JarvisApp(skipAuthentication: true),
  );

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 30; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  Future<void> start(WidgetTester tester, {bool dark = false}) async {
    tester.platformDispatcher.platformBrightnessTestValue = dark
        ? Brightness.dark
        : Brightness.light;
    addTearDown(tester.platformDispatcher.clearPlatformBrightnessTestValue);
    await tester.pumpWidget(app());
    await settle(tester);
  }

  Future<void> openChat(WidgetTester tester) async {
    await tester.tap(find.text('Continue conversation'));
    await settle(tester);
  }

  Future<void> openFromMenu(WidgetTester tester, String label) async {
    await tester.tap(find.byTooltip('Menu'));
    await settle(tester);
    final target = find.text(label).last;
    await tester.ensureVisible(target);
    await tester.tap(target);
    await settle(tester);
  }

  for (final dark in [false, true]) {
    final mode = dark ? 'dark' : 'light';

    screenshotTest('home $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await capture(tester, '01-home-$mode');
      await tester.drag(find.byType(Scrollable).first, const Offset(0, -700));
      await settle(tester);
      await capture(tester, '02-home-scrolled-$mode');
    });

    screenshotTest('chat $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openChat(tester);
      await capture(tester, '03-chat-$mode');
      await tester.enterText(find.byType(TextField).last, 'Also find a good fado bar');
      await settle(tester);
      await capture(tester, '04-chat-typing-$mode');
    });

    screenshotTest('menu $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await tester.tap(find.byTooltip('Menu'));
      await settle(tester);
      await capture(tester, '05-menu-$mode');
    });
  }

  screenshotTest('approval', (tester) async {
    usePhone(tester);
    http.routes.addAll(fixtureRoutes());
    await start(tester);
    await capture(tester, '11-approval');
  });

  screenshotTest('quick actions', (tester) async {
    usePhone(tester);
    await start(tester);
    await openChat(tester);
    await tester.tap(find.byTooltip('More actions'));
    await settle(tester);
    await capture(tester, '06-quick-actions');
  });

  for (final (index, label) in [
    (7, 'Reminders'),
    (8, 'Memory'),
    (9, 'Tasks'),
    (10, 'Habits'),
    (12, 'Today'),
    (13, 'Expenses'),
    (14, 'People'),
    (15, 'Journal'),
  ]) {
    screenshotTest('page $label', (tester) async {
      usePhone(tester);
      await start(tester);
      await openFromMenu(tester, label);
      await capture(tester, '${index.toString().padLeft(2, '0')}-${label.toLowerCase()}');
    });
  }

  screenshotTest('reminder swipe', (tester) async {
    usePhone(tester);
    await start(tester);
    await openFromMenu(tester, 'Reminders');
    final gesture = await tester.startGesture(
      tester.getCenter(find.text('Call mum')),
    );
    for (var i = 0; i < 6; i++) {
      await gesture.moveBy(const Offset(22, 0));
      await tester.pump(const Duration(milliseconds: 16));
    }
    await capture(tester, '19-reminder-swipe');
    await gesture.up();
    await settle(tester);
  });

  screenshotTest('sign in', (tester) async {
    usePhone(tester);
    await tester.pumpWidget(
      RepaintBoundary(key: screenshotKey, child: const JarvisApp()),
    );
    await settle(tester);
    await capture(tester, '16-sign-in');
  });

  screenshotTest('settings', (tester) async {
    usePhone(tester);
    await start(tester);
    await tester.tap(find.byTooltip('Menu'));
    await settle(tester);
    await tester.tap(find.text('Settings'));
    await settle(tester);
    await capture(tester, '17-settings');
  });

  screenshotTest('voice', (tester) async {
    usePhone(tester);
    await start(tester, dark: true);
    await tester.tap(find.byTooltip('Menu'));
    await settle(tester);
    await tester.tap(find.text('Voice'));
    await settle(tester);
    await capture(tester, '18-voice');
  });

  screenshotTest('desktop', (tester) async {
    useDesktop(tester);
    await start(tester);
    await openChat(tester);
    await capture(tester, '20-desktop-chat');
  });
}
