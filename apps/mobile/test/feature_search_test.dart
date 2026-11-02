import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/everything/everything_screen.dart';
import 'package:jarvis_mobile/features/everything/feature_search.dart';
import 'package:jarvis_mobile/features/everything/recent_features.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

List<String> ids(String query) => [
  for (final spec in searchFeatures(query)) spec.id,
];

void main() {
  group('feature search', () {
    test('everyday English words lead to the feature', () {
      expect(ids('bills'), contains('finance'));
      expect(ids('subscriptions'), contains('finance'));
      expect(ids('diary'), contains('journal'));
      expect(ids('promises'), contains('inbox'));
      expect(ids('contacts'), contains('people'));
    });

    test('Dutch words work too, with or without accents', () {
      expect(ids('dagboek'), contains('journal'));
      expect(ids('abonnementen'), contains('finance'));
      expect(ids('beloftes'), contains('inbox'));
      expect(ids('verjaardagen'), contains('people'));
      expect(ids('instellingen'), contains('settings'));
      expect(foldForSearch('Café Müller'), 'cafe muller');
    });

    test(
      'an exact name ranks before a prefix, which ranks before an alias',
      () {
        // "notes" is an alias of Library, not a feature name.
        final exact = featureScore(tileSpecFor('journal')!, 'journal');
        final prefix = featureScore(tileSpecFor('journal')!, 'jour');
        final alias = featureScore(tileSpecFor('journal')!, 'diary');
        expect(exact, greaterThan(prefix));
        expect(prefix, greaterThan(alias));
        expect(alias, greaterThan(0));
      },
    );

    test('a feature named by the query comes before one that only has it as '
        'an alias', () {
      // "chats" is a feature; "chat" is also an alias of Chats, but WhatsApp
      // lists "messages" as one and must not outrank the exact feature name.
      expect(ids('chats').first, 'chats');
      expect(ids('voice').first, 'voice');
      expect(ids('money').first, 'finance');
    });

    test('nothing matches nonsense, and an empty query matches everything', () {
      expect(ids('zzzqqq'), isEmpty);
      expect(ids('   ').length, tileSpecs.length);
    });

    test('every alias belongs to a real feature', () {
      for (final id in featureAliases.keys) {
        expect(tileSpecFor(id), isNotNull, reason: id);
      }
    });
  });

  group('recent features', () {
    setUp(() => SharedPreferences.setMockInitialValues({}));

    test('keeps the last six distinct features, newest first', () async {
      final recent = RecentFeatures(
        preferences: await SharedPreferences.getInstance(),
      );
      for (final destination in [
        'tasks',
        'reminders',
        'habits',
        'journal',
        'decisions',
        'tasks',
        'weekly-review',
        'briefing',
      ]) {
        await recent.recordDestination(destination);
      }
      expect(recent.ids, [
        'briefing',
        'weekly-review',
        'tasks',
        'decisions',
        'journal',
        'habits',
      ]);
    });

    test('things that are not a feature are not remembered', () async {
      final recent = RecentFeatures(
        preferences: await SharedPreferences.getInstance(),
      );
      await recent.recordDestination('trip:42');
      await recent.recordDestination('sign_out');
      expect(recent.ids, isEmpty);
    });

    test('survives a restart and can be cleared', () async {
      final prefs = await SharedPreferences.getInstance();
      await RecentFeatures(preferences: prefs).recordDestination('people');

      final reopened = RecentFeatures(preferences: prefs);
      await reopened.load();
      expect(reopened.ids, ['people']);

      await reopened.clear();
      final again = RecentFeatures(preferences: prefs);
      await again.load();
      expect(again.ids, isEmpty);
    });
  });

  group('Everything screen', () {
    late List<String> opened;

    setUp(() {
      SharedPreferences.setMockInitialValues({});
      opened = [];
    });

    Future<void> show(WidgetTester tester, RecentFeatures recent) async {
      tester.view.physicalSize = const Size(400, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final http = FixtureHttp();
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: Scaffold(
            body: EverythingScreen(
              source: TileDataSource(http.client()),
              layout: TileLayoutController(),
              onOpen: opened.add,
              recent: recent,
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('typing an everyday word shows the feature and says nothing '
        'else matches', (tester) async {
      await show(tester, RecentFeatures());
      await tester.enterText(
        find.byKey(const Key('everything-search')),
        'dagboek',
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('everything-journal')), findsOneWidget);
      expect(find.byKey(const Key('everything-memory')), findsNothing);
    });

    testWidgets('the Recent strip opens a feature directly, can be cleared and '
        'leaves pinned tiles alone', (tester) async {
      final recent = RecentFeatures();
      await recent.recordDestination('people');
      await recent.recordDestination('files');
      await show(tester, recent);

      expect(find.byKey(const Key('everything-recent')), findsOneWidget);
      expect(find.byKey(const Key('recent-files')), findsOneWidget);
      await tester.tap(find.byKey(const Key('recent-people')));
      expect(opened, ['people']);

      await tester.tap(find.byKey(const Key('everything-clear-recent')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('everything-recent')), findsNothing);
      // The full list is untouched.
      expect(find.byKey(const Key('everything-today')), findsOneWidget);
    });

    testWidgets('Recent is hidden while searching and when empty', (
      tester,
    ) async {
      final recent = RecentFeatures();
      await show(tester, recent);
      expect(find.byKey(const Key('everything-recent')), findsNothing);

      await recent.recordDestination('people');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('everything-recent')), findsOneWidget);
      await tester.enterText(find.byKey(const Key('everything-search')), 'pe');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('everything-recent')), findsNothing);
    });
  });
}
