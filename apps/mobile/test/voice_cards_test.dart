import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/voice/voice_cards.dart';
import 'package:jarvis_mobile/features/voice/voice_stage.dart';

Map<String, dynamic> card({String id = 'c1', String state = 'shown'}) => {
  'id': id,
  'kind': 'needs_you',
  'title': 'What needs you',
  'state': state,
  'items': [
    {
      'title': 'Prepare for “Kitchen sync”',
      'detail': 'It starts in about 2 hours.',
      'destination': 'meeting:m1',
    },
    {
      'title': 'Review Netflix',
      'detail': 'Renews 22 Oct.',
      'destination': null,
    },
  ],
};

void main() {
  Future<void> show(
    WidgetTester tester, {
    required List<Map<String, dynamic>> cards,
    String phase = 'listening',
    List<String>? log,
    VoidCallback? onRecap,
  }) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: VoiceStage(
            phase: phase,
            voiceName: 'Juniper',
            handsFree: true,
            captions: true,
            canStart: true,
            onPrimary: () {},
            cards: cards,
            onCardOpen: (d) => log?.add('open:$d'),
            onCardSave: (id) => log?.add('save:$id'),
            onCardDismiss: (id) => log?.add('dismiss:$id'),
            onRecap: onRecap,
          ),
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 300));
  }

  testWidgets(
    'a card shows the rows the voice did not read and opens the real thing',
    (tester) async {
      final log = <String>[];
      await show(tester, cards: [card()], log: log);

      expect(find.text('What needs you'), findsOneWidget);
      expect(find.text('Review Netflix'), findsOneWidget);
      await tester.tap(find.byKey(const Key('voice-card-c1-item-0')));
      await tester.tap(find.byKey(const Key('voice-card-c1-item-1')));
      expect(log, ['open:meeting:m1']);
    },
  );

  testWidgets(
    'save and put away are separate and a saved card cannot be saved again',
    (tester) async {
      final log = <String>[];
      await show(tester, cards: [card()], log: log);

      await tester.tap(find.byKey(const Key('voice-card-save-c1')));
      await tester.tap(find.byKey(const Key('voice-card-dismiss-c1')));
      expect(log, ['save:c1', 'dismiss:c1']);

      await show(
        tester,
        cards: [card(state: 'saved')],
        log: log,
      );
      expect(find.text('Saved'), findsOneWidget);
      log.clear();
      await tester.tap(
        find.byKey(const Key('voice-card-save-c1')),
        warnIfMissed: false,
      );
      expect(log, isEmpty);
    },
  );

  testWidgets('the recap link appears only after the session is over', (
    tester,
  ) async {
    var opened = 0;
    await show(
      tester,
      cards: [card()],
      phase: 'listening',
      onRecap: () => opened++,
    );
    expect(find.byKey(const Key('voice-recap-open')), findsNothing);

    await show(tester, cards: [card()], phase: 'idle', onRecap: () => opened++);
    await tester.tap(find.byKey(const Key('voice-recap-open')));
    expect(opened, 1);
  });

  testWidgets(
    'the recap keeps saved, put away and open apart and says nothing was sent',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                key: const Key('go'),
                onPressed: () => showVoiceRecap(context, {
                  'shown': 3,
                  'saved': 1,
                  'dismissed': 1,
                  'open': 1,
                  'lines': [
                    {
                      'title': 'What needs you',
                      'state': 'saved',
                      'text': 'Saved to your Library.',
                    },
                    {
                      'title': 'Which kettle?',
                      'state': 'dismissed',
                      'text': 'You put it away.',
                    },
                    {
                      'title': 'Other',
                      'state': 'shown',
                      'text': 'Still open: not saved.',
                    },
                  ],
                  'statement': 'Nothing was sent, accepted or changed outside Jarvis by voice.',
                }),
                child: const Text('go'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('go')));
      await tester.pumpAndSettle();

      expect(
        find.text('3 shown · 1 saved · 1 put away · 1 still open'),
        findsOneWidget,
      );
      expect(find.text('Saved to your Library.'), findsOneWidget);
      expect(find.text('You put it away.'), findsOneWidget);
      expect(find.text('Still open: not saved.'), findsOneWidget);
      expect(find.textContaining('Nothing was sent, accepted'), findsOneWidget);
    },
  );
}
