import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/inbox/inbox_models.dart';
import 'package:jarvis_mobile/features/inbox/inbox_screen.dart';

import 'support/fixture_http.dart';

final _now = DateTime(2026, 10, 3, 12);

Map<String, Object?> _thread(String id, String title, String state,
        {int priority = 1, String? summary, String? reply}) =>
    {
      'id': id,
      'source': 'whatsapp',
      'title': title,
      'counterparty': title,
      'state': state,
      'priority': priority,
      'summary': summary,
      'suggestedReply': reply,
      'lastMessagePreview': 'Kun je morgen?',
      'lastFromMe': false,
      'lastMessageAt': '2026-10-03T09:00:00Z',
    };

Map<String, Object?> _inbox() => {
  'threads': [
    _thread('t1', 'Sanne', 'needs_reply', priority: 3),
    _thread('t2', 'Piet', 'waiting'),
  ],
  'counts': {'needs_reply': 1, 'waiting': 1, 'fyi': 0, 'snoozed': 0, 'done': 0},
  'commitments': {'open': 1},
};

List<Object?> _commitments() => [
  {
    'id': 'c1',
    'direction': 'i_owe',
    'counterparty': 'Sanne',
    'description': 'Boek terugbrengen',
    'dueOn': '2026-10-01',
    'status': 'open',
    'suggested': false,
  },
  {
    'id': 'c2',
    'direction': 'owed_to_me',
    'counterparty': 'Piet',
    'description': 'Offerte sturen',
    'status': 'open',
    'suggested': true,
  },
];

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/inbox', _inbox());
    http.on('GET', '/api/v1/commitments', _commitments());
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: InboxScreen(http: http.client(), now: _now)),
    );
    await tester.pumpAndSettle();
  }

  test('models parse and describe commitments', () {
    final data = InboxData.fromJson(_inbox())!;
    expect(data.threads, hasLength(2));
    expect(data.counts['needs_reply'], 1);
    expect(inboxStateLabel('needs_reply'), 'Needs reply');
    expect(priorityLabel(3), 'Urgent');
    final owed = CommitmentData.fromJson(_commitments()[1])!;
    expect(owed.headline, 'Piet owes you');
    expect(CommitmentData.fromJson({'id': 'x'}), isNull);
    final overdue = CommitmentData.fromJson(_commitments()[0])!;
    expect(overdue.isOverdue(_now), isTrue);
  });

  testWidgets('lists threads by state and marks one done', (tester) async {
    http.on('PUT', '/api/v1/inbox/t1/state', _thread('t1', 'Sanne', 'done'));
    await show(tester);

    expect(http.requests.first.query['sync'], true);
    expect(find.text('Sanne'), findsOneWidget);
    expect(find.text('Urgent'), findsOneWidget);
    expect(find.text('Piet'), findsNothing);

    await tester.tap(find.byKey(const Key('inbox-filter-waiting')));
    await tester.pumpAndSettle();
    expect(find.text('Piet'), findsOneWidget);

    await tester.tap(find.byKey(const Key('inbox-filter-needs_reply')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('inbox-done-t1')));
    await tester.pumpAndSettle();
    final put = http.sent('PUT', '/api/v1/inbox/t1/state').single;
    expect((put.body as Map)['state'], 'done');
  });

  testWidgets('promises show overdue items and let you keep a suggestion', (
    tester,
  ) async {
    http.on('PATCH', '/api/v1/commitments/c2', _commitments()[1]);
    await show(tester);
    await tester.tap(find.byKey(const Key('inbox-tab-commitments')));
    await tester.pumpAndSettle();

    expect(find.textContaining('Overdue'), findsOneWidget);
    expect(find.text('Jarvis found these in your chats'), findsOneWidget);
    await tester.tap(find.byKey(const Key('commitment-keep-c2')));
    await tester.pumpAndSettle();
    final patch = http.sent('PATCH', '/api/v1/commitments/c2').single;
    expect((patch.body as Map)['status'], 'accepted');
  });

  testWidgets('adding a promise posts it', (tester) async {
    http.on('POST', '/api/v1/commitments', _commitments()[0], status: 201);
    await show(tester);
    await tester.tap(find.byKey(const Key('inbox-tab-commitments')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('commitment-add')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('commitment-who')), 'Anna');
    await tester.enterText(find.byKey(const Key('commitment-what')), 'Bellen');
    await tester.tap(find.byKey(const Key('commitment-save')));
    await tester.pumpAndSettle();

    final post = http.sent('POST', '/api/v1/commitments').single.body as Map;
    expect(post['counterparty'], 'Anna');
    expect(post['direction'], 'i_owe');
  });
}
