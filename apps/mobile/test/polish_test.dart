import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

void main() {
  Widget field(TextEditingController controller) => MaterialApp(
    theme: buildJarvisTheme(),
    home: Scaffold(
      body: TextField(
        controller: controller,
        maxLength: 10,
        buildCounter: quietCounter,
      ),
    ),
  );

  testWidgets('the character counter appears only near the limit', (
    tester,
  ) async {
    final controller = TextEditingController(text: 'abc');
    addTearDown(controller.dispose);
    await tester.pumpWidget(field(controller));
    expect(find.text('3/10'), findsNothing);

    controller.text = 'abcdefgh';
    await tester.pump();
    expect(find.text('8/10'), findsOneWidget);

    controller.text = 'abcdefghij';
    await tester.pump();
    final full = tester.widget<Text>(find.text('10/10'));
    expect(full.style?.color, JarvisColors.light.warning);
  });

  test('status colours follow the theme', () {
    expect(
      statusStyle('running', JarvisColors.dark).color,
      JarvisColors.dark.info,
    );
    expect(statusStyle('running').color, JarvisColors.light.info);
  });

  testWidgets('a status pill uses the dark palette in dark mode', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(brightness: Brightness.dark),
        home: Scaffold(body: StatusPill.forStatus('failed')),
      ),
    );
    final dot = tester
        .widgetList<Container>(find.byType(Container))
        .map((c) => c.decoration)
        .whereType<BoxDecoration>()
        .where((d) => d.shape == BoxShape.circle)
        .single;
    expect(dot.color, JarvisColors.dark.danger);
  });
}
