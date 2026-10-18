import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/settings/home_zone_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';

import 'support/fixture_http.dart';

Map<String, Object?> view({
  String zone = 'UTC',
  String source = 'default',
  bool chosen = false,
  String? explanation,
}) => {
  'homeTimeZone': zone,
  'source': source,
  'chosen': chosen,
  'localNow': 'Sat 17 Oct 14:07',
  'explanation': explanation,
};

Map<String, Object?> preview() => {
  'current': 'UTC',
  'currentSource': 'default',
  'proposed': 'Europe/Amsterdam',
  'nowThere': 'Sat 17 Oct 16:07',
  'effects': [
    {'area': 'Your day', 'text': 'Today will follow Europe/Amsterdam.'},
    {'area': 'Morning brief', 'text': 'Stays at 08:00 in UTC.'},
  ],
};

void main() {
  late FixtureHttp http;
  final original = deviceTimeZoneLookup;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => null;
  });
  tearDown(() => deviceTimeZoneLookup = original);

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: HomeZoneScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'an unchosen zone is admitted as such and nothing changes until you confirm',
    (tester) async {
      http.on(
        'GET',
        '/api/v1/preferences',
        view(explanation: 'You have not chosen one, so Jarvis uses UTC.'),
      );
      http.on('POST', '/api/v1/preferences/home-zone/preview', preview());
      http.on(
        'PUT',
        '/api/v1/preferences/home-zone',
        view(zone: 'Europe/Amsterdam', source: 'chosen', chosen: true),
      );
      await show(tester);

      expect(find.text('NOT CHOSEN YET'), findsOneWidget);
      expect(find.textContaining('uses UTC'), findsOneWidget);
      await tester.tap(find.byKey(const Key('zone-Europe/Amsterdam')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('zone-preview')), findsOneWidget);
      expect(find.text('Stays at 08:00 in UTC.'), findsOneWidget);
      expect(http.sent('PUT', '/api/v1/preferences/home-zone'), isEmpty);

      await tester.tap(find.byKey(const Key('zone-confirm')));
      await tester.pumpAndSettle();

      expect(http.sent('PUT', '/api/v1/preferences/home-zone').single.body, {
        'timeZoneId': 'Europe/Amsterdam',
      });
      expect(find.text('YOUR HOME TIME ZONE'), findsOneWidget);
      expect(find.byKey(const Key('zone-clear')), findsOneWidget);
    },
  );

  testWidgets('declining the preview changes nothing', (tester) async {
    http.on('GET', '/api/v1/preferences', view());
    http.on('POST', '/api/v1/preferences/home-zone/preview', preview());
    await show(tester);

    await tester.tap(find.byKey(const Key('zone-Europe/Amsterdam')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Not now'));
    await tester.pumpAndSettle();

    expect(http.sent('PUT', '/api/v1/preferences/home-zone'), isEmpty);
  });

  testWidgets(
    'the phone zone is offered, previewed and only used when you say so',
    (tester) async {
      deviceTimeZoneLookup = () async => 'Asia/Tokyo';
      http.on(
        'GET',
        '/api/v1/preferences',
        view(zone: 'Europe/Amsterdam', source: 'chosen', chosen: true),
      );
      http.on('POST', '/api/v1/preferences/home-zone/preview', preview());
      await show(tester);

      expect(find.byKey(const Key('zone-device')), findsOneWidget);
      expect(http.sent('PUT', '/api/v1/preferences/home-zone'), isEmpty);
      await tester.tap(find.byKey(const Key('zone-use-device')));
      await tester.pumpAndSettle();
      expect(
        http.sent('POST', '/api/v1/preferences/home-zone/preview').single.body,
        {'timeZoneId': 'Asia/Tokyo'},
      );
      expect(http.sent('PUT', '/api/v1/preferences/home-zone'), isEmpty);
    },
  );

  testWidgets('going back to automatic clears the choice', (tester) async {
    http.on(
      'GET',
      '/api/v1/preferences',
      view(zone: 'Europe/Amsterdam', source: 'chosen', chosen: true),
    );
    http.on('PUT', '/api/v1/preferences/home-zone', view());
    await show(tester);

    await tester.tap(find.byKey(const Key('zone-clear')));
    await tester.pumpAndSettle();

    expect(http.sent('PUT', '/api/v1/preferences/home-zone').single.body, {
      'timeZoneId': null,
    });
    expect(find.text('NOT CHOSEN YET'), findsOneWidget);
  });
}
