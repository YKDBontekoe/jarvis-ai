import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/opportunities/opportunities_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _item(
  String key,
  String title, {
  String destination = 'meeting:m1',
}) => {
  'key': key,
  'kind': 'meeting_prep',
  'title': title,
  'whyNow': 'It starts soon.',
  'destination': destination,
  'also': <Object>['Also something related.'],
};

Map<String, Object?> _list({
  List<Map<String, Object?>> items = const [],
  List<Map<String, Object?>> history = const [],
  List<String> muted = const [],
}) => {'items': items, 'history': history, 'mutedKinds': muted};

void main() {
  late FixtureHttp http;
  String? opened;

  setUp(() {
    http = FixtureHttp();
    opened = null;
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: OpportunitiesScreen(
          http: http.client(),
          pageFor: (destination) {
            opened = destination;
            return destination == 'missing'
                ? null
                : const Scaffold(body: Text('destination page'));
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('an empty radar says nothing needs you, calmly', (tester) async {
    http.on('GET', '/api/v1/opportunities', _list());
    await show(tester);

    expect(find.text('Nothing needs you right now'), findsOneWidget);
  });

  testWidgets(
    'each card gives the reason, the related items and opens its destination',
    (tester) async {
      http.on(
        'GET',
        '/api/v1/opportunities',
        _list(items: [_item('meeting_prep:m1', 'Prepare for “Kitchen sync”')]),
      );
      await show(tester);

      expect(find.text('It starts soon.'), findsOneWidget);
      expect(find.text('Also: Also something related.'), findsOneWidget);
      await tester.tap(
        find.byKey(const Key('opportunity-open-meeting_prep:m1')),
      );
      await tester.pumpAndSettle();

      expect(opened, 'meeting:m1');
      expect(find.text('destination page'), findsOneWidget);
    },
  );

  testWidgets(
    'a destination that cannot be shown says so instead of failing silently',
    (tester) async {
      http.on(
        'GET',
        '/api/v1/opportunities',
        _list(items: [_item('k', 'Odd', destination: 'missing')]),
      );
      await show(tester);

      await tester.tap(find.byKey(const Key('opportunity-open-k')));
      await tester.pumpAndSettle();

      expect(find.text('That page is not available here.'), findsOneWidget);
    },
  );

  testWidgets('feedback posts the key and the verdict and reloads', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/opportunities', _list(items: [_item('k1', 'One')]))
      ..on('POST', '/api/v1/opportunities/feedback', null, status: 204);
    await show(tester);
    http.on('GET', '/api/v1/opportunities', _list());

    await tester.tap(find.byKey(const Key('opportunity-useful-k1')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/opportunities/feedback').single.body, {
      'key': 'k1',
      'verdict': 'useful',
    });
    expect(find.byKey(const Key('opportunity-k1')), findsNothing);
  });

  testWidgets(
    'the not-now menu offers later, handled, wrong assumption and fewer like this',
    (tester) async {
      http
        ..on('GET', '/api/v1/opportunities', _list(items: [_item('k1', 'One')]))
        ..on('POST', '/api/v1/opportunities/feedback', null, status: 204);
      await show(tester);

      await tester.tap(find.byKey(const Key('opportunity-more-k1')));
      await tester.pumpAndSettle();
      expect(find.text('Already handled'), findsOneWidget);
      expect(find.text('Wrong assumption'), findsOneWidget);
      await tester.tap(find.text('Fewer like this'));
      await tester.pumpAndSettle();

      expect(http.sent('POST', '/api/v1/opportunities/feedback').single.body, {
        'key': 'k1',
        'verdict': 'fewer',
      });
    },
  );

  testWidgets(
    'a quieted kind can be brought back and useful ones are listed as history',
    (tester) async {
      http
        ..on(
          'GET',
          '/api/v1/opportunities',
          _list(
            muted: ['follow_up'],
            history: [
              {'title': 'Plan time for: Send the deck'},
            ],
          ),
        )
        ..on('POST', '/api/v1/opportunities/unmute', null, status: 204);
      await show(tester);

      expect(find.text('Fewer follow-ups for now'), findsOneWidget);
      expect(find.text('· Plan time for: Send the deck'), findsOneWidget);
      await tester.tap(find.byKey(const Key('opportunity-unmute-follow_up')));
      await tester.pumpAndSettle();

      expect(http.sent('POST', '/api/v1/opportunities/unmute').single.body, {
        'kind': 'follow_up',
      });
    },
  );
}
