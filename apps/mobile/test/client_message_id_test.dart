import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/outbox_store.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  // The server accepts 1 to 64 letters, digits, dots, dashes, colons and underscores.
  final accepted = RegExp(r'^[A-Za-z0-9._:-]{1,64}$');

  test('every send gets a fresh id the server will accept', () {
    final ids = {for (var i = 0; i < 500; i++) newClientMessageId()};
    expect(ids, hasLength(500));
    expect(ids.every(accepted.hasMatch), isTrue);
  });

  test('a message keeps its id when it fails, is edited or is queued', () {
    const entry = MessageEntry(role: 'user', content: 'Hi', clientId: 'm-1');
    expect(entry.copyWith(failed: true).clientId, 'm-1');
    expect(entry.copyWith(content: 'Hi!').clientId, 'm-1');
  });

  test('a waiting message keeps its id across a restart', () async {
    SharedPreferences.setMockInitialValues({});
    final outbox = await OutboxStore.open();
    final id = newClientMessageId();
    await outbox.add(
      OutboxMessage(
        id: id,
        conversationId: 'c1',
        content: 'Hello',
        queuedAt: DateTime.now(),
      ),
    );

    final reopened = await OutboxStore.open();
    expect(reopened.forConversation('c1').single.id, id);
    expect(accepted.hasMatch(reopened.forConversation('c1').single.id), isTrue);
  });
}
