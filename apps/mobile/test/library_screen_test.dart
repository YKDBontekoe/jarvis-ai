import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/library/library_models.dart';
import 'package:jarvis_mobile/features/library/library_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _item(String id, String title, String kind) => {
  'id': id,
  'kind': kind,
  'url': kind == 'web' ? 'https://example.com/post' : null,
  'title': title,
  'summary': 'About $title.',
  'keyPoints': ['one', 'two'],
  'tags': ['bees'],
  'content': 'Full text of $title.',
  'origin': 'app',
  'createdAt': '2026-10-03T10:00:00Z',
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/library', {
      'items': [_item('i1', 'Bees explained', 'web'), _item('i2', 'My idea', 'note')],
      'cards': {'total': 3, 'due': 2, 'new': 2, 'learned': 0},
    });
    http.on('GET', '/api/v1/library/cards/due', {
      'cards': [
        {'id': 'c1', 'front': 'What do bees make?', 'back': 'Honey'},
        {'id': 'c2', 'front': 'Where do they live?', 'back': 'Colonies'},
      ],
      'stats': {'total': 3, 'due': 2},
    });
  });

  Future<void> show(WidgetTester tester, {Future<void> Function(String)? open}) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: LibraryScreen(http: http.client(), openLink: open)),
    );
    await tester.pumpAndSettle();
  }

  test('models parse items and cards defensively', () {
    final item = LibraryItemData.fromJson(_item('i1', 'Bees', 'web'))!;
    expect(item.host, 'example.com');
    expect(item.tags, ['bees']);
    expect(libraryKindLabel('report'), 'Research report');
    expect(LibraryItemData.fromJson({'id': 'x'}), isNull);
    expect(FlashcardData.fromJson({'id': 'c', 'front': 'q'}), isNull);
    expect(CardStatsData.fromJson(null).due, 0);
  });

  testWidgets('lists saved items and opens one with its summary', (tester) async {
    http.on('GET', '/api/v1/library/i1', _item('i1', 'Bees explained', 'web'));
    String? opened;
    await show(tester, open: (url) async => opened = url);

    expect(find.text('Bees explained'), findsOneWidget);
    expect(find.text('My idea'), findsOneWidget);
    expect(find.text('Review (2)'), findsOneWidget);
    await tester.tap(find.byKey(const Key('library-item-i1')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Full text of Bees explained'), findsOneWidget);
    await tester.tap(find.byKey(const Key('library-open-link')));
    await tester.pumpAndSettle();
    expect(opened, 'https://example.com/post');
  });

  testWidgets('saving a link posts it and reloads', (tester) async {
    http.on('POST', '/api/v1/library/clip', _item('i3', 'New page', 'web'));
    await show(tester);

    await tester.tap(find.byKey(const Key('library-link')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('library-field-url')),
      'https://example.com/new',
    );
    await tester.tap(find.byKey(const Key('library-dialog-save')));
    await tester.pumpAndSettle();

    final post = http.sent('POST', '/api/v1/library/clip').single.body as Map;
    expect(post['url'], 'https://example.com/new');
    expect(http.sent('GET', '/api/v1/library').length, 2);
  });

  testWidgets('deep research starts a task', (tester) async {
    http.on('POST', '/api/v1/library/research', {'taskId': 't1', 'title': 'Research'}, status: 202);
    await show(tester);

    await tester.tap(find.byKey(const Key('library-research')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('library-field-question')),
      'How do bees survive winter?',
    );
    await tester.tap(find.byKey(const Key('library-dialog-save')));
    await tester.pumpAndSettle();

    final post = http.sent('POST', '/api/v1/library/research').single.body as Map;
    expect(post['question'], 'How do bees survive winter?');
    expect(find.textContaining('Research started'), findsOneWidget);
  });

  testWidgets('reviewing a card reveals the answer then grades it', (tester) async {
    http.on('POST', '/api/v1/library/cards/c1/review', {'id': 'c1', 'front': 'q', 'back': 'a'});
    await show(tester);

    await tester.tap(find.byKey(const Key('library-tab-review')));
    await tester.pumpAndSettle();
    expect(find.text('What do bees make?'), findsOneWidget);
    expect(find.byKey(const Key('flashcard-back')), findsNothing);
    await tester.tap(find.byKey(const Key('flashcard-reveal')));
    await tester.pumpAndSettle();
    expect(find.text('Honey'), findsOneWidget);
    await tester.tap(find.byKey(const Key('flashcard-good')));
    await tester.pumpAndSettle();

    final post = http.sent('POST', '/api/v1/library/cards/c1/review').single.body as Map;
    expect(post['button'], 'good');
    expect(find.text('Where do they live?'), findsOneWidget);
  });
}
