import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/sidebar.dart';
import 'package:jarvis_mobile/theme.dart';

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: child),
);

void main() {
  testWidgets('sidebar navigation dismisses the keyboard', (tester) async {
    var openedTasks = false;
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
          },
          onSettings: () {},
          onJarvisSearch: () {},
        ),
      ),
    );

    await tester.tap(find.byType(TextField));
    await tester.pump();
    await tester.enterText(find.byType(TextField), 'jo');
    expect(FocusManager.instance.primaryFocus?.hasFocus, isTrue);

    await tester.tap(find.text('Tasks'));
    await tester.pumpAndSettle();

    expect(openedTasks, isTrue);
    expect(tester.widget<TextField>(find.byType(TextField)).focusNode?.hasFocus,
        isFalse);
  });
}
