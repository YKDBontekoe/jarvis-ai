import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/meetings/meeting_screen.dart';
import 'package:jarvis_mobile/features/meetings/meetings_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _id = 'm1';

Map<String, Object?> _meeting({
  List<Map<String, Object?>> candidates = const [],
  Map<String, Object?>? prep,
  List<Map<String, Object?>> agenda = const [],
  String? notes,
}) => {
  'id': _id,
  'title': 'Kitchen sync',
  'startsAt': '2026-10-20T14:00:00Z',
  'projectId': null,
  'document': {
    'purpose': 'Align on tiles',
    'template': 'project_update',
    'participants': [
      {'name': 'Rosa', 'personId': null},
    ],
    'agenda': agenda,
    'privateNotes': notes,
    'prep': prep,
    'debriefText': null,
    'candidates': candidates,
  },
};

Map<String, Object?> _prep() => {
  'builtAt': '2026-10-17T09:00:00Z',
  'items': [
    {
      'section': 'Open loops',
      'kind': 'fact',
      'text': 'You owe Rosa: Send the tile quotes — 3 days overdue',
      'ref': 'commitment:11111111-1111-7111-8111-111111111111',
    },
    {
      'section': 'Questions',
      'kind': 'question',
      'text': 'What do you want to leave the meeting with?',
      'ref': null,
    },
  ],
};

Map<String, Object?> _candidate(
  String id,
  String kind,
  String text, {
  String? who,
  String? dueOn,
  String? dueText,
  String state = 'pending',
  String? resultRef,
}) => {
  'id': id,
  'kind': kind,
  'text': text,
  'counterparty': who,
  'dueOn': dueOn,
  'dueText': dueText,
  'evidence': text,
  'state': state,
  'resultRef': resultRef,
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 4200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(_host(child));
    await tester.pumpAndSettle();
  }

  Future<void> detail(WidgetTester tester) =>
      show(tester, MeetingScreen(http: http.client(), meetingId: _id));

  testWidgets(
    'the list offers calendar events to prepare and creates a meeting with a prep pack',
    (tester) async {
      http
        ..on('GET', '/api/v1/meetings', <Object>[])
        ..on('GET', '/api/v1/meetings/events', [
          {
            'title': 'Design review',
            'startAt': '2026-10-20T14:00:00Z',
            'endAt': null,
          },
        ])
        ..on('GET', '/api/v1/projects', [
          {'id': 'p1', 'name': 'Kitchen'},
        ])
        ..on('POST', '/api/v1/meetings', _meeting(), status: 201)
        ..on('POST', '/api/v1/meetings/$_id/prep', _meeting(prep: _prep()))
        ..on('GET', '/api/v1/meetings/$_id', _meeting(prep: _prep()));
      await show(tester, MeetingsScreen(http: http.client()));
      expect(find.text('Coming up on your calendar'), findsOneWidget);
      expect(find.text('Design review'), findsOneWidget);

      await tester.tap(find.text('Prepare').first);
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('meeting-title-field')), findsOneWidget);
      expect(
        (tester
            .widget<TextField>(find.byKey(const Key('meeting-title-field')))
            .controller!
            .text),
        'Design review',
      );
      await tester.enterText(
        find.byKey(const Key('meeting-people-field')),
        'Rosa, Tom, ',
      );
      await tester.tap(
        find.byKey(const Key('meeting-template-project_update')),
      );
      await tester.tap(find.byKey(const Key('meeting-create')));
      await tester.pumpAndSettle();

      final sent = http.sent('POST', '/api/v1/meetings').single.body as Map;
      expect(sent['title'], 'Design review');
      expect(sent['startsAt'], '2026-10-20T14:00:00Z');
      expect(sent['template'], 'project_update');
      expect(sent['participants'], [
        {'name': 'Rosa'},
        {'name': 'Tom'},
      ]);
      expect(http.sent('POST', '/api/v1/meetings/$_id/prep'), hasLength(1));
      expect(find.text('Prep pack'), findsOneWidget);
    },
  );

  testWidgets(
    'the prep pack shows facts with a way to open the source and questions apart',
    (tester) async {
      http.on('GET', '/api/v1/meetings/$_id', _meeting(prep: _prep()));
      await detail(tester);

      expect(find.text('Open loops'), findsOneWidget);
      expect(
        find.text('You owe Rosa: Send the tile quotes — 3 days overdue'),
        findsOneWidget,
      );
      expect(
        find.text('What do you want to leave the meeting with?'),
        findsOneWidget,
      );
      expect(find.text('Refresh'), findsOneWidget);
      expect(find.textContaining('Rosa'), findsWidgets);
    },
  );

  testWidgets('without a prep pack it explains and builds one', (tester) async {
    http
      ..on('GET', '/api/v1/meetings/$_id', _meeting())
      ..on('POST', '/api/v1/meetings/$_id/prep', _meeting(prep: _prep()));
    await detail(tester);
    expect(find.text('Build it'), findsOneWidget);

    await tester.tap(find.byKey(const Key('meeting-prep')));
    await tester.pumpAndSettle();
    expect(find.text('Open loops'), findsOneWidget);
  });

  testWidgets(
    'the agenda is edited, a point can be kept private, and it is saved as written',
    (tester) async {
      http
        ..on(
          'GET',
          '/api/v1/meetings/$_id',
          _meeting(
            agenda: [
              {'id': 'a1', 'text': 'Tile choice', 'shared': true},
            ],
          ),
        )
        ..on('PUT', '/api/v1/meetings/$_id', _meeting());
      await detail(tester);

      await tester.tap(find.byKey(const Key('meeting-agenda-edit')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('meeting-agenda-add')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('meeting-agenda-line-1')),
        'Is the budget realistic?',
      );
      await tester.tap(find.byKey(const Key('meeting-agenda-share-1')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('meeting-agenda-save')));
      await tester.pumpAndSettle();

      final sent = http.sent('PUT', '/api/v1/meetings/$_id').single.body as Map;
      expect(sent['agenda'], [
        {'id': 'a1', 'text': 'Tile choice', 'shared': true},
        {'id': 'a2', 'text': 'Is the budget realistic?', 'shared': false},
      ]);
    },
  );

  testWidgets('private notes are saved on their own', (tester) async {
    http
      ..on('GET', '/api/v1/meetings/$_id', _meeting())
      ..on(
        'PUT',
        '/api/v1/meetings/$_id',
        _meeting(notes: 'Rosa seemed tense'),
      );
    await detail(tester);

    await tester.enterText(
      find.byKey(const Key('meeting-notes')),
      'Rosa seemed tense',
    );
    await tester.ensureVisible(find.byKey(const Key('meeting-notes-save')));
    await tester.tap(find.byKey(const Key('meeting-notes-save')));
    await tester.pumpAndSettle();

    final sent = http.sent('PUT', '/api/v1/meetings/$_id').single.body as Map;
    expect(sent, {'privateNotes': 'Rosa seemed tense'});
    expect(find.textContaining('never appear in a follow-up'), findsOneWidget);
  });

  testWidgets(
    'a debrief lists proposals, unclear dates stay unclear, and nothing is saved until accepted',
    (tester) async {
      final found = [
        _candidate(
          'i_owe-1',
          'i_owe',
          "I'll send Rosa the quote by Friday.",
          who: 'Rosa',
          dueOn: '2026-10-23',
          dueText: 'friday',
        ),
        _candidate(
          'they_owe-1',
          'they_owe',
          'Rosa will confirm the plumber next week.',
          who: 'Rosa',
          dueText: 'next week',
        ),
        _candidate('decision-1', 'decision', 'We decided to go with Bosch.'),
        _candidate('question-1', 'question', 'Who pays for the fitting?'),
      ];
      http
        ..on('GET', '/api/v1/meetings/$_id', _meeting())
        ..on(
          'POST',
          '/api/v1/meetings/$_id/debrief',
          _meeting(candidates: found),
        );
      await detail(tester);

      await tester.enterText(find.byKey(const Key('meeting-debrief')), 'notes');
      await tester.ensureVisible(find.byKey(const Key('meeting-debrief-read')));
      await tester.tap(find.byKey(const Key('meeting-debrief-read')));
      await tester.pumpAndSettle();

      expect(find.text('Review'), findsOneWidget);
      expect(find.textContaining('Due 23 Oct 2026'), findsOneWidget);
      expect(find.textContaining('Date unclear (“next week”)'), findsOneWidget);
      expect(find.text('Open question'), findsOneWidget);
      expect(
        find.byKey(const Key('candidate-accept-question-1')),
        findsNothing,
      );
      expect(
        find.byKey(const Key('candidate-dismiss-question-1')),
        findsOneWidget,
      );
      expect(
        http.sent('POST', '/api/v1/meetings/$_id/candidates/i_owe-1/accept'),
        isEmpty,
      );
    },
  );

  testWidgets(
    'accepting a promise sends the owner-edited values, then it shows as accepted with a link',
    (tester) async {
      final pending = _candidate(
        'i_owe-1',
        'i_owe',
        "I'll send Rosa the quote next week.",
        who: 'Rosa',
        dueText: 'next week',
      );
      final accepted = _candidate(
        'i_owe-1',
        'i_owe',
        "I'll send Rosa the quote next week.",
        who: 'Rosa',
        dueText: 'next week',
        state: 'accepted',
        resultRef: 'commitment:22222222-2222-7222-8222-222222222222',
      );
      http
        ..on('GET', '/api/v1/meetings/$_id', _meeting(candidates: [pending]))
        ..on(
          'POST',
          '/api/v1/meetings/$_id/candidates/i_owe-1/accept',
          _meeting(candidates: [accepted]),
        );
      await detail(tester);

      await tester.ensureVisible(
        find.byKey(const Key('candidate-accept-i_owe-1')),
      );
      await tester.tap(find.byKey(const Key('candidate-accept-i_owe-1')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('accept-text')),
        'Send Rosa the revised quote',
      );
      await tester.pump();
      await tester.tap(find.byKey(const Key('accept-save')));
      await tester.pumpAndSettle();

      final sent =
          http
                  .sent(
                    'POST',
                    '/api/v1/meetings/$_id/candidates/i_owe-1/accept',
                  )
                  .single
                  .body
              as Map;
      expect(sent['description'], 'Send Rosa the revised quote');
      expect(sent['counterparty'], 'Rosa');
      expect(sent['dueOn'], isNull);
      expect(sent['clearDue'], isTrue);
      expect(find.text('Accepted'), findsOneWidget);
      expect(find.text('Reviewed'), findsOneWidget);
      expect(find.text('Open'), findsOneWidget);
    },
  );

  testWidgets('logging a decision asks for how sure you are and a review day', (
    tester,
  ) async {
    final pending = _candidate(
      'decision-1',
      'decision',
      'We decided to go with Bosch.',
    );
    http
      ..on('GET', '/api/v1/meetings/$_id', _meeting(candidates: [pending]))
      ..on(
        'POST',
        '/api/v1/meetings/$_id/candidates/decision-1/accept',
        _meeting(candidates: [pending]),
      );
    await detail(tester);

    await tester.ensureVisible(
      find.byKey(const Key('candidate-accept-decision-1')),
    );
    await tester.tap(find.byKey(const Key('candidate-accept-decision-1')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('accept-percent')), findsOneWidget);
    await tester.tap(find.byKey(const Key('accept-save')));
    await tester.pumpAndSettle();

    final sent =
        http
                .sent(
                  'POST',
                  '/api/v1/meetings/$_id/candidates/decision-1/accept',
                )
                .single
                .body
            as Map;
    expect(sent['probability'], .7);
    expect(sent['reviewOn'], matches(RegExp(r'^\d{4}-\d{2}-\d{2}$')));
    expect(sent.containsKey('counterparty'), isFalse);
  });

  testWidgets('dismissing a proposal sends only a dismissal', (tester) async {
    final pending = _candidate('i_owe-1', 'i_owe', "I'll send Rosa the quote.");
    http
      ..on('GET', '/api/v1/meetings/$_id', _meeting(candidates: [pending]))
      ..on(
        'POST',
        '/api/v1/meetings/$_id/candidates/i_owe-1/dismiss',
        _meeting(
          candidates: [
            _candidate(
              'i_owe-1',
              'i_owe',
              "I'll send Rosa the quote.",
              state: 'dismissed',
            ),
          ],
        ),
      );
    await detail(tester);

    await tester.ensureVisible(
      find.byKey(const Key('candidate-dismiss-i_owe-1')),
    );
    await tester.tap(find.byKey(const Key('candidate-dismiss-i_owe-1')));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/meetings/$_id/candidates/i_owe-1/dismiss'),
      hasLength(1),
    );
    expect(
      http.sent('POST', '/api/v1/meetings/$_id/candidates/i_owe-1/accept'),
      isEmpty,
    );
    expect(find.text('Dismissed'), findsOneWidget);
  });

  testWidgets('the follow-up is shown as an unsent draft that can be copied', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/meetings/$_id', _meeting())
      ..on('POST', '/api/v1/meetings/$_id/followup', {
        'subject': 'Follow-up: Kitchen sync',
        'body': 'Hi Rosa,\n\nI will:\n- Send the quote\n',
        'sent': false,
        'included': ['Send the quote'],
      });
    String? copied;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, (call) async {
          if (call.method == 'Clipboard.setData') {
            copied = (call.arguments as Map)['text'] as String;
          }
          return null;
        });
    addTearDown(
      () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(SystemChannels.platform, null),
    );
    await detail(tester);

    await tester.ensureVisible(find.byKey(const Key('meeting-followup')));
    await tester.tap(find.byKey(const Key('meeting-followup')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('followup-unsent')), findsOneWidget);
    expect(find.textContaining('Nothing has been sent'), findsOneWidget);
    await tester.tap(find.byKey(const Key('followup-copy')));
    await tester.pumpAndSettle();
    expect(copied, contains('Send the quote'));
  });

  testWidgets('a deleted meeting says so', (tester) async {
    http.on('GET', '/api/v1/meetings/$_id', null, status: 404);
    await detail(tester);
    expect(find.text('This meeting is gone'), findsOneWidget);
  });
}
