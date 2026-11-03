import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/main.dart';

import 'screenshots/fixtures.dart';
import 'screenshots/harness.dart';

/// A hardware keyboard (iPad, Android tablet, desktop web) can reach and use the
/// main controls: Tab walks through them, and Enter or Space activates one.
void main() {
  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    debugJarvisHttpAdapter = ScreenshotHttp(fixtureRoutes(withApproval: true));
  });
  tearDown(() => debugJarvisHttpAdapter = null);

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 20; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  /// The semantics label of what has keyboard focus, or of its nearest
  /// labelled ancestor.
  String? focusedLabel(WidgetTester tester) {
    final context = FocusManager.instance.primaryFocus?.context;
    if (context == null) return null;
    RenderObject? render = context.findRenderObject();
    while (render != null) {
      final node = render.debugSemantics;
      final label = node?.label;
      if (label != null && label.isNotEmpty) return label.split('\n').first;
      render = render.parent;
    }
    return null;
  }

  Future<List<String>> tabThrough(WidgetTester tester, int presses) async {
    final seen = <String>[];
    for (var i = 0; i < presses; i++) {
      await tester.sendKeyEvent(LogicalKeyboardKey.tab);
      await tester.pump();
      final label = focusedLabel(tester);
      if (label != null && !seen.contains(label)) seen.add(label);
    }
    return seen;
  }

  testWidgets('Tab reaches the dock, the assistant and Home actions', (
    tester,
  ) async {
    usePhone(tester);
    final semantics = tester.ensureSemantics();
    await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
    await settle(tester);

    final seen = await tabThrough(tester, 80);

    for (final expected in ['Home', 'Everything', 'You', 'Ask Jarvis']) {
      expect(
        seen.any((label) => label.startsWith(expected)),
        isTrue,
        reason: '$expected was never focused; focus order: $seen',
      );
    }
    expect(
      seen.any((label) => label.startsWith('Chats')),
      isTrue,
      reason: 'Chats was never focused; focus order: $seen',
    );
    // Focus shows tooltips; let their timers finish before the tree goes.
    await tester.pumpWidget(const SizedBox());
    await tester.pump(const Duration(seconds: 3));
    semantics.dispose();
  });

  for (final key in [LogicalKeyboardKey.enter, LogicalKeyboardKey.space]) {
    testWidgets('${key.keyLabel} on a focused tab opens it', (tester) async {
      usePhone(tester);
      final semantics = tester.ensureSemantics();
      await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
      await settle(tester);

      for (var i = 0; i < 80; i++) {
        await tester.sendKeyEvent(LogicalKeyboardKey.tab);
        await tester.pump();
        if (focusedLabel(tester)?.startsWith('Everything') ?? false) break;
      }
      expect(focusedLabel(tester), startsWith('Everything'));
      await tester.sendKeyEvent(key);
      await settle(tester);

      expect(find.byKey(const Key('tab-selected-label')), findsOneWidget);
      expect(
        tester.widget<Text>(find.byKey(const Key('tab-selected-label'))).data,
        'Everything',
      );
      semantics.dispose();
    });
  }

  testWidgets('Enter sends from the composer and Shift+Enter does not', (
    tester,
  ) async {
    final controller = TextEditingController();
    addTearDown(controller.dispose);
    var sent = 0;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: Center(
            child: ChatComposer(
              controller: controller,
              onSend: () => sent++,
              onVoice: null,
              sending: false,
              voiceActive: false,
              voiceStarting: false,
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.byType(TextField));
    await tester.enterText(find.byType(TextField), 'Plan my Friday');
    await tester.pump();

    await tester.sendKeyDownEvent(LogicalKeyboardKey.shiftLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.enter);
    await tester.sendKeyUpEvent(LogicalKeyboardKey.shiftLeft);
    await tester.pump();
    expect(sent, 0);

    await tester.sendKeyEvent(LogicalKeyboardKey.enter);
    await tester.pump();
    expect(sent, 1);
  });
}
