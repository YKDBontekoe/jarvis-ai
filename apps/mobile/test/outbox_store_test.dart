import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/chat/outbox_store.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

OutboxMessage _message(String id, String conversation, {DateTime? at}) =>
    OutboxMessage(
      id: id,
      conversationId: conversation,
      content: 'Message $id',
      queuedAt: at ?? DateTime.now(),
      photos: const [(fileId: 'f1', fileName: 'leaf.jpg')],
    );

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  test('keeps waiting messages in order per conversation', () async {
    final outbox = await OutboxStore.open();
    await outbox.add(_message('a', 'c1'));
    await outbox.add(_message('b', 'c2'));
    await outbox.add(_message('c', 'c1'));

    final reopened = await OutboxStore.open();
    expect(reopened.forConversation('c1').map((item) => item.id), ['a', 'c']);
    expect(reopened.forConversation('c1').first.photos.single.fileId, 'f1');
    expect(reopened.forConversation('c2').single.content, 'Message b');
  });

  test('remove and clear forget messages on the device', () async {
    final outbox = await OutboxStore.open();
    await outbox.add(_message('a', 'c1'));
    await outbox.add(_message('b', 'c1'));
    await outbox.remove('a');
    expect(
      (await OutboxStore.open()).forConversation('c1').map((item) => item.id),
      ['b'],
    );

    await OutboxStore.clearAll();
    expect((await OutboxStore.open()).isEmpty, isTrue);
  });

  test('drops messages older than two days', () async {
    final now = DateTime(2026, 10, 3, 12);
    final outbox = await OutboxStore.open(now: now);
    await outbox.add(
      _message('old', 'c1', at: now.subtract(const Duration(days: 3))),
    );
    await outbox.add(_message('new', 'c1', at: now));

    final reopened = await OutboxStore.open(now: now);
    expect(reopened.forConversation('c1').map((item) => item.id), ['new']);
  });

  test('ignores corrupt stored data', () async {
    SharedPreferences.setMockInitialValues({
      OutboxStore.storageKey: '[{"x":1}',
    });
    expect((await OutboxStore.open()).isEmpty, isTrue);
  });

  testWidgets('a waiting message says so and can be cancelled', (tester) async {
    var cancelled = 0;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: MessageBubble(
            message: const MessageEntry(
              role: 'user',
              content: 'Book a table',
              outboxId: 'o1',
            ),
            onCancelQueued: () => cancelled++,
          ),
        ),
      ),
    );

    expect(
      find.text('Waiting for a connection. Sends by itself.'),
      findsOneWidget,
    );
    await tester.tap(find.text('Cancel'));
    expect(cancelled, 1);
  });
}
