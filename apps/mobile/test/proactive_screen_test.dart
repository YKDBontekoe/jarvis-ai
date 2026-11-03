import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/proactivity/proactive_screen.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/schedule_format.dart';

import 'support/fixture_http.dart';

const proactiveFixtureId = '019a4320-0000-7000-8000-000000000001';

Map<String, dynamic> proactiveFixture() => {
  'id': proactiveFixtureId,
  'kind': 'project',
  'title': 'Move the renovation forward',
  'objective': 'Keep the quotes, measurements and decisions moving together.',
  'status': 'open',
  'enabled': true,
  'revision': 3,
  'nextAction': 'Choose a quote. The measurements are ready.',
  'stopCondition': 'The renovation is finished and I confirm the result.',
  'sourceRefs': <String>[],
  'context': <Object>[],
  'steps': [
    {
      'id': 'measure',
      'title': 'Confirm the measurements',
      'done': true,
      'dependsOn': <String>[],
    },
    {
      'id': 'choose',
      'title': 'Choose a quote',
      'done': false,
      'dependsOn': ['measure'],
      'minutes': 30,
    },
  ],
  'findings': [
    {
      'key': 'next',
      'title': 'Ready for your decision',
      'reason': 'The measurements are complete. The quote decision is the next eligible step.',
    },
  ],
  'preparedReport': 'Current evidence: measurements are complete. No messages have been sent and no supplier has been chosen.',
  'receipts': [
    {
      'id': 'receipt-1',
      'kind': 'prepared',
      'reason': 'Checked permitted sources and refreshed the next step.',
      'at': '2026-11-02T12:00:00Z',
      'undone': false,
    },
  ],
  'feedback': <Object>[],
};

void main() {
  late FixtureHttp fixture;
  final originalZoneLookup = deviceTimeZoneLookup;
  tearDown(() => deviceTimeZoneLookup = originalZoneLookup);
  setUp(() {
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
    fixture = FixtureHttp();
    fixture.on('GET', '/api/v1/proactivity/cases', [proactiveFixture()]);
    fixture.on(
      'GET',
      '/api/v1/proactivity/cases/$proactiveFixtureId',
      proactiveFixture(),
    );
    fixture.on('GET', '/api/v1/proactivity/cases/sources', <Object>[]);
  });
  Widget app({String? id}) => MaterialApp(
    theme: buildJarvisTheme(brightness: Brightness.light),
    home: ProactiveScreen(http: fixture.client(), caseId: id),
  );
  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 15; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  testWidgets(
    'opens tracked work and keeps next action and history accessible',
    (tester) async {
      await tester.pumpWidget(app());
      await settle(tester);
      expect(find.text('Move the renovation forward'), findsOneWidget);
      await tester.tap(find.text('Open'));
      await settle(tester);
      expect(
        find.text('Choose a quote. The measurements are ready.'),
        findsOneWidget,
      );
      expect(find.text('Pause'), findsOneWidget);
    },
  );

  testWidgets(
    'pause sends the current revision and changes its visible state',
    (tester) async {
      fixture.on(
        'POST',
        '/api/v1/proactivity/cases/$proactiveFixtureId/commands',
        {
          ...proactiveFixture(),
          'status': 'paused',
          'enabled': false,
          'revision': 4,
        },
      );
      await tester.pumpWidget(app(id: proactiveFixtureId));
      await settle(tester);
      await tester.tap(find.text('Pause'));
      await settle(tester);
      expect(find.text('Enable checks'), findsOneWidget);
      final request = fixture
          .sent(
            'POST',
            '/api/v1/proactivity/cases/$proactiveFixtureId/commands',
          )
          .single;
      expect(request.body, {'command': 'pause', 'revision': 3});
    },
  );

  testWidgets('failed creation preserves the editor contents', (tester) async {
    fixture.on('POST', '/api/v1/proactivity/cases', {
      'message': 'Cannot save right now',
    }, status: 503);
    await tester.pumpWidget(app());
    await settle(tester);
    await tester.tap(find.text('New responsibility'));
    await settle(tester);
    await tester.enterText(find.byType(TextFormField).at(0), 'Compare offers');
    await tester.enterText(
      find.byType(TextFormField).at(1),
      'Keep this comparison moving',
    );
    await tester.tap(find.text('Save'));
    await settle(tester);
    expect(find.text('Compare offers'), findsOneWidget);
    expect(find.text('Keep this comparison moving'), findsOneWidget);
    expect(find.text('Save'), findsOneWidget);
  });

  testWidgets('owner check limits are saved without enabling responsibility', (
    tester,
  ) async {
    fixture.on('POST', '/api/v1/proactivity/cases', proactiveFixture());
    await tester.pumpWidget(app());
    await settle(tester);
    await tester.tap(find.text('New responsibility'));
    await settle(tester);
    await tester.enterText(
      find.byType(TextFormField).at(0),
      'Follow the repair',
    );
    await tester.enterText(
      find.byType(TextFormField).at(1),
      'Track the repair outcome',
    );
    final settings = find.text('Checks and interruptions');
    FocusManager.instance.primaryFocus?.unfocus();
    await settle(tester);
    await tester.dragUntilVisible(
      settings,
      find.byType(ListView).last,
      const Offset(0, -250),
    );
    await settle(tester);
    await tester.tap(settings);
    await settle(tester);
    final cadence = find.widgetWithText(
      TextFormField,
      'Minutes between checks (15–10080)',
    );
    await tester.ensureVisible(cadence);
    await settle(tester);
    await tester.enterText(cadence, '120');
    await tester.tap(find.text('Save'));
    await settle(tester);
    final body =
        fixture.sent('POST', '/api/v1/proactivity/cases').single.body as Map;
    expect(body['enabled'], false);
    expect((body['policy'] as Map)['cadenceMinutes'], 120);
    expect((body['policy'] as Map)['timeZoneId'], 'Europe/Amsterdam');
  });

  testWidgets('weekly calendar capacity requires an explicit owner choice', (
    tester,
  ) async {
    final value = {...proactiveFixture(), 'kind': 'goal_capacity'};
    fixture.on('GET', '/api/v1/proactivity/cases/$proactiveFixtureId', value);
    fixture.on('PUT', '/api/v1/proactivity/cases/$proactiveFixtureId', value);
    await tester.pumpWidget(app(id: proactiveFixtureId));
    await settle(tester);
    await tester.tap(find.text('Edit responsibility'));
    await settle(tester);
    final settings = find.text('Checks and interruptions');
    await tester.dragUntilVisible(
      settings,
      find.byType(ListView).last,
      const Offset(0, -250),
    );
    await settle(tester);
    await tester.tap(settings);
    await settle(tester);
    final calendar = find.text("Include this week's calendar availability");
    await tester.ensureVisible(calendar);
    await settle(tester);
    await tester.tap(calendar);
    await tester.tap(find.text('Save'));
    await settle(tester);
    final body =
        fixture
                .sent('PUT', '/api/v1/proactivity/cases/$proactiveFixtureId')
                .single
                .body
            as Map;
    expect((body['policy'] as Map)['useCalendarCapacity'], true);
  });

  testWidgets(
    'trip delay monitoring links the chosen watch to a verified itinerary item',
    (tester) async {
      tester.view.physicalSize = const Size(393, 852);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      const trip = 'trip:019a4320-0000-7000-8000-000000000002';
      const watch = 'watch:019a4320-0000-7000-8000-000000000003';
      final sources = [
        {
          'ref': trip,
          'title': 'Weekend away',
          'tripItems': [
            {'id': 'flight', 'title': 'Flight', 'verified': true},
          ],
        },
        {'ref': watch, 'title': 'Flight delay', 'numericValue': 60},
      ];
      final value = {
        ...proactiveFixture(),
        'kind': 'trip',
        'sourceRefs': [trip, watch],
        'context': sources,
      };
      fixture.on('GET', '/api/v1/proactivity/cases/$proactiveFixtureId', value);
      fixture.on('GET', '/api/v1/proactivity/cases/sources', sources);
      fixture.on('PUT', '/api/v1/proactivity/cases/$proactiveFixtureId', value);
      await tester.pumpWidget(app(id: proactiveFixtureId));
      await settle(tester);
      await tester.tap(find.text('Edit responsibility'));
      await settle(tester);
      final add = find.text('Watch a trip change');
      await tester.dragUntilVisible(
        add,
        find.byType(ListView).last,
        const Offset(0, -250),
      );
      await settle(tester);
      await tester.tap(add);
      await settle(tester);
      final statement = find
          .descendant(
            of: find.byType(AlertDialog),
            matching: find.byType(TextFormField),
          )
          .first;
      await tester.enterText(statement, 'Follow the published flight delay');
      FocusManager.instance.primaryFocus?.unfocus();
      await settle(tester);
      final source = find.byKey(const ValueKey('assumption-source-true'));
      await tester.ensureVisible(source);
      await settle(tester);
      await tester.tap(source);
      await settle(tester);
      await tester.tap(find.text('Flight delay').last);
      await settle(tester);
      final item = find.byKey(const Key('trip-delay-item'));
      await tester.ensureVisible(item);
      await settle(tester);
      await tester.tap(item);
      await settle(tester);
      await tester.tap(find.text('Weekend away · Flight').last);
      await settle(tester);
      await tester.tap(find.text('Save').last);
      await settle(tester);
      await tester.tap(find.text('Save'));
      await settle(tester);
      final body =
          fixture
                  .sent('PUT', '/api/v1/proactivity/cases/$proactiveFixtureId')
                  .single
                  .body
              as Map;
      final binding = (body['assumptions'] as List).single as Map;
      expect(binding['effect'], 'delay_minutes');
      expect(binding['sourceRef'], watch);
      expect(binding['tripRef'], trip);
      expect(binding['tripItemId'], 'flight');
    },
  );
}
