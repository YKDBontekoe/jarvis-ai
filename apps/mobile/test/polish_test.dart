import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
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

  testWidgets('double-tapping a photo zooms in and double-tapping again fits '
      'it', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: PhotoViewerPage(bytes: Uint8List(0), title: 'Receipt'),
      ),
    );
    Matrix4 matrix() => tester
        .widget<InteractiveViewer>(find.byType(InteractiveViewer))
        .transformationController!
        .value;
    expect(matrix().getMaxScaleOnAxis(), 1);

    Future<void> doubleTap() async {
      final center = tester.getCenter(find.byType(InteractiveViewer));
      await tester.tapAt(center);
      await tester.pump(const Duration(milliseconds: 50));
      await tester.tapAt(center);
      await tester.pumpAndSettle();
    }

    await doubleTap();
    expect(matrix().getMaxScaleOnAxis(), closeTo(2.5, .01));
    await doubleTap();
    expect(matrix().getMaxScaleOnAxis(), closeTo(1, .01));
    expect(find.text('This photo cannot be shown.'), findsOneWidget);
  });

  testWidgets('the search clear button shows with text and empties the '
      'field', (tester) async {
    final controller = TextEditingController();
    addTearDown(controller.dispose);
    var cleared = 0;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: TextField(
            controller: controller,
            decoration: InputDecoration(
              suffixIcon: SearchClearButton(
                controller: controller,
                onCleared: () => cleared++,
              ),
            ),
          ),
        ),
      ),
    );
    expect(find.byTooltip('Clear search'), findsNothing);
    await tester.enterText(find.byType(TextField), 'rosa');
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Clear search'));
    await tester.pumpAndSettle();
    expect(controller.text, isEmpty);
    expect(cleared, 1);
    expect(find.byTooltip('Clear search'), findsNothing);
  });
}
