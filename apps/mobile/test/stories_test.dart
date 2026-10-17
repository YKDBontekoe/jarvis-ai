import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/stories/stories_screen.dart';
import 'package:jarvis_mobile/features/stories/story_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> story({
  bool removed = false,
  String text = 'Book the plumber',
}) => {
  'id': 's1',
  'title': 'Recap: 18 Sep – 17 Oct 2026',
  'from': '2026-09-18',
  'to': '2026-10-17',
  'version': 1,
  'savedVersion': 0,
  'counts': {'task': 1},
  'items': [
    {
      'id': 'i1',
      'kind': 'task',
      'date': '2026-10-02',
      'text': text,
      'originalText': 'Book the plumber',
      'sourceRef': 'task:t1',
      'removed': removed,
    },
  ],
  'patterns': <Object>[],
  'patternsNote':
      'There are too few days with data to say anything about patterns.',
  'reflection': 'What mattered?',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets('an empty list invites a first recap and creating one opens it', (
    tester,
  ) async {
    http.on('GET', '/api/v1/stories', <Object>[]);
    http.on('POST', '/api/v1/stories', story(), status: 201);
    http.on('GET', '/api/v1/stories/s1', story());
    await show(tester, StoriesScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('story-start')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('story-include-contact')));
    await tester.tap(find.byKey(const Key('story-style-retrospective')));
    await tester.tap(find.byKey(const Key('story-create')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/stories').single.body as Map;
    expect(body['style'], 'retrospective');
    expect(body['kinds'], ['decision', 'task', 'journal', 'contact']);
    expect(find.text('Book the plumber'), findsOneWidget);
  });

  testWidgets('money stays out of a new story unless asked for', (
    tester,
  ) async {
    http.on('GET', '/api/v1/stories', <Object>[]);
    http.on('POST', '/api/v1/stories', story(), status: 201);
    http.on('GET', '/api/v1/stories/s1', story());
    await show(tester, StoriesScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('story-start')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('story-create')));
    await tester.pumpAndSettle();

    expect(
      (http.sent('POST', '/api/v1/stories').single.body as Map).containsKey(
        'kinds',
      ),
      isFalse,
    );
  });

  testWidgets(
    'wording can be edited and a moment left out, with the honest note shown',
    (tester) async {
      http.on('GET', '/api/v1/stories/s1', story());
      http.on(
        'PUT',
        '/api/v1/stories/s1/items/i1',
        story(text: 'We booked the plumber'),
      );
      http.on(
        'PUT',
        '/api/v1/stories/s1/items/i1/removed',
        story(removed: true),
      );
      await show(tester, StoryScreen(http: http.client(), storyId: 's1'));

      expect(find.byKey(const Key('story-patterns-note')), findsOneWidget);
      expect(find.byKey(const Key('story-reflection')), findsOneWidget);
      await tester.tap(find.byKey(const Key('story-edit-i1')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('story-edit-field')),
        'We booked the plumber',
      );
      await tester.tap(find.byKey(const Key('story-edit-save')));
      await tester.pumpAndSettle();
      expect(find.text('We booked the plumber'), findsOneWidget);
      expect(find.textContaining('edited'), findsOneWidget);

      await tester.tap(find.byKey(const Key('story-remove-i1')));
      await tester.pumpAndSettle();
      expect(find.text('Put back'), findsOneWidget);
      expect(
        (http.sent('PUT', '/api/v1/stories/s1/items/i1/removed').single.body
            as Map)['removed'],
        true,
      );
    },
  );

  testWidgets(
    'save writes privately and the shareable copy asks to leave out what was chosen',
    (tester) async {
      http.on('GET', '/api/v1/stories/s1', story());
      http.on('POST', '/api/v1/stories/s1/save', {
        ...story(),
        'savedVersion': 1,
      });
      http.on('GET', '/api/v1/stories/s1/export', '# Recap');
      await show(tester, StoryScreen(http: http.client(), storyId: 's1'));

      await tester.tap(find.byKey(const Key('story-save')));
      await tester.pumpAndSettle();
      expect(find.textContaining('saved privately'), findsWidgets);

      await tester.tap(find.byKey(const Key('story-leave-journal')));
      await tester.tap(find.byKey(const Key('story-leave-people')));
      await tester.tap(find.byKey(const Key('story-export')));
      await tester.pumpAndSettle();
      expect(
        http.sent('GET', '/api/v1/stories/s1/export').single.query['redact'],
        'journal,people',
      );
    },
  );
}
