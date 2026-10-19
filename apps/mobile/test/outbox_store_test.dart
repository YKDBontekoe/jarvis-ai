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

  test(
    'an old message is kept and marked expired instead of being dropped',
    () async {
      final now = DateTime(2026, 10, 3, 12);
      final outbox = await OutboxStore.open();
      await outbox.add(
        _message('old', 'c1', at: now.subtract(const Duration(days: 3))),
      );
      await outbox.add(_message('new', 'c1', at: now));

      final items = (await OutboxStore.open()).forConversation('c1');
      expect(items.map((item) => item.id), ['old', 'new']);
      expect(OutboxStore.isExpired(items[0], now), isTrue);
      expect(OutboxStore.isExpired(items[1], now), isFalse);
    },
  );

  test('a waiting message says how long it has waited and when it stops sending by itself', () {
    final now = DateTime(2026, 10, 3, 12);

    expect(queuedNote(null, now), 'Waiting for a connection. Sends by itself.');
    expect(
      queuedNote(now.subtract(const Duration(minutes: 5)), now),
      'Written 5 min ago. Waiting for a connection; it sends by itself.',
    );
    expect(
      queuedNote(now.subtract(const Duration(hours: 3)), now),
      contains('3 h ago'),
    );
    final stale = queuedNote(now.subtract(const Duration(days: 3)), now);
    expect(stale, contains('3 d ago'));
    expect(stale, contains('will not go out by itself'));
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

  testWidgets(
    'an expired message offers Send now and is not promised to send by itself',
    (tester) async {
      var sent = 0;
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: Scaffold(
            body: MessageBubble(
              message: MessageEntry(
                role: 'user',
                content: 'Book a table',
                outboxId: 'o1',
                queuedAt: DateTime.now().subtract(const Duration(days: 3)),
              ),
              onCancelQueued: () {},
              onSendQueuedNow: () => sent++,
            ),
          ),
        ),
      );

      expect(find.textContaining('will not go out by itself'), findsOneWidget);
      await tester.tap(find.byKey(const Key('queued-send-now')));
      expect(sent, 1);
    },
  );

  testWidgets('a fresh waiting message has no Send now', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: MessageBubble(
            message: MessageEntry(
              role: 'user',
              content: 'Book a table',
              outboxId: 'o1',
              queuedAt: DateTime.now().subtract(const Duration(minutes: 2)),
            ),
            onCancelQueued: () {},
            onSendQueuedNow: () {},
          ),
        ),
      ),
    );

    expect(find.byKey(const Key('queued-send-now')), findsNothing);
    expect(find.textContaining('sends by itself'), findsOneWidget);
  });

  group('arrivedMessageId', () {
    final queuedAt = DateTime.utc(2026, 10, 16, 12);
    OutboxMessage queued(String content) => OutboxMessage(
      id: 'o-$content',
      conversationId: 'c1',
      content: content,
      queuedAt: queuedAt,
    );
    Map<String, dynamic> stored(
      String id,
      String content, {
      String role = 'user',
      int minute = 0,
    }) => {
      'id': id,
      'role': role,
      'content': content,
      'createdAt': queuedAt.add(Duration(minutes: minute)).toIso8601String(),
    };

    test('finds a message the server took before the connection dropped', () {
      final recent = [stored('m1', 'On my way')];

      expect(
        arrivedMessageId(
          queued('On my way'),
          recent,
          existing: {'m1'},
          claimed: {},
        ),
        'm1',
      );
      expect(
        arrivedMessageId(
          queued('Something else'),
          recent,
          existing: {'m1'},
          claimed: {},
        ),
        isNull,
      );
    });

    test('ignores replies, older messages and other roles', () {
      final recent = [
        stored('m1', 'On my way', role: 'assistant'),
        stored('m2', 'On my way', minute: -5),
      ];

      expect(
        arrivedMessageId(
          queued('On my way'),
          recent,
          existing: {'m1', 'm2'},
          claimed: {},
        ),
        isNull,
      );
    });

    test('two identical texts typed offline are two messages', () {
      // The first one went out during this flush, so it was not there when the flush began.
      final recent = [stored('m-sent-now', 'Yes', minute: 1)];

      expect(
        arrivedMessageId(
          queued('Yes'),
          recent,
          existing: {'m-old'},
          claimed: {},
        ),
        isNull,
      );
    });

    test('one stored message is only ever taken by one queued item', () {
      final recent = [stored('m1', 'Yes')];
      final claimed = <String>{};

      final first = arrivedMessageId(
        queued('Yes'),
        recent,
        existing: {'m1'},
        claimed: claimed,
      );
      claimed.add(first!);

      expect(
        arrivedMessageId(
          queued('Yes'),
          recent,
          existing: {'m1'},
          claimed: claimed,
        ),
        isNull,
      );
    });
  });
}
