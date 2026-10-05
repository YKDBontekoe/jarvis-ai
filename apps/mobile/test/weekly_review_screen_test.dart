import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/review/mood_trend_chart.dart';
import 'package:jarvis_mobile/features/review/weekly_review_format.dart';
import 'package:jarvis_mobile/features/review/weekly_review_screen.dart';
import 'package:jarvis_mobile/features/notifications/notification_routing.dart';
import 'package:jarvis_mobile/schedule_format.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _stats({double? mood = 4.2, double? previous = 3.7}) => {
  'journalEntries': 3,
  'mood': mood,
  'energy': 3.0,
  'stress': 2.3,
  'rating': 7.3,
  'previousMood': previous,
  'bestDay': '2026-09-30',
  'bestDayRating': 9,
  'tasksCompleted': 4,
  'remindersHandled': 6,
  'remindersUpcoming': 2,
  'newMemories': 5,
  'topTags': ['run', 'family'],
  'days': [
    {'date': '2026-09-28', 'mood': 4, 'energy': 3, 'stress': 2, 'rating': 7},
    {'date': '2026-09-30', 'mood': 5, 'energy': 4, 'stress': 1, 'rating': 9},
    {'date': '2026-10-03', 'mood': 3, 'energy': 2, 'stress': 4, 'rating': 6},
  ],
};

Map<String, Object?> _review(String id, String week, String story) => {
  'id': id,
  'weekStart': week,
  'weekEnd': week,
  'story': story,
  'narrated': true,
  'stats': _stats(),
  'createdAt': '2026-10-04T17:00:00Z',
  'updatedAt': '2026-10-04T17:00:00Z',
  'notifiedAt': '2026-10-04T17:00:00Z',
};

Map<String, Object?> _overview({
  List<Map<String, Object?>> reviews = const [],
  bool enabled = true,
  String zone = 'Europe/Amsterdam',
}) => {
  'settings': {'enabled': enabled, 'localTime': '19:00:00', 'timeZoneId': zone},
  'currentWeekStart': '2026-09-28',
  'nextDeliveryAt': enabled ? '2026-10-04T17:00:00Z' : null,
  'trend': [
    for (final (index, week) in [
      '2026-08-10',
      '2026-08-17',
      '2026-08-24',
      '2026-08-31',
      '2026-09-07',
      '2026-09-14',
      '2026-09-21',
      '2026-09-28',
    ].indexed)
      {
        'weekStart': week,
        'entries': index == 3 ? 0 : 2,
        'mood': index == 3 ? null : 3 + index / 7,
        'energy': index == 3 ? null : 3.0,
        'stress': null,
        'rating': null,
      },
  ],
  'reviews': reviews,
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(
    WidgetTester tester, {
    Size size = const Size(900, 2400),
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: WeeklyReviewScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  test('week labels, averages and changes read naturally', () {
    final now = DateTime(2026, 10, 1);
    expect(formatWeekRange(DateTime(2026, 9, 28), now: now), '28 Sep – 4 Oct');
    expect(
      formatWeekRange(DateTime(2025, 12, 29), now: now),
      '29 Dec 2025 – 4 Jan 2026',
    );
    expect(formatAverage(4.25), '4.3');
    expect(formatAverage(null), '–');
    expect(changeLabel(4.2, 3.7), '+0.5 on last week');
    expect(changeLabel(3.0, 3.5), '−0.5 on last week');
    expect(changeLabel(3.0, 3.02), 'Same as last week');
    expect(changeLabel(3.0, null), isNull);
    expect(opensWeeklyReview('briefing.weekly'), isTrue);
    expect(opensWeeklyReview('briefing.daily'), isFalse);
  });

  testWidgets('shows the story, the numbers, the trend and earlier weeks', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    http.on(
      'GET',
      '/api/v1/reviews/weekly',
      _overview(
        reviews: [
          _review('r2', '2026-09-28', 'A bright week with long runs.'),
          _review('r1', '2026-09-21', 'A heavier week, but you rested.'),
        ],
      ),
    );
    await show(tester);

    expect(find.text('Your week in review'), findsOneWidget);
    expect(
      find.descendant(
        of: find.byType(SelectableText),
        matching: find.text('A bright week with long runs.'),
      ),
      findsOneWidget,
    );
    expect(find.text('+0.5 on last week'), findsOneWidget);
    expect(find.text('Tasks finished'), findsOneWidget);
    expect(find.text('#run'), findsOneWidget);
    expect(find.text('Mood over 8 weeks'), findsOneWidget);
    expect(find.byType(MoodTrendChart), findsOneWidget);
    expect(
      tester.getSemantics(find.byType(MoodTrendChart)).label,
      contains(
        'Mood by week out of 5: 10 Aug 3.0, 17 Aug 3.1, 24 Aug 3.3, '
        '31 Aug no entries',
      ),
    );

    await tester.tap(find.text('A heavier week, but you rested.').last);
    await tester.pumpAndSettle();
    expect(find.text('Looking back'), findsOneWidget);
    expect(
      find.descendant(
        of: find.byType(SelectableText),
        matching: find.text('A heavier week, but you rested.'),
      ),
      findsOneWidget,
    );
    semantics.dispose();
  });

  testWidgets('writes a review on demand before the first Sunday', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reviews/weekly', _overview());
    http.on(
      'POST',
      '/api/v1/reviews/weekly/generate',
      _review('r1', '2026-09-28', 'Fresh story.'),
    );
    await show(tester);

    expect(find.text('Your week, in one look'), findsOneWidget);
    await tester.tap(find.byKey(const Key('weekly-review-generate')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/reviews/weekly/generate'), hasLength(1));
  });

  testWidgets('turning it on saves with this device time zone', (tester) async {
    final previous = deviceTimeZoneLookup;
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
    addTearDown(() => deviceTimeZoneLookup = previous);
    http.on(
      'GET',
      '/api/v1/reviews/weekly',
      _overview(enabled: false, zone: 'UTC'),
    );
    http.on('PUT', '/api/v1/reviews/weekly/settings', {});
    await show(tester);

    await tester.tap(find.byKey(const Key('weekly-review-enabled')));
    await tester.pumpAndSettle();

    final body = http
        .sent('PUT', '/api/v1/reviews/weekly/settings')
        .single
        .body;
    expect(body, {
      'enabled': true,
      'localTime': '19:00:00',
      'timeZoneId': 'Europe/Amsterdam',
    });
  });

  testWidgets('fits a small phone with large text', (tester) async {
    http.on(
      'GET',
      '/api/v1/reviews/weekly',
      _overview(reviews: [_review('r1', '2026-09-28', 'Story.')]),
    );
    tester.platformDispatcher.textScaleFactorTestValue = 2;
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    await show(tester, size: const Size(360, 2600));

    expect(tester.takeException(), isNull);
  });

  testWidgets('settled decisions show with how the predictions scored', (
    tester,
  ) async {
    http.on(
      'GET',
      '/api/v1/reviews/weekly',
      _overview(
        reviews: [
          {
            ..._review('r2', '2026-09-28', 'A bright week.'),
            'stats': {
              ..._stats(),
              'decisionsResolved': 3,
              'brierScore': 0.12,
              'previousBrierScore': 0.30,
            },
          },
        ],
      ),
    );
    await show(tester);

    expect(find.text('Decisions settled'), findsOneWidget);
    expect(find.text('Score 0.12 · better than before'), findsOneWidget);
  });

  testWidgets('a week without settled decisions has no decisions tile', (
    tester,
  ) async {
    http.on(
      'GET',
      '/api/v1/reviews/weekly',
      _overview(reviews: [_review('r2', '2026-09-28', 'A bright week.')]),
    );
    await show(tester);

    expect(find.text('Decisions settled'), findsNothing);
  });
}
