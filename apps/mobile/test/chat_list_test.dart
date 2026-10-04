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
      {
        'id': 'wa',
        'kind': 'whatsapp_linked',
        'account': '+31600000000',
      },
      {'id': 'sig', 'kind': 'signal'},
    ]);
    http.on('GET', '/api/v1/channels/wa/chats', {
      'chats': [
        _chat('+31611', 'Sam', now.subtract(const Duration(minutes: 10)), unread: 2),
        _chat('fam@g.us', 'Family', now.subtract(const Duration(hours: 1)),
            group: true, unread: 3),
        _chat('+31622', 'Tom', now.subtract(const Duration(hours: 2)),
            fromMe: true, preview: 'Running late'),
        {'chatId': '+31633', 'name': 'Never wrote'},
      ],
    });
  }

  test('Jarvis conversations alone, pinned first', () {
    expect(list.all.map((i) => i.title), ['Pinned plan', 'Lisbon trip']);
    expect(list.all.first.preview, 'Work');
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
  });

  test('one failing account does not hide the others', () async {
    http.on('GET', '/api/v1/channels', [
      {'id': 'bad', 'kind': 'whatsapp_linked'},
      {'id': 'wa', 'kind': 'whatsapp_linked'},
    ]);
    http.on('GET', '/api/v1/channels/bad/chats', const <String, Object>{}, status: 502);
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
