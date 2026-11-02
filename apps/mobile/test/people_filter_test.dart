import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/people/people_filter.dart';
import 'package:jarvis_mobile/features/people/people_models.dart';
import 'package:jarvis_mobile/features/people/people_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _person(String id, String name, {String? relationship}) =>
    {
      'id': id,
      'name': name,
      'relationship': relationship,
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

final _raw = [
  _person('1', 'Anna de Vries', relationship: 'Friend'),
  _person('2', 'Mum', relationship: 'Mother'),
  _person('3', 'Tom Jansen', relationship: 'friend'),
  _person('4', 'José Pérez'),
];

List<PersonData> get people => [for (final r in _raw) PersonData.fromJson(r)!];
List<String> names(List<PersonData> list) => [for (final p in list) p.name];

void main() {
  group('filterPeople', () {
    test('matches name or relationship, ignoring case and accents', () {
      expect(names(filterPeople(people, query: 'anna')), ['Anna de Vries']);
      expect(names(filterPeople(people, query: 'jose')), ['José Pérez']);
      expect(names(filterPeople(people, query: 'mother')), ['Mum']);
      expect(names(filterPeople(people, query: 'FRIEND')), [
        'Anna de Vries',
        'Tom Jansen',
      ]);
    });

    test('relationship filter keeps the original order', () {
      expect(names(filterPeople(people, relationship: 'Friend')), [
        'Anna de Vries',
        'Tom Jansen',
      ]);
      expect(
        names(filterPeople(people, query: 'jansen', relationship: 'friend')),
        ['Tom Jansen'],
      );
      expect(
        filterPeople(people, query: 'jansen', relationship: 'Mother'),
        isEmpty,
      );
    });

    test('no filters returns everyone in the given order', () {
      expect(names(filterPeople(people)), names(people));
    });

    test('relationship choices merge spellings and sort by popularity', () {
      final choices = relationshipChoices(people);
      expect(choices.first, (label: 'Friend', count: 2));
      expect(choices.last, (label: 'Mother', count: 1));
      expect(choices, hasLength(2));
    });
  });

  group('People screen', () {
    late FixtureHttp http;

    setUp(() {
      http = FixtureHttp();
      http.on('GET', '/api/v1/people', _raw);
      http.on('GET', '/api/v1/people/suggestions', <Object>[]);
    });

    Future<void> show(WidgetTester tester) async {
      tester.view.physicalSize = const Size(800, 2000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(home: PeopleScreen(http: http.client())),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('search and relationship chips narrow the list and can be '
        'cleared', (tester) async {
      await show(tester);
      expect(find.text('Mum'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('people-search')), 'jansen');
      await tester.pumpAndSettle();
      expect(find.text('Tom Jansen'), findsOneWidget);
      expect(find.text('Mum'), findsNothing);
      expect(find.text('1 of 4'), findsOneWidget);

      await tester.tap(find.byKey(const Key('people-clear-filters')));
      await tester.pumpAndSettle();
      expect(find.text('Mum'), findsOneWidget);

      await tester.tap(find.byKey(const Key('people-relationship-Friend')));
      await tester.pumpAndSettle();
      expect(find.text('Anna de Vries'), findsOneWidget);
      expect(find.text('Mum'), findsNothing);
    });

    testWidgets('no matches says so and keeps the filters to clear', (
      tester,
    ) async {
      await show(tester);
      await tester.enterText(find.byKey(const Key('people-search')), 'zzz');
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('people-no-match')), findsOneWidget);
      expect(find.byKey(const Key('people-clear-filters')), findsOneWidget);
    });

    testWidgets('a single person needs no filters', (tester) async {
      http.on('GET', '/api/v1/people', [_raw.first]);
      await show(tester);
      expect(find.byKey(const Key('people-search')), findsNothing);
    });
  });
}
