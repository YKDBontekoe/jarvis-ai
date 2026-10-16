import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';
import 'package:jarvis_mobile/features/home/home_insights.dart';

import 'support/fixture_http.dart';

String _iso(DateTime time) => time.toUtc().toIso8601String();

void main() {
  final now = DateTime(2026, 10, 16, 9, 14);

  Future<ChatList> chatsWith(List<Map<String, Object?>> rows) async {
    final http = FixtureHttp()
      ..on('GET', '/api/v1/channels', [
        {'id': 'wa', 'kind': 'whatsapp_linked'},
      ])
      ..on('GET', '/api/v1/channels/wa/chats', {'chats': rows});
    final list = ChatList();
    await list.loadWhatsApp(http.client());
    return list;
  }

  Map<String, Object?> chat(
    String id,
    String name,
    Duration ago, {
    int unread = 0,
    bool group = false,
    Map<String, Object?>? catchUp,
  }) => {
    'chatId': id,
    'name': name,
    'isGroup': group,
    'lastMessageAt': _iso(now.subtract(ago)),
    'preview': 'hello',
    'unreadCount': unread,
    'catchUp': ?catchUp,
  };

  group('homeSuggestions', () {
    test('mornings plan the day; a waiting person comes first', () async {
      final chats = await chatsWith([
        chat('+1', 'Rosa', const Duration(hours: 14), unread: 1),
      ]);
      final picks = homeSuggestions(now: now, chats: chats);
      expect(picks.first.label, 'Reply to Rosa');
      expect(picks.map((s) => s.label), contains('Plan my day'));
    });

    test('a reminder within three hours gets a prepare chip', () {
      final picks = homeSuggestions(
        now: now,
        briefing: {
          'reminders': [
            {
              'title': 'Meet Joëlle on campus',
              'dueAt': _iso(now.add(const Duration(minutes: 46))),
            },
          ],
        },
      );
      expect(picks.first.label, 'Prepare for Meet Joëlle on campus');
    });

    test('the time of day changes the defaults', () {
      List<String> at(int hour) => homeSuggestions(
        now: DateTime(2026, 10, 16, hour),
      ).map((s) => s.label).toList();
      expect(at(14), contains("What's left today?"));
      expect(at(21), contains('Plan tomorrow'));
      expect(at(21).length, greaterThanOrEqualTo(2));
    });
  });

  group('needsYouItems', () {
    test('approvals, due reminders, people, then catch-ups', () async {
      final chats = await chatsWith([
        chat('+1', 'Sam', const Duration(minutes: 10), unread: 2),
        chat('+2', 'Rosa', const Duration(hours: 14), unread: 1),
        chat(
          'g@g.us',
          'Climbing crew',
          const Duration(hours: 3),
          unread: 9,
          group: true,
          catchUp: {
            'summary': 'Noor booked Saturday.',
            'toReply': [
              {'who': 'Noor', 'about': 'Are you in?'},
            ],
          },
        ),
        chat(
          'f@g.us',
          'Family',
          const Duration(hours: 1),
          unread: 3,
          group: true,
        ),
        chat(
          '+3',
          'Tom',
          const Duration(hours: 5),
          catchUp: {'summary': 'Tom moved the dinner.'},
        ),
      ]);
      final items = needsYouItems(
        now: now,
        chats: chats,
        max: 10,
        briefing: {
          'approvals': [
            {
              'id': 'a',
              'toolName': 'SendEmail',
              'createdAt': _iso(now.subtract(const Duration(minutes: 2))),
            },
          ],
          'reminders': [
            {
              'id': 'late',
              'title': 'Bring the charger',
              'dueAt': _iso(now.subtract(const Duration(minutes: 20))),
              'status': 'pending',
            },
            {
              'id': 'later',
              'title': 'Rosa at 17:00',
              'dueAt': _iso(now.add(const Duration(hours: 8))),
              'status': 'pending',
            },
          ],
          'tasks': [
            {'id': 't', 'title': 'Book flights', 'status': 'needs_approval'},
            {'id': 'u', 'title': 'Compare', 'status': 'running'},
          ],
        },
      );
      expect(items.map((i) => i.kind), [
        NeedsYouKind.approval,
        NeedsYouKind.reminder,
        NeedsYouKind.person,
        NeedsYouKind.person,
        NeedsYouKind.person,
        NeedsYouKind.task,
        NeedsYouKind.catchUp,
      ]);
      expect(items[1].detail, 'Overdue · 20 min');
      expect(items[1].overdue, isTrue);
      // People first, longest wait first; a group only when it asks you.
      expect(items.sublist(2, 5).map((i) => i.title), [
        'Rosa',
        'Sam',
        'Climbing crew',
      ]);
      expect(items[4].detail, startsWith('Noor asked: Are you in?'));
      expect(items.last.title, 'Catch up on Tom');
    });

    test('caps the list', () {
      final items = needsYouItems(
        now: now,
        briefing: {
          'approvals': [
            for (var i = 0; i < 8; i++) {'id': 'a$i', 'toolName': 'Tool'},
          ],
        },
      );
      expect(items, hasLength(5));
    });
  });
}
