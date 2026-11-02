import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/settings/settings_view.dart';
import 'package:jarvis_mobile/theme.dart';

List<String> hits(String query) => [
  for (final match in searchSettings(query)) match.item.destination,
];

void main() {
  group('searchSettings', () {
    test('finds a setting by title, description and alias', () {
      expect(hits('appearance').first, 'appearance');
      expect(hits('dark mode').first, 'appearance');
      expect(hits('face id'), contains('app-lock'));
      expect(hits('currency'), contains('locale'));
    });

    test('Dutch words and accents work', () {
      expect(hits('tijdzone').first, 'home-zone');
      expect(hits('taal').first, 'locale');
      expect(hits('donker').first, 'appearance');
      expect(hits('Instellingen'), isEmpty); // not a setting of its own
    });

    test('a title ranks before an alias, before a description word', () {
      final matches = searchSettings('voice');
      expect(matches.first.item.destination, 'voice-settings');
      final scores = [for (final m in matches) m.score];
      expect(scores, [...scores]..sort((a, b) => b.compareTo(a)));
    });

    test('every result says which group it lives in', () {
      final match = searchSettings('briefing')
          .firstWhere((m) => m.item.destination == 'briefing');
      expect(match.group, 'General');
    });

    test('an empty or unmatched query finds nothing', () {
      expect(searchSettings('   '), isEmpty);
      expect(searchSettings('zzzqqq'), isEmpty);
    });

    test('every alias belongs to a real setting', () {
      final destinations = {
        for (final (_, items) in settingsGroups)
          for (final item in items) item.destination,
      };
      for (final key in settingAliases.keys) {
        expect(destinations, contains(key), reason: key);
      }
    });
  });

  group('SettingsView', () {
    late List<String> opened;
    setUp(() => opened = []);

    Future<void> show(WidgetTester tester) async {
      tester.view.physicalSize = const Size(420, 1400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: Scaffold(
            body: SettingsView(connected: true, onOpen: opened.add),
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('a result opens the page and shows its group', (tester) async {
      await show(tester);
      await tester.enterText(
        find.byKey(const Key('settings-search')),
        'dark mode',
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('settings-appearance')), findsOneWidget);
      expect(find.textContaining('General ·'), findsOneWidget);
      // Groups collapse into one result list while searching.
      expect(find.text('Connections'), findsNothing);

      await tester.tap(find.byKey(const Key('settings-appearance')));
      expect(opened, ['appearance']);
    });

    testWidgets('no match says so; clearing brings the groups back', (
      tester,
    ) async {
      await show(tester);
      await tester.enterText(find.byKey(const Key('settings-search')), 'zzz');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('settings-no-match')), findsOneWidget);

      await tester.tap(find.byKey(const Key('settings-clear-search')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('settings-no-match')), findsNothing);
      expect(find.text('General'), findsOneWidget);
    });
  });
}
