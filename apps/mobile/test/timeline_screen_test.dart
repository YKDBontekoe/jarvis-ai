import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/timeline/timeline_models.dart';
import 'package:jarvis_mobile/features/timeline/timeline_screen.dart';

import 'support/fixture_http.dart';

final _now = DateTime(2026, 10, 3, 12);

Map<String, Object?> _event(
  String id,
  String kind,
  String title,
  String date, {
  String? detail,
}) => {
  'id': id,
  'kind': kind,
  'title': title,
  'detail': detail,
  'at': '${date}T10:00:00Z',
  'date': date,
  'targetId': null,
  'amount': null,
  'currency': null,
};

Map<String, Object?> _timeline() => {
  'from': '2026-09-04',
  'to': '2026-10-03',
  'total': 3,
  'truncated': false,
  'failedKinds': <String>[],
  'days': [
    {
      'date': '2026-10-03',
      'events': [
        _event('journal:1', 'journal', 'Journal entry', '2026-10-03',
            detail: 'mood 4/5 · great run'),
        _event('expense:1', 'expense', 'Spent €12.00 at Bagels', '2026-10-03'),
      ],
    },
    {
      'date': '2026-10-02',
      'events': [_event('habit:1', 'habit', 'Did Exercise', '2026-10-02')],
    },
  ],
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: TimelineScreen(http: http.client(), now: _now),
      ),
    );
    await tester.pumpAndSettle();
  }

  test('timeline data parses days and skips malformed events', () {
    final data = TimelineData.fromJson({
      ..._timeline(),
      'days': [
        {
          'date': '2026-10-03',
          'events': [
            _event('a', 'journal', 'Journal entry', '2026-10-03'),
            {'id': 'broken'},
          ],
        },
        {'date': 'not a date', 'events': <Object>[]},
      ],
    })!;
    expect(data.days, hasLength(1));
    expect(data.days.single.events, hasLength(1));
    expect(timelineKindLabel('expense'), 'Spending');
  });

  testWidgets('shows days, events, patterns and on this day', (tester) async {
    http.on('GET', '/api/v1/timeline', _timeline());
    http.on('GET', '/api/v1/timeline/insights', {
      'insights': [
        {
          'headline': 'Mood is higher on days with more habit follow-through',
          'detail': 'Across 20 days, mood averaged 4.2.',
        },
      ],
    });
    http.on('GET', '/api/v1/timeline/on-this-day', [
      {
        'year': 2025,
        'date': '2025-10-03',
        'events': [_event('journal:9', 'journal', 'Last year entry', '2025-10-03')],
      },
    ]);
    await show(tester);

    expect(find.text('Today'), findsOneWidget);
    expect(find.text('Yesterday'), findsOneWidget);
    expect(find.text('Journal entry'), findsOneWidget);
    expect(find.text('Spent €12.00 at Bagels'), findsOneWidget);
    expect(find.text('Did Exercise'), findsOneWidget);
    expect(find.textContaining('Mood is higher'), findsOneWidget);
    expect(find.text('Patterns, not proof of cause.'), findsOneWidget);
    expect(find.text('On this day'), findsOneWidget);
    expect(find.textContaining('Last year entry'), findsOneWidget);
    expect(http.requests.first.query['from'], '2026-09-04');
    expect(http.requests.first.query['to'], '2026-10-03');
  });

  testWidgets('kind filters and range reload the timeline', (tester) async {
    http.on('GET', '/api/v1/timeline', _timeline());
    await show(tester);

    await tester.tap(find.byKey(const Key('timeline-kind-expense')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('timeline-range-7')));
    await tester.pumpAndSettle();

    final timelineRequests = http.sent('GET', '/api/v1/timeline').toList();
    expect(timelineRequests.last.query['kinds'], 'expense');
    expect(timelineRequests.last.query['from'], '2026-09-27');
  });

  testWidgets('an empty timeline explains itself', (tester) async {
    http.on('GET', '/api/v1/timeline', {
      ..._timeline(),
      'days': <Object>[],
      'total': 0,
    });
    await show(tester);

    expect(find.text('Nothing on your timeline yet'), findsOneWidget);
  });
}
