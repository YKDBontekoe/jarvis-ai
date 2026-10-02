import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/sidebar.dart';
import 'package:jarvis_mobile/theme.dart';

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: child),
);

void main() {
  testWidgets('sidebar has one search entry and core destinations', (
    tester,
  ) async {
    var openedToday = false;
    var searched = false;
    var openedWhatsApp = false;
    await tester.pumpWidget(
      _host(
        JarvisSidebar(
          conversations: const [],
          selectedConversationId: null,
          homeSelected: true,
          connected: true,
          onHome: () {},
          onNewChat: () {},
          onVoice: () {},
          onConversation: (_) {},
          onSeeAll: () {},
          onUtility: (destination) {
            if (destination == 'today') openedToday = true;
            if (destination == 'whatsapp') openedWhatsApp = true;
          },
          onSettings: () {},
          onJarvisSearch: () => searched = true,
        ),
      ),
    );

    expect(find.byType(TextField), findsNothing);
    for (final label in [
      'Files',
      'Usage',
      'Coding',
      'Tasks',
      'Memory',
      'Journal',
      'Expenses',
      'Habits',
      'People',
      'Reminders',
    ]) {
      expect(find.text(label), findsNothing);
    }

    await tester.tap(find.text('Ask or find'));
    expect(searched, isTrue);

    await tester.tap(find.text('Today'));
    await tester.pumpAndSettle();
    expect(openedToday, isTrue);
    await tester.ensureVisible(find.text('WhatsApp'));
    await tester.tap(find.text('WhatsApp'));
    expect(openedWhatsApp, isTrue);
  });
}
