import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/notifications/notification_routing.dart';
import 'package:jarvis_mobile/features/people/people_models.dart';
import 'package:jarvis_mobile/features/people/people_screen.dart';
import 'package:jarvis_mobile/features/people/person_detail_screen.dart';
import 'package:jarvis_mobile/features/people/radar_models.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _person(String id, String name) => {
  'id': id,
  'name': name,
  'relationship': null,
  'birthdayMonth': null,
  'birthdayDay': null,
  'birthYear': null,
  'notes': null,
  'contactEveryDays': null,
  'lastContactedAt': null,
  'graphEntityId': null,
  'nextBirthday': null,
  'daysUntilBirthday': null,
  'turningAge': null,
  'daysSinceContact': null,
  'contactDueOn': null,
  'contactDue': false,
  'createdAt': '2026-09-01T12:00:00Z',
  'updatedAt': '2026-09-01T12:00:00Z',
};

Map<String, Object?> _signal(
  String headline, {
  int severity = 1,
  String kind = 'quiet',
  String detail = '',
}) => {
  'kind': kind,
  'severity': severity,
  'headline': headline,
  'detail': detail,
};

Map<String, Object?> _report(
  String id,
  String name, {
  List<Map<String, Object?>> signals = const [],
  List<int> weekly = const [4, 5, 6, 5, 4, 2, 1, 0],
  double? reply,
  double? baselineReply,
  int? tone,
  String? toneReason,
}) => {
  'personId': id,
  'name': name,
  'lastMessageAt': '2026-09-20T10:00:00Z',
  'recentSent': 2,
  'recentReceived': 3,
  'recentPerWeek': 1.2,
  'baselinePerWeek': 7.0,
  'myReplyMinutes': reply,
  'baselineReplyMinutes': baselineReply,
  'myInitiationShare': null,
  'baselineInitiationShare': null,
  'unansweredInbound': 0,
  'unansweredHours': null,
  'weeklyMessages': weekly,
  'signals': signals,
  'toneScore': tone,
  'toneReason': toneReason,
  'severity': signals.isEmpty
      ? 0
      : signals
            .map((s) => s['severity'] as int)
            .reduce((a, b) => a > b ? a : b),
};

Map<String, Object?> _overview(
  List<Map<String, Object?>> people, {
  bool tone = false,
}) => {'toneEnabled': tone, 'people': people};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(theme: buildJarvisTheme(), home: child),
    );
    await tester.pumpAndSettle();
  }

  void people(List<Map<String, Object?>> list) {
    http.on('GET', '/api/v1/people', list);
    http.on('GET', '/api/v1/people/suggestions', <Object?>[]);
  }

  test('labels read in plain words', () {
    expect(perWeekLabel(1), '1 message a week');
    expect(perWeekLabel(7), '7 messages a week');
    expect(perWeekLabel(0.4), '0.4 messages a week');
    expect(perWeekLabel(0), '0 messages a week');
    expect(replyTimeLabel(10), '10 min');
    expect(replyTimeLabel(0.2), '1 min');
    expect(replyTimeLabel(240), '4 h');
    expect(replyTimeLabel(3 * 24 * 60), '3 days');
    expect(toneLabel(2), 'Warm');
    expect(toneLabel(1), 'Friendly');
    expect(toneLabel(0), 'Neutral');
    expect(toneLabel(-1), 'A little cool');
    expect(toneLabel(-2), 'Tense');
    expect(opensPeople('people.radar'), isTrue);
  });

  test(
    'a report parses with its worst signal first and ignores broken rows',
    () {
      final report = RadarReportData.fromJson(
        _report(
          'p1',
          'Anna',
          signals: [
            _signal('Small note'),
            _signal('Big one', severity: 2),
            {'severity': 2},
          ],
          tone: -1,
          toneReason: 'Short replies.',
        ),
      )!;

      expect(report.signals.length, 2);
      expect(report.severity, 2);
      expect(report.topSignal!.headline, 'Big one');
      expect(report.toneScore, -1);
      expect(report.hasMessages, isTrue);
      expect(RadarReportData.fromJson({'name': 'no id'}), isNull);
      expect(RadarReportData.fromJson(null), isNull);
      expect(
        RadarReportData.fromJson(
          _report('p2', 'Piet', weekly: [0, 0]),
        )!.hasMessages,
        isFalse,
      );
      expect(RadarOverviewData.fromJson(_overview([]))!.people, isEmpty);
    },
  );

  testWidgets('the list shows who is drifting with the most worrying note', (
    tester,
  ) async {
    people([_person('p1', 'Anna'), _person('p2', 'Piet')]);
    http.on(
      'GET',
      '/api/v1/people/radar',
      _overview([
        _report(
          'p1',
          'Anna',
          signals: [_signal('No messages in 30 days', severity: 2)],
        ),
        _report('p2', 'Piet'),
      ]),
    );
    http.on('GET', '/api/v1/people/link-suggestions', <Object?>[]);
    await show(tester, PeopleScreen(http: http.client()));

    expect(find.text('Drifting'), findsOneWidget);
    expect(find.text('No messages in 30 days'), findsOneWidget);
    // Piet is fine, so he is not in the drifting list.
    expect(find.byKey(const Key('radar-person-p2')), findsNothing);
    expect(find.byKey(const Key('radar-person-p1')), findsOneWidget);
    expect(find.byKey(const Key('radar-steady')), findsNothing);
  });

  testWidgets('tapping a drifting person opens their page', (tester) async {
    final anna = _person('p1', 'Anna');
    people([anna]);
    http.on(
      'GET',
      '/api/v1/people/radar',
      _overview([
        _report(
          'p1',
          'Anna',
          signals: [_signal('The conversation has gone quiet')],
        ),
      ]),
    );
    http.on('GET', '/api/v1/people/link-suggestions', <Object?>[]);
    http.on('GET', '/api/v1/people/p1', {'person': anna, 'facts': <Object?>[]});
    await show(tester, PeopleScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('radar-person-p1')));
    await tester.pumpAndSettle();

    expect(find.byType(PersonDetailScreen), findsOneWidget);
  });

  testWidgets('when everyone is steady the radar says so', (tester) async {
    people([_person('p1', 'Anna')]);
    http.on('GET', '/api/v1/people/radar', _overview([_report('p1', 'Anna')]));
    http.on('GET', '/api/v1/people/link-suggestions', <Object?>[]);
    await show(tester, PeopleScreen(http: http.client()));

    expect(find.text('Relationship radar'), findsOneWidget);
    expect(find.byKey(const Key('radar-steady')), findsOneWidget);
    expect(find.text('Drifting'), findsNothing);
  });

  testWidgets(
    'no radar section without linked chats or when the radar is unavailable',
    (tester) async {
      people([_person('p1', 'Anna')]);
      http.on('GET', '/api/v1/people/radar', _overview([]));
      await show(tester, PeopleScreen(http: http.client()));
      expect(find.byKey(const Key('radar-section')), findsNothing);
      expect(find.text('Anna'), findsWidgets);

      final failing = FixtureHttp();
      failing.on('GET', '/api/v1/people', [_person('p1', 'Anna')]);
      failing.on('GET', '/api/v1/people/suggestions', <Object?>[]);
      failing.on('GET', '/api/v1/people/radar', null, status: 500);
      await tester.pumpWidget(const SizedBox());
      await show(tester, PeopleScreen(http: failing.client()));
      expect(find.byKey(const Key('radar-section')), findsNothing);
      expect(find.text('Anna'), findsWidgets);
    },
  );

  testWidgets(
    'the tone switch saves the choice and goes back when saving fails',
    (tester) async {
      people([_person('p1', 'Anna')]);
      http.on(
        'GET',
        '/api/v1/people/radar',
        _overview([_report('p1', 'Anna')]),
      );
      http.on('GET', '/api/v1/people/link-suggestions', <Object?>[]);
      http.on('PUT', '/api/v1/people/radar/settings', {'toneEnabled': true});
      await show(tester, PeopleScreen(http: http.client()));

      expect(
        tester
            .widget<SwitchListTile>(find.byKey(const Key('radar-tone-switch')))
            .value,
        isFalse,
      );
      await tester.tap(find.byKey(const Key('radar-tone-switch')));
      await tester.pumpAndSettle();

      expect(http.sent('PUT', '/api/v1/people/radar/settings').single.body, {
        'toneEnabled': true,
      });
      expect(
        tester
            .widget<SwitchListTile>(find.byKey(const Key('radar-tone-switch')))
            .value,
        isTrue,
      );

      http.on('PUT', '/api/v1/people/radar/settings', null, status: 500);
      await tester.tap(find.byKey(const Key('radar-tone-switch')));
      await tester.pumpAndSettle();

      expect(
        tester
            .widget<SwitchListTile>(find.byKey(const Key('radar-tone-switch')))
            .value,
        isTrue,
      );
      expect(find.text('Could not save that.'), findsOneWidget);
    },
  );

  testWidgets('a suggested chat is only linked after the owner confirms', (
    tester,
  ) async {
    people([_person('p1', 'Anna')]);
    http.on('GET', '/api/v1/people/radar', _overview([]));
    http.on('GET', '/api/v1/people/link-suggestions', [
      {
        'personId': 'p1',
        'personName': 'Anna',
        'connectionId': 'c1',
        'chatId': '+31611111111',
        'chatName': 'Anna K',
      },
    ]);
    http.on('POST', '/api/v1/people/p1/links', {'id': 'l1'}, status: 201);
    await show(tester, PeopleScreen(http: http.client()));

    expect(find.text('Link a WhatsApp chat?'), findsOneWidget);
    expect(find.text('Chat: Anna K'), findsOneWidget);
    expect(http.sent('POST', '/api/v1/people/p1/links'), isEmpty);

    http.on('GET', '/api/v1/people/link-suggestions', <Object?>[]);
    await tester.tap(
      find.byKey(const Key('link-suggestion-p1|c1|+31611111111')),
    );
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/people/p1/links').single.body, {
      'connectionId': 'c1',
      'chatId': '+31611111111',
    });
    expect(find.text('Linked Anna to Anna K.'), findsOneWidget);
    expect(find.text('Link a WhatsApp chat?'), findsNothing);
  });

  group('person page', () {
    Future<void> detail(
      WidgetTester tester, {
      Object? radar,
      int status = 200,
    }) async {
      final anna = _person('p1', 'Anna');
      http.on('GET', '/api/v1/people/p1', {
        'person': anna,
        'facts': <Object?>[],
      });
      if (radar != null || status != 200) {
        http.on('GET', '/api/v1/people/p1/radar', radar, status: status);
      }
      await show(
        tester,
        PersonDetailScreen(
          http: http.client(),
          person: PersonData.fromJson(anna)!,
        ),
      );
    }

    testWidgets('invites linking a chat when none is linked', (tester) async {
      await detail(tester, radar: {'links': <Object?>[], 'report': null});

      expect(find.text('Staying in touch'), findsOneWidget);
      expect(find.byKey(const Key('radar-link-empty')), findsOneWidget);
      expect(find.byKey(const Key('radar-sparkline')), findsNothing);
    });

    testWidgets('shows how you keep in touch, the notes and what is a guess', (
      tester,
    ) async {
      await detail(
        tester,
        radar: {
          'links': [
            {
              'id': 'l1',
              'chatId': '+3161',
              'displayName': 'Anna K',
              'readAlong': true,
            },
          ],
          'report': _report(
            'p1',
            'Anna',
            reply: 240,
            baselineReply: 10,
            tone: -1,
            toneReason: 'Short replies.',
            signals: [
              _signal(
                'You reply slower than you used to',
                detail: 'Usually 10 min, lately 4 h.',
              ),
              _signal(
                'They wrote 6 days ago and you have not replied',
                severity: 2,
                kind: 'unanswered',
              ),
            ],
          ),
        },
      );

      expect(find.text('Anna K'), findsOneWidget);
      expect(find.byKey(const Key('radar-sparkline')), findsOneWidget);
      expect(
        find.text('1.2 messages a week lately · 7 messages a week before'),
        findsOneWidget,
      );
      expect(
        find.text('You reply in about 4 h (before: 10 min)'),
        findsOneWidget,
      );
      expect(find.text('Tone: A little cool (a guess)'), findsOneWidget);
      expect(find.text('You reply slower than you used to'), findsOneWidget);
      expect(find.text('Usually 10 min, lately 4 h.'), findsOneWidget);
      expect(
        find.text('They wrote 6 days ago and you have not replied'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('radar-read-along-hint')), findsNothing);
    });

    testWidgets(
      'says when a linked chat is not read along, so there is nothing to look at',
      (tester) async {
        await detail(
          tester,
          radar: {
            'links': [
              {
                'id': 'l1',
                'chatId': '+3161',
                'displayName': 'Anna K',
                'readAlong': false,
              },
            ],
            'report': _report('p1', 'Anna', weekly: [0, 0, 0, 0, 0, 0, 0, 0]),
          },
        );

        expect(find.byKey(const Key('radar-read-along-hint')), findsOneWidget);
        expect(find.byKey(const Key('radar-sparkline')), findsNothing);
      },
    );

    testWidgets('has no radar card when the radar is unavailable', (
      tester,
    ) async {
      await detail(tester, status: 500);

      expect(find.byKey(const Key('person-radar')), findsNothing);
      expect(find.text('Notes'), findsOneWidget);
    });

    testWidgets('unlinking removes the chat and reloads', (tester) async {
      await detail(
        tester,
        radar: {
          'links': [
            {
              'id': 'l1',
              'chatId': '+3161',
              'displayName': 'Anna K',
              'readAlong': true,
            },
          ],
          'report': null,
        },
      );
      http.on('DELETE', '/api/v1/people/p1/links/l1', null, status: 204);
      http.on('GET', '/api/v1/people/p1/radar', {
        'links': <Object?>[],
        'report': null,
      });

      await tester.tap(find.byKey(const Key('radar-unlink-l1')));
      await tester.pumpAndSettle();

      expect(http.sent('DELETE', '/api/v1/people/p1/links/l1'), hasLength(1));
      expect(find.text('Unlinked Anna K.'), findsOneWidget);
      expect(find.byKey(const Key('radar-link-empty')), findsOneWidget);
    });

    testWidgets(
      'linking picks a chat from the candidates, with read-along ones first and searchable',
      (tester) async {
        await detail(tester, radar: {'links': <Object?>[], 'report': null});
        http.on('GET', '/api/v1/people/link-candidates', [
          {
            'connectionId': 'c1',
            'chatId': '+3161',
            'displayName': 'Anna K',
            'readAlong': true,
          },
          {
            'connectionId': 'c1',
            'chatId': '+3162',
            'displayName': 'Bram',
            'readAlong': false,
          },
        ]);
        http.on('POST', '/api/v1/people/p1/links', {'id': 'l1'}, status: 201);

        await tester.tap(find.byKey(const Key('radar-link-empty')));
        await tester.pumpAndSettle();
        expect(find.text('Anna K'), findsOneWidget);
        expect(find.text('Read-along on'), findsOneWidget);
        expect(
          find.text('Read-along off: no messages to look at'),
          findsOneWidget,
        );

        await tester.enterText(
          find.byKey(const Key('chat-picker-filter')),
          'bra',
        );
        await tester.pumpAndSettle();
        expect(find.text('Anna K'), findsNothing);
        expect(find.text('Bram'), findsOneWidget);

        await tester.enterText(
          find.byKey(const Key('chat-picker-filter')),
          'zzz',
        );
        await tester.pumpAndSettle();
        expect(find.byKey(const Key('chat-picker-empty')), findsOneWidget);

        await tester.enterText(
          find.byKey(const Key('chat-picker-filter')),
          'anna',
        );
        await tester.pumpAndSettle();
        http.on('GET', '/api/v1/people/p1/radar', {
          'links': [
            {
              'id': 'l1',
              'chatId': '+3161',
              'displayName': 'Anna K',
              'readAlong': true,
            },
          ],
          'report': null,
        });
        await tester.tap(find.byKey(const Key('chat-pick-c1-+3161')));
        await tester.pumpAndSettle();

        expect(http.sent('POST', '/api/v1/people/p1/links').single.body, {
          'connectionId': 'c1',
          'chatId': '+3161',
        });
        expect(find.text('Linked Anna to Anna K.'), findsOneWidget);
        expect(find.byKey(const Key('radar-link-l1')), findsOneWidget);
      },
    );

    testWidgets(
      'a chat that is already linked to someone else shows the reason',
      (tester) async {
        await detail(tester, radar: {'links': <Object?>[], 'report': null});
        http.on('GET', '/api/v1/people/link-candidates', [
          {
            'connectionId': 'c1',
            'chatId': '+3161',
            'displayName': 'Anna K',
            'readAlong': true,
          },
        ]);
        http.on('POST', '/api/v1/people/p1/links', {
          'title': 'Conflict',
          'detail': 'That chat is already linked to someone else.',
        }, status: 409);

        await tester.tap(find.byKey(const Key('radar-link-empty')));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('chat-pick-c1-+3161')));
        await tester.pumpAndSettle();

        expect(
          find.textContaining('already linked to someone else'),
          findsOneWidget,
        );
      },
    );

    testWidgets('with no chats the picker explains what to do first', (
      tester,
    ) async {
      await detail(tester, radar: {'links': <Object?>[], 'report': null});
      http.on('GET', '/api/v1/people/link-candidates', <Object?>[]);

      await tester.tap(find.byKey(const Key('radar-link-empty')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('chat-picker-empty')), findsOneWidget);
      expect(find.textContaining('read-along'), findsWidgets);
    });
  });
}
