import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/people/people_models.dart';
import 'package:jarvis_mobile/features/people/people_screen.dart';
import 'package:jarvis_mobile/features/people/person_detail_screen.dart';
import 'package:jarvis_mobile/features/notifications/notification_routing.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _person(
  String id,
  String name, {
  String? relationship,
  int? month,
  int? day,
  int? year,
  int? every,
  int? daysUntil,
  int? turning,
  int? since,
  bool due = false,
  String? notes,
}) => {
  'id': id,
  'name': name,
  'relationship': relationship,
  'birthdayMonth': month,
  'birthdayDay': day,
  'birthYear': year,
  'notes': notes,
  'contactEveryDays': every,
  'lastContactedAt': since == null ? null : '2026-09-20T12:00:00Z',
  'graphEntityId': null,
  'nextBirthday': null,
  'daysUntilBirthday': daysUntil,
  'turningAge': turning,
  'daysSinceContact': since,
  'contactDueOn': null,
  'contactDue': due,
  'createdAt': '2026-09-01T12:00:00Z',
  'updatedAt': '2026-09-01T12:00:00Z',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  test('people parse from the API and describe dates naturally', () {
    final person = PersonData.fromJson(
      _person('p1', 'Anna de Vries', month: 3, day: 14, daysUntil: 1),
    )!;
    expect(person.hasBirthday, isTrue);
    expect(birthdayCountdown(person), 'Tomorrow');
    expect(personInitials('Anna de Vries'), 'AV');
    expect(personInitials('mama'), 'M');
    expect(cadenceLabel(14), 'every 2 weeks');
    expect(cadenceLabel(30), 'every month');
    expect(daysAgoLabel(21), '3 weeks ago');
    expect(lastTalkedLabel(person), 'No contact logged');
    expect(PersonData.fromJson({'name': 'no id'}), isNull);
    expect(opensPeople('people.birthday'), isTrue);
    expect(opensPeople('people.checkin'), isTrue);
    expect(opensPeople('reminder.due'), isFalse);
  });

  testWidgets('overview shows upcoming birthdays, who to reach out to, and '
      'everyone', (tester) async {
    http.on('GET', '/api/v1/people', [
      _person(
        'p1',
        'Anna',
        relationship: 'Sister',
        month: 10,
        day: 2,
        daysUntil: 0,
        turning: 30,
      ),
      _person(
        'p2',
        'Mama',
        relationship: 'Mother',
        every: 14,
        since: 20,
        due: true,
      ),
      _person('p3', 'Tom'),
    ]);
    http.on('GET', '/api/v1/people/suggestions', [
      {
        'graphEntityId': 'g1',
        'name': 'Opa Kees',
        'relationship': 'Grandfather',
        'birthdayMonth': 5,
        'birthdayDay': 1,
      },
    ]);
    await show(tester, PeopleScreen(http: http.client()));

    expect(find.text('Coming up'), findsOneWidget);
    expect(find.text('Today'), findsOneWidget);
    expect(find.text('Turns 30'), findsOneWidget);
    expect(find.text('Time to reach out'), findsOneWidget);
    expect(
      find.text('Talked 2 weeks ago · you wanted every 2 weeks'),
      findsOneWidget,
    );
    expect(find.text('Sister · 2 October'), findsOneWidget);
    expect(find.text('Tap to add details'), findsOneWidget);
    expect(find.text('From your memory'), findsOneWidget);
    expect(find.text('Grandfather · Birthday 1 May'), findsOneWidget);
  });

  testWidgets('talked marks contact and adding a suggestion links memory', (
    tester,
  ) async {
    http.on('GET', '/api/v1/people', [
      _person('p2', 'Mama', every: 14, since: 20, due: true),
    ]);
    http.on('GET', '/api/v1/people/suggestions', [
      {
        'graphEntityId': 'g1',
        'name': 'Opa Kees',
        'birthdayMonth': 5,
        'birthdayDay': 1,
      },
    ]);
    http.on(
      'POST',
      '/api/v1/people/p2/contact',
      _person('p2', 'Mama', every: 14, since: 0),
    );
    http.on(
      'POST',
      '/api/v1/people',
      _person('p9', 'Opa Kees', month: 5, day: 1),
    );
    await show(tester, PeopleScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('people-talked-p2')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/people/p2/contact'), hasLength(1));
    expect(find.text('Time to reach out'), findsNothing);
    expect(find.text('Talked today'), findsOneWidget);

    await tester.tap(find.byKey(const Key('people-suggest-g1')));
    await tester.pumpAndSettle();
    final body = http.sent('POST', '/api/v1/people').single.body! as Map;
    expect(body['name'], 'Opa Kees');
    expect(body['graphEntityId'], 'g1');
    expect(body['birthdayMonth'], 5);
  });

  testWidgets('empty state invites adding someone with a birthday and a '
      'check-in rhythm', (tester) async {
    http.on('GET', '/api/v1/people', <Object>[]);
    http.on('GET', '/api/v1/people/suggestions', <Object>[]);
    http.on(
      'POST',
      '/api/v1/people',
      _person('p1', 'Anna', month: 3, day: 14, every: 30),
    );
    http.on('GET', '/api/v1/people/p1', {
      'person': _person(
        'p1',
        'Anna',
        month: 3,
        day: 14,
        every: 30,
        daysUntil: 164,
      ),
      'facts': <Object>[],
    });
    await show(tester, PeopleScreen(http: http.client()));

    expect(find.text('Keep the people who matter close'), findsOneWidget);
    await tester.tap(find.byKey(const Key('people-add-first')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('person-name')), 'Anna');
    await tester.tap(find.byKey(const ValueKey('person-birthday-day-31')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('14').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('person-birthday-month')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('March').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('person-every-30')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('person-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/people').single.body! as Map;
    expect(body['name'], 'Anna');
    expect(body['birthdayDay'], 14);
    expect(body['birthdayMonth'], 3);
    expect(body['birthYear'], isNull);
    expect(body['contactEveryDays'], 30);
    expect(find.byType(PersonDetailScreen), findsOneWidget);
  });

  testWidgets('person page shows birthday, rhythm, notes, and memory', (
    tester,
  ) async {
    final person = _person(
      'p1',
      'Mama',
      relationship: 'Mother',
      month: 3,
      day: 14,
      year: 1961,
      daysUntil: 5,
      turning: 65,
      every: 14,
      since: 20,
      due: true,
      notes: 'Likes tulips',
    );
    http.on('GET', '/api/v1/people/p1', {
      'person': person,
      'facts': [
        {'predicate': 'lives_in', 'value': 'Utrecht'},
      ],
    });
    http.on(
      'POST',
      '/api/v1/people/p1/contact',
      _person('p1', 'Mama', every: 14, since: 0),
    );
    await show(
      tester,
      PersonDetailScreen(
        http: http.client(),
        person: PersonData.fromJson(person)!,
      ),
    );

    expect(find.text('14 March 1961'), findsOneWidget);
    expect(find.text('In 5 days · turns 65'), findsOneWidget);
    expect(find.text('Every 2 weeks'), findsOneWidget);
    expect(find.text('Talked 2 weeks ago · due now'), findsOneWidget);
    expect(find.text('Likes tulips'), findsOneWidget);
    expect(find.text('What Jarvis remembers'), findsOneWidget);
    expect(find.text('Lives in'), findsOneWidget);
    expect(find.text('Utrecht'), findsOneWidget);

    await tester.tap(find.byKey(const Key('person-talked-today')));
    await tester.pumpAndSettle();
    expect(find.text('Talked today'), findsOneWidget);
  });
}
