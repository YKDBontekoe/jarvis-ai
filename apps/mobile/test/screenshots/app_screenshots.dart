import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';
import 'package:jarvis_mobile/ui/phosphor_icons.dart';
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
      await tester.enterText(
        find.byType(TextField).last,
        'Also find a good fado bar',
      );
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
      await capture(
        tester,
        '${index.toString().padLeft(2, '0')}-${label.toLowerCase()}',
      );
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

  for (final dark in [false, true]) {
    screenshotTest('components ${dark ? 'dark' : 'light'}', (tester) async {
      usePhone(tester);
      final input = TextEditingController(text: 'Find a quiet fado bar near Alfama');
      addTearDown(input.dispose);
      await tester.pumpWidget(
        RepaintBoundary(
          key: screenshotKey,
          child: MaterialApp(
            debugShowCheckedModeBanner: false,
            theme: buildJarvisTheme(
              brightness: dark ? Brightness.dark : Brightness.light,
            ),
            home: Scaffold(
              body: SafeArea(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(18, 16, 18, 14),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const MessageBubble(
                        message: MessageEntry(
                          role: 'user',
                          content: 'Find a quiet fado bar near Alfama for Saturday.',
                        ),
                      ),
                      ToolRunView(
                        run: const ToolRunEntry([
                          ToolStep('SearchMemory', ToolStepStatus.completed),
                          ToolStep('WebSearch', ToolStepStatus.running),
                        ]),
                        onOpenTasks: () {},
                      ),
                      const MessageBubble(
                        message: MessageEntry(
                          role: 'assistant',
                          content: '',
                          pending: true,
                        ),
                        thinkingLabel: 'Looking around Alfama',
                      ),
                      const SizedBox(height: 8),
                      const StatusChip(
                        label: 'Realtime updates are offline',
                        color: Color(0xffd97706),
                        actionLabel: 'Retry',
                      ),
                      const Spacer(),
                      ChatComposer(
                        controller: input,
                        onSend: () {},
                        onCancel: () {},
                        onVoice: () {},
                        onAttach: () {},
                        onPhoto: () {},
                        sending: true,
                        voiceActive: false,
                        voiceStarting: false,
                      ),
                      const SizedBox(height: 12),
                      ChatComposer(
                        controller: TextEditingController(text: 'Book it for 21:00'),
                        onSend: () {},
                        onVoice: () {},
                        onAttach: () {},
                        sending: false,
                        voiceActive: false,
                        voiceStarting: false,
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      );
      for (var i = 0; i < 8; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await capture(tester, '21-components-${dark ? 'dark' : 'light'}');
    });
  }

  screenshotTest('empty state', (tester) async {
    usePhone(tester);
    await tester.pumpWidget(
      RepaintBoundary(
        key: screenshotKey,
        child: MaterialApp(
          debugShowCheckedModeBanner: false,
          theme: buildJarvisTheme(),
          home: Scaffold(
            appBar: AppBar(title: const Text('Journal')),
            body: EmptyState(
              icon: PhosphorIconsRegular.notebook,
              title: 'Nothing written yet',
              message: 'Tell Jarvis about your day and it keeps the notes here.',
              action: FilledButton(onPressed: () {}, child: const Text('Write today')),
            ),
          ),
        ),
      ),
    );
    for (var i = 0; i < 8; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    await capture(tester, '22-empty-state');
  });

  for (final (index, action) in [
    (23, 'Set a reminder'),
    (24, 'Start a background task'),
    (25, 'Add a memory'),
  ]) {
    screenshotTest('create $action', (tester) async {
      usePhone(tester);
      await start(tester);
      await openChat(tester);
      await tester.tap(find.byTooltip('More actions'));
      await settle(tester);
      await tester.tap(find.text(action));
      await settle(tester);
      await capture(tester, '$index-create-${action.split(' ').last}');
    });
  }

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
