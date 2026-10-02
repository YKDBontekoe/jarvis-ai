import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/reading/reading_list_screen.dart';
import 'package:jarvis_mobile/features/reading/reading_models.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _item(
  String id,
  String title, {
  String status = 'ready',
  bool read = false,
  int? minutes = 6,
  String? summary = 'A calm look at why tomatoes need sun.',
  List<String> keyPoints = const ['Six hours of sun', 'Water at the root'],
  String? failureReason,
}) => {
  'id': id,
  'url': 'https://www.garden.example/$id',
  'status': status,
  'title': title,
  'siteName': 'Garden Weekly',
  'excerpt': null,
  'summary': summary,
  'keyPoints': keyPoints,
  'wordCount': 1200,
  'readingMinutes': minutes,
  'note': null,
  'source': 'app',
  'failureReason': failureReason,
  'read': read,
  'readAt': read ? '2026-10-01T19:00:00Z' : null,
  'fetchedAt': '2026-10-01T18:00:00Z',
  'createdAt': '2026-10-01T17:00:00Z',
  'updatedAt': '2026-10-01T18:00:00Z',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  // Pending links show a spinner that never settles, so pump a few frames.
  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 6; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  test('items parse from the API and totals read naturally', () {
    final items = ReadingItemData.listFromJson([
      _item('a', 'Tomatoes'),
      _item('b', 'Herbs', minutes: 4),
      {'title': 'no id'},
    ]);
    expect(items.map((x) => x.title), ['Tomatoes', 'Herbs']);
    expect(items.first.source, 'Garden Weekly');
    expect(items.first.host, 'garden.example');
    expect(readingTotalsLabel(items), '2 unread · about 10 min');
    expect(readingTotalsLabel(const []), 'All caught up');
    expect(readingTimeLabel(6), '6 min read');
    final pending = ReadingItemData.fromJson({
      'id': 'p',
      'url': 'https://www.news.example/story/',
      'title': 'https://www.news.example/story/',
    })!;
    expect(pending.title, 'news.example/story');
  });

  test('links are found inside pasted text', () {
    expect(
      extractLink('Lees dit: https://example.com/a?b=1.'),
      'https://example.com/a?b=1',
    );
    expect(extractLink('example.org/post'), 'example.org/post');
    expect(extractLink('no link here'), isNull);
  });

  testWidgets('unread links show their summary, reading time and key points', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reading', [
      _item('a', 'Growing tomatoes'),
      _item('b', 'Old news', read: true),
    ]);
    await show(tester, ReadingListScreen(http: http.client()));

    expect(find.text('1 unread · about 6 min'), findsOneWidget);
    expect(find.text('Growing tomatoes'), findsOneWidget);
    expect(find.text('6 min read'), findsOneWidget);
    expect(find.text('A calm look at why tomatoes need sun.'), findsOneWidget);
    expect(find.text('Old news'), findsNothing);
    expect(find.text('Six hours of sun'), findsNothing);

    await tester.tap(find.byKey(const Key('reading-expand-a')));
    await tester.pumpAndSettle();
    expect(find.text('Six hours of sun'), findsOneWidget);

    await tester.tap(find.text('Read · 1'));
    await tester.pumpAndSettle();
    expect(find.text('Old news'), findsOneWidget);
    expect(find.text('Growing tomatoes'), findsNothing);
  });

  testWidgets('saving a link adds it as pending and polls until it is read', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reading', <Object>[]);
    http.on(
      'POST',
      '/api/v1/reading',
      _item(
        'n',
        'https://news.example/x',
        status: 'pending',
        minutes: null,
        summary: null,
        keyPoints: const [],
      ),
      status: 201,
    );
    await show(
      tester,
      ReadingListScreen(
        http: http.client(),
        pollInterval: const Duration(seconds: 2),
      ),
    );
    expect(find.text('Nothing saved yet'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('reading-link')),
      'Look at https://news.example/x',
    );
    await tester.pump();
    await tester.tap(find.byKey(const Key('reading-save')));
    await settle(tester);

    expect(http.sent('POST', '/api/v1/reading').single.body, {
      'url': 'https://news.example/x',
    });
    expect(find.text('Jarvis is reading the page…'), findsOneWidget);

    http.on('GET', '/api/v1/reading', [_item('n', 'The finished article')]);
    await tester.pump(const Duration(seconds: 2));
    await settle(tester);
    await tester.pumpAndSettle();
    expect(find.text('The finished article'), findsOneWidget);
    expect(find.text('Jarvis is reading the page…'), findsNothing);
  });

  testWidgets('a rejected link shows the server message', (tester) async {
    http.on('GET', '/api/v1/reading', <Object>[]);
    http.on('POST', '/api/v1/reading', {
      'errors': {
        'url': ['The reading list cannot open local or private hosts.'],
      },
    }, status: 400);
    await show(tester, ReadingListScreen(http: http.client()));

    await tester.enterText(
      find.byKey(const Key('reading-link')),
      'http://192.168.1.1',
    );
    await tester.pump();
    await tester.tap(find.byKey(const Key('reading-save')));
    await tester.pumpAndSettle();

    expect(
      find.text('The reading list cannot open local or private hosts.'),
      findsOneWidget,
    );
  });

  testWidgets('pasting from the clipboard saves the link', (tester) async {
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      (call) async => call.method == 'Clipboard.getData'
          ? {'text': 'see https://paste.example/read'}
          : null,
    );
    addTearDown(
      () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        null,
      ),
    );
    http.on('GET', '/api/v1/reading', <Object>[]);
    http.on(
      'POST',
      '/api/v1/reading',
      _item('p', 'Pasted', status: 'pending', minutes: null),
      status: 201,
    );
    await show(
      tester,
      ReadingListScreen(
        http: http.client(),
        pollInterval: const Duration(days: 1),
      ),
    );

    await tester.tap(find.byKey(const Key('reading-paste')));
    await settle(tester);

    expect(http.sent('POST', '/api/v1/reading').single.body, {
      'url': 'https://paste.example/read',
    });
  });

  testWidgets('marking read moves the link and failed links can retry', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reading', [
      _item('a', 'Growing tomatoes'),
      _item(
        'f',
        'Paywalled',
        status: 'failed',
        minutes: null,
        summary: null,
        failureReason: 'The site does not let Jarvis read this page.',
      ),
    ]);
    http.on(
      'PATCH',
      '/api/v1/reading/a',
      _item('a', 'Growing tomatoes', read: true),
    );
    http.on(
      'POST',
      '/api/v1/reading/f/refresh',
      _item('f', 'Paywalled', status: 'pending', minutes: null, summary: null),
    );
    await show(
      tester,
      ReadingListScreen(
        http: http.client(),
        pollInterval: const Duration(days: 1),
      ),
    );

    expect(
      find.text('The site does not let Jarvis read this page.'),
      findsOneWidget,
    );
    await tester.tap(find.text('Try again'));
    await settle(tester);
    expect(http.sent('POST', '/api/v1/reading/f/refresh'), hasLength(1));
    expect(find.text('Jarvis is reading the page…'), findsOneWidget);

    await tester.tap(find.byKey(const Key('reading-toggle-a')));
    await settle(tester);
    expect(http.sent('PATCH', '/api/v1/reading/a').single.body, {'read': true});
    expect(find.text('Growing tomatoes'), findsNothing);
    expect(find.text('Marked as read'), findsOneWidget);
  });

  testWidgets('summarize sends the prompt to chat', (tester) async {
    String? asked;
    http.on('GET', '/api/v1/reading', [_item('a', 'Growing tomatoes')]);
    await show(
      tester,
      ReadingListScreen(
        http: http.client(),
        onAskInChat: (text) => asked = text,
      ),
    );

    await tester.tap(find.byKey(const Key('reading-ask-jarvis')));
    await tester.pumpAndSettle();
    expect(asked, readingSummaryPrompt);
  });
}
