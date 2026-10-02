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
    var openedTasks = false;
    var openedWhatsApp = false;
    var searched = false;
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
            if (destination == 'tasks') openedTasks = true;
            if (destination == 'whatsapp') openedWhatsApp = true;
          },
          onSettings: () {},
          onJarvisSearch: () => searched = true,
        ),
      ),
    );

    expect(find.byType(TextField), findsNothing);
    for (final label in ['Files', 'Usage', 'Coding']) {
      expect(find.text(label), findsNothing);
    }

    for (final label in [
      'Voice',
      'Today',
      'Tasks',
      'Memory',
      'Journal',
      'Expenses',
      'Habits',
      'People',
      'Reminders',
      'WhatsApp',
    ]) {
      expect(find.text(label), findsOneWidget);
    }
    expect(find.text('Ask or find'), findsNothing);

    await tester.tap(find.text('Search'));
    expect(searched, isTrue);

    await tester.tap(find.text('Tasks'));
    await tester.pumpAndSettle();
    expect(openedTasks, isTrue);
    await tester.tap(find.text('WhatsApp'));
    await tester.pumpAndSettle();
    expect(openedWhatsApp, isTrue);
  });
}
