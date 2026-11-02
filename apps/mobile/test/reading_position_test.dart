import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/reading_position.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  group('ReadingPositions', () {
    setUp(() => SharedPreferences.setMockInitialValues({}));

    test('a spot survives reopening', () async {
      final store = await ReadingPositions.open();
      store.write('c1', (messageId: 'm42', within: 33.5));
      await store.flush();

      final reopened = await ReadingPositions.open();
      expect(reopened.read('c1'), (messageId: 'm42', within: 33.5));
      expect(reopened.read('c2'), isNull);
    });

    test('a spot older than 30 days is dropped', () async {
      final store = await ReadingPositions.open();
      store.write('c1', (
        messageId: 'm1',
        within: 0,
      ), now: DateTime.now().subtract(const Duration(days: 31)));
      await store.flush();
      expect((await ReadingPositions.open()).read('c1'), isNull);
    });

    test('keeps the 30 most recent conversations', () async {
      final store = await ReadingPositions.open();
      for (var i = 0; i < ReadingPositions.maxItems + 5; i++) {
        store.write('c$i', (messageId: 'm$i', within: 0));
      }
      expect(store.read('c0'), isNull);
      expect(store.read('c${ReadingPositions.maxItems + 4}'), isNotNull);
    });

    test('remove forgets a spot and clearAll forgets every one', () async {
      final store = await ReadingPositions.open();
      store.write('c1', (messageId: 'm1', within: 0));
      store.write('c2', (messageId: 'm2', within: 0));
      store.remove('c1');
      await store.flush();
      final reopened = await ReadingPositions.open();
      expect(reopened.read('c1'), isNull);
      expect(reopened.read('c2'), isNotNull);

      await ReadingPositions.clearAll();
      expect((await ReadingPositions.open()).read('c2'), isNull);
    });

    test('a corrupt value is ignored', () async {
      SharedPreferences.setMockInitialValues({
        ReadingPositions.storageKey: '{not json',
      });
      expect((await ReadingPositions.open()).read('c1'), isNull);
    });
  });

  group('TranscriptAnchors', () {
    late ScrollController controller;
    late TranscriptAnchors anchors;

    // Messages of different heights, built lazily like the real transcript.
    double heightOf(int i) => 60.0 + (i % 4) * 30;

    Future<void> show(WidgetTester tester) async {
      controller = ScrollController();
      anchors = TranscriptAnchors();
      addTearDown(controller.dispose);
      tester.view.physicalSize = const Size(400, 600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ListView.builder(
              controller: controller,
              itemCount: 200,
              itemBuilder: (_, i) => KeyedSubtree(
                key: anchors.keyFor('m$i'),
                child: SizedBox(height: heightOf(i), child: Text('message $i')),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('finds the message at the top of the screen and how far in', (
      tester,
    ) async {
      await show(tester);
      controller.jumpTo(1000);
      await tester.pumpAndSettle();

      final spot = anchors.anchor(controller)!;
      var top = 0.0;
      var index = 0;
      while (top + heightOf(index) <= 1000) {
        top += heightOf(index);
        index++;
      }
      expect(spot.messageId, 'm$index');
      expect(spot.within, closeTo(1000 - top, 1));
    });

    testWidgets('reveal returns to the same spot, even for a message that was '
        'not built', (tester) async {
      await show(tester);
      controller.jumpTo(5000);
      await tester.pumpAndSettle();
      final spot = anchors.anchor(controller)!;

      // Back to the top: that message is no longer built.
      controller.jumpTo(0);
      await tester.pumpAndSettle();

      final placed = await anchors.reveal(
        controller,
        spot,
        estimate: () => controller.position.maxScrollExtent * .25,
        nextFrame: () => tester.pump(),
      );
      expect(placed, isTrue);
      await tester.pumpAndSettle();
      expect(controller.position.pixels, closeTo(5000, 2));
    });

    testWidgets('a message that is gone cannot be revealed', (tester) async {
      await show(tester);
      final placed = await anchors.reveal(
        controller,
        (messageId: 'missing', within: 0),
        estimate: () => 100,
        attempts: 3,
        nextFrame: () => tester.pump(),
      );
      expect(placed, isFalse);
    });
  });
}
