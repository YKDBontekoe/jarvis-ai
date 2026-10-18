import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/trips/trip_screen.dart';
import 'package:jarvis_mobile/features/trips/trips_screen.dart';

import 'support/fixture_http.dart';

final today = DateTime(2026, 10, 18);

Map<String, Object?> view({bool chosen = false}) => {
  'trip': {
    'id': 't1',
    'title': 'Lisbon in November',
    'destination': 'Lisbon',
    'start': '2026-11-12',
    'end': '2026-11-15',
    'currency': 'EUR',
  },
  'options': [
    {
      'option': {
        'id': 'o1',
        'kind': 'transport',
        'title': 'TAP 08:15',
        'chosen': chosen,
      },
      'quote': 'current',
      'quoteNote': 'Quoted 16 Oct from its source.',
      'priceText': 'EUR 240.00',
    },
    {
      'option': {
        'id': 'o2',
        'kind': 'stay',
        'title': 'Hotel Alfama',
        'chosen': false,
      },
      'quote': 'outdated',
      'quoteNote': 'Quoted 12 days ago. Prices change: check it again.',
      'priceText': 'EUR 410.00',
    },
  ],
  'days': <Object>[],
  'budget': {'currency': 'EUR', 'budget': 1500, 'chosen': chosen ? 240 : 0},
  'checklist': [
    {
      'id': 'documents',
      'group': 'Documents',
      'text': 'Passport or ID',
      'note': 'Check the official government source.',
      'done': false,
    },
  ],
  'holds': [
    {
      'title': 'Flight',
      'date': '2026-11-12',
      'at': '08:15:00',
      'why': 'Departure',
    },
  ],
  'unknowns': ['No stay chosen yet.'],
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets(
    'an empty list invites a first trip; creating posts and opens it',
    (tester) async {
      http.on('GET', '/api/v1/trips', <Object>[]);
      http.on('POST', '/api/v1/trips', view(), status: 201);
      http.on('GET', '/api/v1/trips/t1', view());
      await show(tester, TripsScreen(http: http.client(), today: today));

      await tester.tap(find.byKey(const Key('trip-start')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('trip-destination')),
        'Lisbon',
      );
      await tester.enterText(find.byKey(const Key('trip-budget')), '1500');
      await tester.tap(find.byKey(const Key('trip-create')));
      await tester.pumpAndSettle();

      final body = http.sent('POST', '/api/v1/trips').single.body as Map;
      expect(body['destination'], 'Lisbon');
      expect(body['title'], 'Lisbon');
      expect(body['budget'], 1500);
      expect(find.text('Lisbon in November'), findsWidgets);
    },
  );

  testWidgets('options show how fresh their quote is', (tester) async {
    http.on('GET', '/api/v1/trips/t1', view());
    await show(
      tester,
      TripScreen(http: http.client(), tripId: 't1', today: today),
    );

    expect(find.text('Current'), findsOneWidget);
    expect(find.text('Outdated'), findsOneWidget);
    expect(find.text('No stay chosen yet.'), findsOneWidget);
  });

  testWidgets('choosing an option and removing one call the API', (
    tester,
  ) async {
    http.on('GET', '/api/v1/trips/t1', view());
    http.on('PUT', '/api/v1/trips/t1/options/o1/chosen', view(chosen: true));
    http.on('DELETE', '/api/v1/trips/t1/options/o2', view(chosen: true));
    await show(
      tester,
      TripScreen(http: http.client(), tripId: 't1', today: today),
    );

    await tester.tap(find.byKey(const Key('trip-choose-o1')));
    await tester.pumpAndSettle();
    expect(
      (http.sent('PUT', '/api/v1/trips/t1/options/o1/chosen').single.body
          as Map)['chosen'],
      true,
    );
    expect(find.text('Chosen'), findsOneWidget);

    await tester.tap(find.byKey(const Key('trip-remove-o2')));
    await tester.pumpAndSettle();
    expect(http.sent('DELETE', '/api/v1/trips/t1/options/o2'), hasLength(1));
  });

  testWidgets('the checklist toggles and holds say nothing was added', (
    tester,
  ) async {
    http.on('GET', '/api/v1/trips/t1', view());
    http.on('PUT', '/api/v1/trips/t1/checklist/documents', view());
    await show(
      tester,
      TripScreen(http: http.client(), tripId: 't1', today: today),
    );

    await tester.scrollUntilVisible(
      find.byKey(const Key('trip-holds-note')),
      300,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.textContaining('Nothing has been added'), findsOneWidget);

    await tester.tap(find.byKey(const Key('trip-check-documents')));
    await tester.pumpAndSettle();
    final body =
        http.sent('PUT', '/api/v1/trips/t1/checklist/documents').single.body
            as Map;
    expect(body['done'], true);
  });

  testWidgets('adding an option posts its source and quote date', (
    tester,
  ) async {
    http.on('GET', '/api/v1/trips/t1', view());
    http.on('POST', '/api/v1/trips/t1/options', view());
    await show(
      tester,
      TripScreen(http: http.client(), tripId: 't1', today: today),
    );

    await tester.ensureVisible(find.byKey(const Key('trip-add-option')));
    await tester.tap(find.byKey(const Key('trip-add-option')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('option-title')),
      'Hotel Baixa',
    );
    await tester.enterText(find.byKey(const Key('option-price')), '380');
    await tester.enterText(
      find.byKey(const Key('option-source')),
      'https://hotel.example/offer',
    );
    await tester.tap(find.byKey(const Key('option-save')));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/trips/t1/options').single.body as Map;
    expect(body['title'], 'Hotel Baixa');
    expect(body['sourceUrl'], 'https://hotel.example/offer');
    expect(body['quotedOn'], '2026-10-18');
  });
}
