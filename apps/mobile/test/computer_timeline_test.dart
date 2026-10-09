import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/generative_ui.dart';

// A 1x1 transparent PNG.
final _png = Uint8List.fromList(const [
  0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, //
  0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
  0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
  0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
  0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
  0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
]);

BrowserSessionEntry _computer({String controlMode = 'agent'}) =>
    BrowserSessionEntry(
      id: 's1',
      goal: 'Book a table',
      kind: 'computer',
      controlMode: controlMode,
      steps: const [
        BrowserStepItem(
          tool: 'UseComputer',
          summary: 'Started: Book a table',
          success: true,
          ordinal: 1,
        ),
        BrowserStepItem(
          tool: 'computer_click',
          summary: 'Clicked left at 10,20.',
          success: true,
          ordinal: 2,
          hasScreenshot: true,
        ),
      ],
    );

Future<void> _show(WidgetTester tester, Widget child) async {
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(body: SingleChildScrollView(child: child)),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  test('steps read their screenshot and ordinal from the API', () {
    final step = BrowserStepItem.fromJson(const {
      'tool': 'computer_type',
      'summary': 'Typed 4 characters.',
      'success': true,
      'ordinal': 3,
      'hasScreenshot': true,
    });
    expect(step.ordinal, 3);
    expect(step.hasScreenshot, isTrue);
    expect(_computer().latestScreenshot?.ordinal, 2);
  });

  testWidgets('a computer session shows its latest screenshot and controls', (
    tester,
  ) async {
    final requested = <String>[];
    var watched = 0;
    var takenOver = 0;
    await _show(
      tester,
      BrowserTimelineView(
        session: _computer(),
        loadScreenshot: (sessionId, ordinal) async {
          requested.add('$sessionId:$ordinal');
          return _png;
        },
        onWatch: () => watched++,
        onTakeOver: () => takenOver++,
        onHandBack: () {},
      ),
    );

    expect(requested, ['s1:2']);
    expect(find.byType(Image), findsOneWidget);
    expect(find.text('Clicked left at 10,20.'), findsOneWidget);
    await tester.ensureVisible(find.text('Watch live'));
    await tester.tap(find.text('Watch live'));
    await tester.tap(find.text('Take over'));
    expect(watched, 1);
    expect(takenOver, 1);
    expect(find.text('Hand back to Jarvis'), findsNothing);
  });

  testWidgets('after taking over the card offers to hand control back', (
    tester,
  ) async {
    var handedBack = 0;
    await _show(
      tester,
      BrowserTimelineView(
        session: _computer(controlMode: 'user'),
        loadScreenshot: (_, _) async => null,
        onWatch: () {},
        onTakeOver: () {},
        onHandBack: () => handedBack++,
      ),
    );

    expect(find.textContaining('You have control'), findsOneWidget);
    expect(find.text('Take over'), findsNothing);
    await tester.ensureVisible(find.text('Hand back to Jarvis'));
    await tester.tap(find.text('Hand back to Jarvis'));
    expect(handedBack, 1);
  });

  testWidgets('finished and headless browser sessions have no live controls', (
    tester,
  ) async {
    await _show(
      tester,
      BrowserTimelineView(
        session: _computer().copyWith(status: 'completed'),
        onWatch: () {},
        onTakeOver: () {},
      ),
    );
    expect(find.text('Watch live'), findsNothing);

    await _show(
      tester,
      BrowserTimelineView(
        session: const BrowserSessionEntry(
          id: 'b1',
          goal: 'Look up the weather',
          steps: [],
        ),
        onWatch: () {},
      ),
    );
    expect(find.text('Watch live'), findsNothing);
  });
}
