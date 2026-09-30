import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/journal/journal_editor_screen.dart';
import 'package:jarvis_mobile/features/journal/journal_format.dart';
import 'package:jarvis_mobile/features/journal/journal_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _entry(
  String id,
  String date, {
  String content = 'Walked with Anna.',
  String source = 'written',
  int? mood,
  int? rating,
}) => {
  'id': id,
  'entryDate': date,
  'source': source,
  'content': content,
  'highlights': null,
  'gratitude': null,
  'rating': rating,
  'mood': mood,
  'energy': null,
  'stress': null,
  'tags': ['family'],
  'memoryId': 'm-$id',
  'createdAt': '2026-09-30T18:00:00Z',
  'updatedAt': '2026-09-30T18:00:00Z',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  test('journal dates and tags are formatted for the API and the UI', () {
    expect(journalDateKey(DateTime(2026, 9, 5)), '2026-09-05');
    expect(parseJournalDate('2026-09-30T00:00:00Z'), DateTime(2026, 9, 30));
    expect(parseJournalDate('nope'), isNull);
    expect(parseJournalDate(null), isNull);
    final now = DateTime(2026, 9, 30, 12);
    expect(formatJournalDate(DateTime(2026, 9, 30), now: now), 'Today');
    expect(formatJournalDate(DateTime(2026, 9, 29), now: now), 'Yesterday');
    expect(
      formatJournalDate(DateTime(2026, 9, 23), now: now),
      'Wednesday, 23 September',
    );
    expect(
      parseJournalTags('#Work, family,, work , #  '),
      equals(['work', 'family']),
    );
    expect(moodEmoji(5), '😄');
    expect(moodEmoji(9), '');
  });

  testWidgets('lists entries with ratings, source, and the summary', (
    tester,
  ) async {
    http.on('GET', '/api/v1/journal', [
      _entry('1', '2026-09-30', mood: 4, rating: 8, source: 'voice'),
      _entry('2', '2026-09-20', content: 'Quiet weekend.'),
    ]);
    http.on('GET', '/api/v1/journal/summary', {
      'days': 30,
      'totalEntries': 2,
      'currentStreak': 3,
      'averageMood': 4.0,
    });
    await show(tester, JournalScreen(http: http.client()));

    expect(find.text('Walked with Anna.'), findsOneWidget);
    expect(find.text('Quiet weekend.'), findsOneWidget);
    expect(find.text('Told to Jarvis'), findsOneWidget);
    expect(find.textContaining('Mood 4/5'), findsOneWidget);
    expect(find.text('Day 8/10'), findsOneWidget);
    expect(find.text('3'), findsOneWidget);
    expect(find.text('4.0'), findsOneWidget);
  });

  testWidgets('empty journal offers to talk or write and starts a chat', (
    tester,
  ) async {
    http.on('GET', '/api/v1/journal', <Object>[]);
    String? asked;
    await show(
      tester,
      Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => JournalScreen(
                    http: http.client(),
                    onTalkAboutDay: (prompt) => asked = prompt,
                  ),
                ),
              ),
              child: const Text('open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    expect(find.text('Your journal is empty'), findsOneWidget);
    await tester.tap(find.byKey(const Key('journal-talk')));
    await tester.pumpAndSettle();

    expect(asked, journalTalkPrompt);
    expect(find.text('Your journal is empty'), findsNothing);
  });

  testWidgets('shows a retryable error when the journal cannot load', (
    tester,
  ) async {
    await show(tester, JournalScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('Could not load your journal.'), findsOneWidget);
  });

  testWidgets('saving a new entry posts text, ratings, and tags', (
    tester,
  ) async {
    http.on('POST', '/api/v1/journal', _entry('9', '2026-09-30'));
    await show(tester, JournalEditorScreen(http: http.client()));

    await tester.enterText(
      find.byKey(const Key('journal-content')),
      '  Shipped the release.  ',
    );
    await tester.enterText(
      find.byKey(const Key('journal-tags')),
      '#Work, team',
    );
    await tester.tap(find.byKey(const Key('rating-day-8')));
    await tester.tap(find.byKey(const Key('rating-mood-4')));
    await tester.tap(find.byKey(const Key('rating-stress-2')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('journal-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/journal').single.body as Map;
    expect(body['content'], 'Shipped the release.');
    expect(body['rating'], 8);
    expect(body['mood'], 4);
    expect(body['energy'], isNull);
    expect(body['stress'], 2);
    expect(body['tags'], ['work', 'team']);
    expect(body['entryDate'], matches(RegExp(r'^\d{4}-\d{2}-\d{2}$')));
  });

  testWidgets('tapping a selected rating clears it', (tester) async {
    http.on('POST', '/api/v1/journal', _entry('9', '2026-09-30'));
    await show(tester, JournalEditorScreen(http: http.client()));

    await tester.enterText(find.byKey(const Key('journal-content')), 'Fine.');
    await tester.tap(find.byKey(const Key('rating-mood-3')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('rating-mood-3')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('journal-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/journal').single.body as Map;
    expect(body['mood'], isNull);
  });

  testWidgets('an empty entry is not sent', (tester) async {
    await show(tester, JournalEditorScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('journal-save')));
    await tester.pumpAndSettle();

    expect(http.requests, isEmpty);
    expect(
      find.text('Write something or add a rating before saving.'),
      findsOneWidget,
    );
  });

  testWidgets('editing an entry puts to its id', (tester) async {
    http.on('PUT', '/api/v1/journal/1', _entry('1', '2026-09-30'));
    await show(
      tester,
      JournalEditorScreen(
        http: http.client(),
        entry: _entry('1', '2026-09-30', mood: 2),
      ),
    );

    expect(find.text('Walked with Anna.'), findsOneWidget);
    await tester.enterText(
      find.byKey(const Key('journal-content')),
      'Walked with Anna and Sam.',
    );
    await tester.tap(find.byKey(const Key('journal-save')));
    await tester.pumpAndSettle();

    final body = http.sent('PUT', '/api/v1/journal/1').single.body as Map;
    expect(body['content'], 'Walked with Anna and Sam.');
    expect(body['mood'], 2);
    expect(body['tags'], ['family']);
  });

  testWidgets('server validation problems are shown on the form', (
    tester,
  ) async {
    http.on('POST', '/api/v1/journal', {
      'title': 'Validation failed',
      'errors': {
        'mood': ['Mood must be between 1 and 5.'],
      },
    }, status: 400);
    await show(tester, JournalEditorScreen(http: http.client()));

    await tester.enterText(find.byKey(const Key('journal-content')), 'Hi');
    await tester.tap(find.byKey(const Key('journal-save')));
    await tester.pumpAndSettle();

    expect(find.text('Mood must be between 1 and 5.'), findsOneWidget);
  });
}
