import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/settings/locale_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> view({String? language, String? currency}) => {
  'language': language,
  'languageName': language == 'nl' ? 'Nederlands' : null,
  'baseCurrency': currency,
  'languages': [
    {'code': 'en', 'name': 'English'},
    {'code': 'nl', 'name': 'Nederlands'},
  ],
  'currencies': ['EUR', 'USD'],
  'explanation': 'Nothing is converted.',
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
      MaterialApp(home: LocaleScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('nothing is selected until the owner chooses', (tester) async {
    http.on('GET', '/api/v1/preferences/locale', view());
    await show(tester);

    expect(find.text('No language set · no currency'), findsOneWidget);
    expect(
      tester
          .widget<ChoiceChip>(find.byKey(const Key('locale-language-none')))
          .selected,
      isTrue,
    );
    expect(
      tester
          .widget<ChoiceChip>(find.byKey(const Key('locale-currency-none')))
          .selected,
      isTrue,
    );
  });

  testWidgets('choosing a language and a currency saves each on its own', (
    tester,
  ) async {
    http.on('GET', '/api/v1/preferences/locale', view());
    http.on(
      'PUT',
      '/api/v1/preferences/locale',
      view(language: 'nl', currency: 'EUR'),
    );
    await show(tester);

    await tester.tap(find.byKey(const Key('locale-language-nl')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('locale-currency-EUR')));
    await tester.pumpAndSettle();

    final bodies = http
        .sent('PUT', '/api/v1/preferences/locale')
        .map((r) => r.body as Map)
        .toList();
    expect(bodies[0], {'language': 'nl'});
    expect(bodies[1], {'currency': 'EUR'});
    expect(find.text('Nederlands · EUR'), findsOneWidget);
  });

  testWidgets('clearing sends an empty value rather than guessing', (
    tester,
  ) async {
    http.on(
      'GET',
      '/api/v1/preferences/locale',
      view(language: 'nl', currency: 'EUR'),
    );
    http.on('PUT', '/api/v1/preferences/locale', view());
    await show(tester);

    await tester.tap(find.byKey(const Key('locale-language-none')));
    await tester.pumpAndSettle();

    expect(
      (http.sent('PUT', '/api/v1/preferences/locale').single.body
          as Map)['language'],
      '',
    );
  });
}
