import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/library/lesson_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _path = '/api/v1/library/i1/lesson';

Map<String, Object?> _lesson() => {
  'itemId': 'i1',
  'title': 'Sourdough basics',
  'overview': 'How a sourdough starter works.',
  'minutes': 4,
  'note': 'Every point is a sentence from your saved text. Nothing here is invented.',
  'points': [
    {
      'text': 'Feeding the starter every day keeps the yeast active.',
      'source': {'quote': 'x', 'offset': 0},
    },
  ],
  'questions': [
    {
      'id': 'b1',
      'kind': 'blank',
      'prompt': 'Fill in the missing word: Feeding the starter every day keeps the _____ active.',
      'hint': 'It starts with “y”.',
      'source': {
        'quote': 'Feeding the starter every day keeps the yeast active.',
        'offset': 0,
      },
    },
    {
      'id': 'e1',
      'kind': 'explain_back',
      'prompt': 'In your own words, what is the main idea?',
      'hint': 'Mention key terms.',
      'source': {'quote': 'q', 'offset': 0},
    },
  ],
};

Map<String, Object?> _grade(String verdict, String message) => {
  'verdict': verdict,
  'expected': 'yeast',
  'source': {
    'quote': 'Feeding the starter every day keeps the yeast active.',
    'offset': 0,
  },
  'message': message,
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 3600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: LessonScreen(http: http.client(), itemId: 'i1'),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('shows quoted points, the honesty note and every question', (
    tester,
  ) async {
    http.on('GET', _path, _lesson());
    await show(tester);

    expect(find.textContaining('Nothing here is invented'), findsOneWidget);
    expect(
      find.textContaining(
        '“Feeding the starter every day keeps the yeast active.”',
      ),
      findsOneWidget,
    );
    expect(find.byKey(const Key('lesson-q-b1')), findsOneWidget);
    expect(find.byKey(const Key('lesson-q-e1')), findsOneWidget);
    expect(
      find.byKey(const Key('lesson-card-e1')),
      findsNothing,
    ); // only fill-in questions become cards
  });

  testWidgets('checking an answer shows the verdict and the source sentence', (
    tester,
  ) async {
    http
      ..on('GET', _path, _lesson())
      ..on(
        'POST',
        '$_path/check',
        _grade('incorrect', 'Your text says “yeast”.'),
      );
    await show(tester);

    await tester.enterText(
      find.byKey(const Key('lesson-answer-b1')),
      'pineapple',
    );
    await tester.tap(find.byKey(const Key('lesson-check-b1')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_path/check').single.body, {
      'questionId': 'b1',
      'answer': 'pineapple',
    });
    expect(find.textContaining('Not quite.'), findsOneWidget);
    expect(
      find.textContaining('Your text: “Feeding the starter'),
      findsOneWidget,
    );
  });

  testWidgets('a grade the owner disagrees with can be corrected', (
    tester,
  ) async {
    http
      ..on('GET', _path, _lesson())
      ..on(
        'POST',
        '$_path/check',
        _grade('incorrect', 'Your text says “yeast”.'),
      );
    await show(tester);
    await tester.enterText(
      find.byKey(const Key('lesson-answer-b1')),
      'sourdough culture',
    );
    await tester.tap(find.byKey(const Key('lesson-check-b1')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('lesson-correct-b1')));
    await tester.pumpAndSettle();

    expect(find.textContaining('Correct.'), findsOneWidget);
    expect(find.byKey(const Key('lesson-correct-b1')), findsNothing);
  });

  testWidgets('not knowing is welcome and offers no correction', (
    tester,
  ) async {
    http
      ..on('GET', _path, _lesson())
      ..on(
        'POST',
        '$_path/check',
        _grade('unsure', 'No problem. Here is what your text says.'),
      );
    await show(tester);

    await tester.tap(find.byKey(const Key('lesson-check-b1')));
    await tester.pumpAndSettle();

    expect(find.textContaining('No problem.'), findsWidgets);
    expect(find.byKey(const Key('lesson-correct-b1')), findsNothing);
  });

  testWidgets('chosen cards are kept for spaced review and the count says so', (
    tester,
  ) async {
    http
      ..on('GET', _path, _lesson())
      ..on('POST', '$_path/cards', {'added': 1, 'skipped': 0});
    await show(tester);
    expect(find.byKey(const Key('lesson-keep-cards')), findsNothing);

    await tester.tap(find.byKey(const Key('lesson-card-b1')));
    await tester.pumpAndSettle();
    expect(find.text('Keep 1 card for review'), findsOneWidget);
    await tester.tap(find.byKey(const Key('lesson-keep-cards')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_path/cards').single.body, {
      'questionIds': ['b1'],
    });
    expect(find.textContaining('Kept 1 card'), findsOneWidget);
    expect(find.byKey(const Key('lesson-keep-cards')), findsNothing);
  });

  testWidgets('text that is too thin for a lesson says so', (tester) async {
    http.on('GET', _path, {
      'detail': 'There is not enough text in this item to teach from.',
    }, status: 400);
    await show(tester);

    expect(find.textContaining('not enough text'), findsOneWidget);
  });
}
