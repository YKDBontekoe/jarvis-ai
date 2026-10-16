import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';

import 'support/fixture_http.dart';

String _iso(DateTime time) => time.toUtc().toIso8601String();

Map<String, Object?> _chat(
  String id,
  String name,
  DateTime at, {
  int unread = 0,
  bool group = false,
  String? preview,
  bool fromMe = false,
}) => {
  'chatId': id,
  'name': name,
  'isGroup': group,
  'readAlong': true,
  'lastMessageAt': _iso(at),
  'preview': preview ?? 'hello from $name',
  'previewFromMe': fromMe,
  'unreadCount': unread,
};

void main() {
  final now = DateTime(2026, 10, 3, 18);
  late FixtureHttp http;
  late ChatList list;

  setUp(() {
    http = FixtureHttp();
    list = ChatList();
    list.setConversations([
      {
        'id': 'c1',
        'title': 'Lisbon trip',
        'updatedAt': _iso(now.subtract(const Duration(hours: 5))),
      },
      {
        'id': 'c2',
        'title': 'Pinned plan',
        'updatedAt': _iso(now.subtract(const Duration(days: 3))),
        'pinned': true,
        'profileName': 'Work',
      },
      {'title': 'No id'},
    ]);
  });

  void whatsApp() {
    http.on('GET', '/api/v1/channels', [
      {'id': 'wa', 'kind': 'whatsapp_linked', 'account': '+31600000000'},
      {'id': 'sig', 'kind': 'signal'},
    ]);
    http.on('GET', '/api/v1/channels/wa/chats', {
      'chats': [
        _chat(
          '+31611',
          'Sam',
          now.subtract(const Duration(minutes: 10)),
          unread: 2,
        ),
        _chat(
          'fam@g.us',
          'Family',
          now.subtract(const Duration(hours: 1)),
          group: true,
          unread: 3,
        ),
        _chat(
          '+31622',
          'Tom',
          now.subtract(const Duration(hours: 2)),
          fromMe: true,
          preview: 'Running late',
        ),
        {'chatId': '+31633', 'name': 'Never wrote'},
      ],
    });
  }

  test('Jarvis conversations alone, pinned first', () {
    expect(list.all.map((i) => i.title), ['Pinned plan', 'Lisbon trip']);
    expect(list.all.first.profile, 'Work');
    expect(list.all.first.preview, isNull);
    expect(list.all.every((i) => i.isJarvis), isTrue);
    expect(list.unreadCount, 0);
  });

  test('WhatsApp chats join the list, newest first after pinned', () async {
    whatsApp();
    await list.loadWhatsApp(http.client());
    expect(list.whatsAppLinked, isTrue);
    expect(list.all.map((i) => i.title), [
      'Pinned plan',
      'Sam',
      'Family',
      'Tom',
      'Lisbon trip',
    ]);
    final sam = list.all.firstWhere((i) => i.title == 'Sam');
    expect(sam.isJarvis, isFalse);
    expect(sam.unread, 2);
    expect(sam.channelId, 'wa');
    expect(sam.account, '+31600000000');
    expect(list.unreadCount, 5);
    final tom = list.all.firstWhere((i) => i.title == 'Tom');
    expect(tom.preview, 'You: Running late');
    expect(tom.fromMe, isTrue);
    expect(list.unreadChats, 2);
  });

  test('Jarvis rows read the last message, typing and reminder origin', () {
    list.setConversations([
      {
        'id': 'mail',
        'title': 'Mail access',
        'updatedAt': _iso(now),
        'lastMessage': 'I can read your Gmail.',
        'lastMessageRole': 'assistant',
        'responding': true,
      },
      {
        'id': 'mine',
        'title': 'Groceries',
        'updatedAt': _iso(now),
        'lastMessage': 'Add oat milk',
        'lastMessageRole': 'user',
      },
      {
        'id': 'r',
        'title': 'Meet Joëlle',
        'updatedAt': _iso(now),
        'origin': 'reminder',
        'originId': 'rem-1',
      },
    ]);
    final byId = {for (final item in list.all) item.conversationId: item};
    expect(byId['mail']!.preview, 'I can read your Gmail.');
    expect(byId['mail']!.responding, isTrue);
    expect(byId['mine']!.preview, 'You: Add oat milk');
    expect(byId['mine']!.fromMe, isTrue);
    expect(byId['r']!.fromReminder, isTrue);
    expect(byId['r']!.originId, 'rem-1');
  });

  test(
    'For you puts people waiting first, then Jarvis, then the quiet rest',
    () async {
      whatsApp();
      http.on('GET', '/api/v1/channels/wa/chats', {
        'chats': [
          _chat(
            '+31611',
            'Sam',
            now.subtract(const Duration(minutes: 10)),
            unread: 2,
          ),
          _chat(
            '+31644',
            'Rosa',
            now.subtract(const Duration(hours: 14)),
            unread: 1,
          ),
          _chat(
            'fam@g.us',
            'Family',
            now.subtract(const Duration(hours: 1)),
            group: true,
            unread: 3,
          ),
          {
            ..._chat(
              'men@g.us',
              'Mannen middag',
              now.subtract(const Duration(hours: 2)),
              group: true,
              unread: 14,
            ),
            'catchUp': {
              'summary': 'Bart is hosting on Saturday.',
              'toReply': [
                {'who': 'Bart', 'about': 'Kom je zaterdag?'},
              ],
            },
          },
          _chat(
            '+31622',
            'Tom',
            now.subtract(const Duration(hours: 2)),
            fromMe: true,
            preview: 'Running late',
          ),
        ],
      });
      list.setConversations([
        {'id': 'c1', 'title': 'Lisbon trip', 'updatedAt': _iso(now)},
        {
          'id': 'r1',
          'title': 'Meet Joëlle',
          'updatedAt': _iso(now),
          'origin': 'reminder',
        },
      ]);
      await list.loadWhatsApp(http.client());

      final groups = list.grouped(ChatFilter.forYou, now: now);

      expect(groups.map((g) => g.section), [
        ChatSection.waiting,
        ChatSection.jarvis,
        ChatSection.quiet,
      ]);
      // People before groups, longest wait first; a group that asks you counts.
      expect(groups[0].items.map((i) => i.title), [
        'Rosa',
        'Sam',
        'Mannen middag',
      ]);
      expect(groups[1].items.map((i) => i.title), ['Lisbon trip']);
      expect(groups[1].reminders.map((i) => i.title), ['Meet Joëlle']);
      expect(groups[2].items.map((i) => i.title), ['Tom']);
      expect(groups[2].busyGroups.map((i) => i.title), ['Family']);
    },
  );

  test('other filters group by day with pinned chats on top', () async {
    whatsApp();
    await list.loadWhatsApp(http.client());
    list.setConversations([
      {
        'id': 'p',
        'title': 'Pinned plan',
        'pinned': true,
        'updatedAt': _iso(now.subtract(const Duration(days: 30))),
      },
      {
        'id': 'y',
        'title': 'Yesterday chat',
        'updatedAt': _iso(now.subtract(const Duration(days: 1))),
      },
      {
        'id': 'w',
        'title': 'This week chat',
        'updatedAt': _iso(now.subtract(const Duration(days: 4))),
      },
      {
        'id': 'o',
        'title': 'Old chat',
        'updatedAt': _iso(now.subtract(const Duration(days: 40))),
      },
    ]);

    final groups = list.grouped(ChatFilter.all, now: now);

    expect(groups.map((g) => g.section), [
      ChatSection.pinned,
      ChatSection.today,
      ChatSection.yesterday,
      ChatSection.thisWeek,
      ChatSection.earlier,
    ]);
    expect(groups.first.items.single.title, 'Pinned plan');
    expect(groups[1].items.map((i) => i.title), ['Sam', 'Family', 'Tom']);
    // Searching gives one flat section.
    expect(list.grouped(ChatFilter.all, query: 'chat', now: now), hasLength(1));
  });

  test('pinning shows at once and goes back when the server refuses', () async {
    final lisbon = list.all.firstWhere((i) => i.title == 'Lisbon trip');
    http.on('PATCH', '/api/v1/conversations/c1', {'id': 'c1'});
    expect(await list.setPinned(http.client(), lisbon, true), isTrue);
    expect(list.all.first.title, 'Lisbon trip');
    expect(http.sent('PATCH', '/api/v1/conversations/c1').single.body, {
      'pinned': true,
    });

    http.on(
      'PATCH',
      '/api/v1/conversations/c1',
      const <String, Object>{},
      status: 500,
    );
    final pinned = list.all.firstWhere((i) => i.title == 'Lisbon trip');
    expect(await list.setPinned(http.client(), pinned, false), isFalse);
    expect(list.all.firstWhere((i) => i.title == 'Lisbon trip').pinned, isTrue);
  });

  test('marking read clears the count and restores it on failure', () async {
    whatsApp();
    await list.loadWhatsApp(http.client());
    final sam = list.all.firstWhere((i) => i.title == 'Sam');
    http.on(
      'POST',
      '/api/v1/channels/wa/chats/open/read',
      const <String, Object>{},
    );
    expect(await list.markRead(http.client(), sam), isTrue);
    expect(list.unreadChats, 1);
    expect(http.requests.last.query['chatId'], '+31611');

    final family = list.all.firstWhere((i) => i.title == 'Family');
    http.on(
      'POST',
      '/api/v1/channels/wa/chats/open/read',
      const <String, Object>{},
      status: 502,
    );
    expect(await list.markRead(http.client(), family), isFalse);
    expect(list.all.firstWhere((i) => i.title == 'Family').unread, 3);
  });

  test('filters narrow the list', () async {
    whatsApp();
    await list.loadWhatsApp(http.client());
    List<String> titles(ChatFilter filter, [String query = '']) =>
        list.filtered(filter, query).map((i) => i.title).toList();
    expect(titles(ChatFilter.unread), ['Sam', 'Family']);
    expect(titles(ChatFilter.jarvis), ['Pinned plan', 'Lisbon trip']);
    expect(titles(ChatFilter.whatsapp), ['Sam', 'Family', 'Tom']);
    expect(titles(ChatFilter.all, 'lis'), ['Lisbon trip']);
    expect(titles(ChatFilter.whatsapp, 'RUNNING'), ['Tom']);
    expect(titles(ChatFilter.unread, 'tom'), isEmpty);
  });

  test('no linked account leaves only Jarvis and says so', () async {
    http.on('GET', '/api/v1/channels', [
      {'id': 'sig', 'kind': 'signal'},
    ]);
    await list.loadWhatsApp(http.client());
    expect(list.whatsAppLinked, isFalse);
    expect(list.whatsApp, isEmpty);
    expect(list.loadingWhatsApp, isFalse);
  });

  test('an unreachable server keeps what was loaded', () async {
    whatsApp();
    await list.loadWhatsApp(http.client());
    http.on('GET', '/api/v1/channels', const <String, Object>{}, status: 500);
    await list.loadWhatsApp(http.client());
    expect(list.whatsApp, hasLength(3));
    expect(list.whatsAppLinked, isTrue);
    expect(list.whatsAppError, isTrue);
  });

  test('one failing account does not hide the others', () async {
    http.on('GET', '/api/v1/channels', [
      {'id': 'bad', 'kind': 'whatsapp_linked'},
      {'id': 'wa', 'kind': 'whatsapp_linked'},
    ]);
    http.on(
      'GET',
      '/api/v1/channels/bad/chats',
      const <String, Object>{},
      status: 502,
    );
    http.on('GET', '/api/v1/channels/wa/chats', {
      'chats': [_chat('+31611', 'Sam', now)],
    });
    await list.loadWhatsApp(http.client());
    expect(list.whatsApp.map((i) => i.title), ['Sam']);
  });

  test('clearing forgets everything and notifies listeners', () async {
    whatsApp();
    await list.loadWhatsApp(http.client());
    var notified = 0;
    list.addListener(() => notified++);
    list.clear();
    expect(list.all, isEmpty);
    expect(list.whatsAppLinked, isFalse);
    expect(notified, 1);
  });

  test('a late answer after clearing is dropped', () async {
    whatsApp();
    final pending = list.loadWhatsApp(http.client());
    list.clear();
    await pending;
    expect(list.whatsApp, isEmpty);
  });
}
