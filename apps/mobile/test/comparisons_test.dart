import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/comparisons/comparison_screen.dart';
import 'package:jarvis_mobile/features/comparisons/comparisons_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _id = 'cmp1';

Map<String, Object?> _comparison({
  String? chosen,
  bool thin = false,
  bool tied = false,
}) => {
  'id': _id,
  'definition': {
    'title': 'Which boiler?',
    'question': 'Replace the 2010 boiler before winter',
    'criteria': [
      {'id': 'price', 'name': 'Price', 'weight': 8},
      {'id': 'noise', 'name': 'Noise', 'weight': 2},
    ],
    'options': [
      {
        'id': 'alpha',
        'name': 'Alpha',
        'summary': null,
        'cells': {
          'price': {
            'score': 9,
            'note': '€1,900 incl. fitting',
            'source': 'quote-a.pdf',
            'asOf': '2026-10-01',
          },
          'noise': {'score': null},
        },
      },
      {
        'id': 'beta',
        'name': 'Beta',
        'summary': null,
        'cells': {
          'price': {'score': 5},
          'noise': {'score': 9},
        },
      },
    ],
    'chosenOptionId': chosen,
    'decisionId': chosen == null ? null : 'd1',
  },
  'result': {
    'rows': [
      {
        'optionId': 'alpha',
        'name': 'Alpha',
        'score': 9.0,
        'coverage': .8,
        'unknownCriteria': ['Noise'],
        'rank': 1,
        'tied': tied,
        'thin': thin,
      },
      {
        'optionId': 'beta',
        'name': 'Beta',
        'score': 5.8,
        'coverage': 1.0,
        'unknownCriteria': <Object>[],
        'rank': 2,
        'tied': false,
        'thin': false,
      },
    ],
    'leaderId': 'alpha',
    'leaderIsTied': tied,
    'sensitivity': [
      {
        'criterionId': 'price',
        'criterionName': 'Price',
        'changesLeader': true,
        'leaderWithout': 'Beta',
      },
      {
        'criterionId': 'noise',
        'criterionName': 'Noise',
        'changesLeader': false,
        'leaderWithout': null,
      },
    ],
    'warnings': thin
        ? [
            'Some options are missing most of what you care about, so their score is a rough guide.',
          ]
        : <Object>[],
  },
  'libraryItemId': null,
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 3200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(_host(child));
    await tester.pumpAndSettle();
  }

  testWidgets(
    'the list invites a first comparison and creates one from plain lines',
    (tester) async {
      http
        ..on('GET', '/api/v1/comparisons', <Object>[])
        ..on('POST', '/api/v1/comparisons', _comparison(), status: 201)
        ..on('GET', '/api/v1/comparisons/$_id', _comparison());
      await show(tester, ComparisonsScreen(http: http.client()));
      expect(find.text('Weigh your options'), findsOneWidget);

      await tester.tap(find.byKey(const Key('comparison-start')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<FilledButton>(find.byKey(const Key('comparison-create')))
            .onPressed,
        isNull,
      );
      await tester.enterText(
        find.byKey(const Key('comparison-title-field')),
        'Which boiler?',
      );
      await tester.enterText(
        find.byKey(const Key('comparison-options-field')),
        'Bosch\nVaillant\n',
      );
      await tester.enterText(
        find.byKey(const Key('comparison-criteria-field')),
        'Price\nNoise',
      );
      await tester.pump();
      await tester.tap(find.byKey(const Key('comparison-create')));
      await tester.pumpAndSettle();

      final body = http.sent('POST', '/api/v1/comparisons').single.body as Map;
      final definition = body['definition'] as Map;
      expect(definition['title'], 'Which boiler?');
      expect((definition['options'] as List).map((o) => (o as Map)['id']), [
        'bosch',
        'vaillant',
      ]);
      expect(
        (definition['criteria'] as List).map((c) => (c as Map)['weight']),
        [5, 5],
      );
      // It opens the new comparison.
      expect(find.text('Ranking'), findsOneWidget);
    },
  );

  testWidgets(
    'the ranking shows scores, what is unknown and what could change the answer',
    (tester) async {
      http.on(
        'GET',
        '/api/v1/comparisons/$_id',
        _comparison(thin: true, tied: true),
      );
      await show(
        tester,
        ComparisonScreen(http: http.client(), comparisonId: _id),
      );

      expect(find.text('9 / 10'), findsOneWidget);
      expect(find.text('5.8 / 10'), findsOneWidget);
      expect(find.text('Unknown: Noise'), findsOneWidget);
      expect(find.text('80% of what you care about is known'), findsOneWidget);
      expect(find.text('Tied'), findsOneWidget);
      expect(find.text('Mostly unknown'), findsOneWidget);
      expect(find.textContaining('rough guide'), findsOneWidget);
      expect(find.byKey(const Key('comparison-sensitivity')), findsOneWidget);
      expect(
        find.textContaining('without “Price” Beta would lead'),
        findsOneWidget,
      );
      expect(find.text('€1,900 incl. fitting'), findsOneWidget);
      expect(
        find.textContaining('quote-a.pdf · seen 2026-10-01'),
        findsOneWidget,
      );
      // An unknown answer says so; it is not shown as a zero.
      expect(find.text('Unknown'), findsOneWidget);
    },
  );

  testWidgets(
    'changing a weight sends the edited definition and shows the server ranking',
    (tester) async {
      http
        ..on('GET', '/api/v1/comparisons/$_id', _comparison())
        ..on('PUT', '/api/v1/comparisons/$_id', _comparison());
      await show(
        tester,
        ComparisonScreen(http: http.client(), comparisonId: _id),
      );

      final slider = find.byKey(const Key('comparison-slider-noise'));
      await tester.drag(slider, const Offset(300, 0));
      await tester.pumpAndSettle();

      final sent =
          http.sent('PUT', '/api/v1/comparisons/$_id').single.body as Map;
      final criteria = (sent['definition'] as Map)['criteria'] as List;
      expect((criteria[0] as Map)['weight'], 8);
      expect((criteria[1] as Map)['weight'], greaterThan(2));
    },
  );

  testWidgets('a cell can be set unknown, with facts, a source and a date', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/comparisons/$_id', _comparison())
      ..on('PUT', '/api/v1/comparisons/$_id', _comparison());
    await show(
      tester,
      ComparisonScreen(http: http.client(), comparisonId: _id),
    );

    await tester.ensureVisible(find.byKey(const Key('cell-beta-noise')));
    await tester.tap(find.byKey(const Key('cell-beta-noise')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('cell-known')));
    await tester.enterText(
      find.byKey(const Key('cell-note')),
      'Not stated in the brochure',
    );
    await tester.enterText(find.byKey(const Key('cell-asof')), 'tomorrow');
    await tester.pump();
    expect(
      tester.widget<FilledButton>(find.byKey(const Key('cell-save'))).onPressed,
      isNull,
    );
    await tester.enterText(find.byKey(const Key('cell-asof')), '2026-10-16');
    await tester.pump();
    await tester.tap(find.byKey(const Key('cell-save')));
    await tester.pumpAndSettle();

    final sent =
        http.sent('PUT', '/api/v1/comparisons/$_id').single.body as Map;
    final beta = ((sent['definition'] as Map)['options'] as List)[1] as Map;
    final cell = (beta['cells'] as Map)['noise'] as Map;
    expect(cell['score'], isNull);
    expect(cell['note'], 'Not stated in the brochure');
    expect(cell['asOf'], '2026-10-16');
    // Nothing else changed.
    expect(((beta['cells'] as Map)['price'] as Map)['score'], 5);
  });

  testWidgets(
    'choosing an option logs a decision and once decided no other can be chosen',
    (tester) async {
      http
        ..on('GET', '/api/v1/comparisons/$_id', _comparison())
        ..on('POST', '/api/v1/comparisons/$_id/decide', {
          'decisionId': 'd1',
          'optionId': 'alpha',
          'optionName': 'Alpha',
          'created': true,
        });
      await show(
        tester,
        ComparisonScreen(http: http.client(), comparisonId: _id),
      );

      await tester.ensureVisible(
        find.byKey(const Key('comparison-choose-alpha')),
      );
      await tester.tap(find.byKey(const Key('comparison-choose-alpha')));
      await tester.pumpAndSettle();
      expect(find.textContaining('Nothing is bought or sent'), findsOneWidget);
      http.on('GET', '/api/v1/comparisons/$_id', _comparison(chosen: 'alpha'));
      await tester.tap(find.byKey(const Key('decide-save')));
      await tester.pumpAndSettle();

      final sent =
          http.sent('POST', '/api/v1/comparisons/$_id/decide').single.body
              as Map;
      expect(sent['optionId'], 'alpha');
      expect(sent['probability'], .7);
      expect(sent['reviewOn'], matches(RegExp(r'^\d{4}-\d{2}-\d{2}$')));
      expect(find.text('Chosen'), findsOneWidget);
      expect(find.text('Your choice, logged as a decision'), findsOneWidget);
      expect(find.text('Already decided'), findsOneWidget);
    },
  );

  testWidgets('a deleted comparison says so', (tester) async {
    http.on('GET', '/api/v1/comparisons/$_id', null, status: 404);
    await show(
      tester,
      ComparisonScreen(http: http.client(), comparisonId: _id),
    );
    expect(find.text('This comparison is gone'), findsOneWidget);
  });

  testWidgets(
    'a criterion can be kept out of a shared copy, which is only copied',
    (tester) async {
      String? copied;
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        (call) async {
          if (call.method == 'Clipboard.setData') {
            copied = (call.arguments as Map)['text'] as String?;
          }
          return null;
        },
      );
      addTearDown(
        () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
          SystemChannels.platform,
          null,
        ),
      );
      http
        ..on('GET', '/api/v1/comparisons/$_id', _comparison())
        ..on('PUT', '/api/v1/comparisons/$_id', _comparison())
        ..on('GET', '/api/v1/comparisons/$_id/export', {
          'markdown': '# Which boiler?',
          'leftOut': true,
        });
      await show(
        tester,
        ComparisonScreen(http: http.client(), comparisonId: _id),
      );

      await tester.tap(find.byKey(const Key('comparison-private-price')));
      await tester.pumpAndSettle();
      final definition =
          (http.sent('PUT', '/api/v1/comparisons/$_id').single.body
                  as Map)['definition']
              as Map;
      final price = (definition['criteria'] as List).cast<Map>().firstWhere(
        (c) => c['id'] == 'price',
      );
      expect(price['private'], true);

      await tester.tap(find.byTooltip('More'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Copy a shareable version'));
      await tester.pumpAndSettle();

      expect(http.sent('GET', '/api/v1/comparisons/$_id/export'), hasLength(1));
      expect(copied, '# Which boiler?');
      expect(
        find.textContaining('Private criteria were left out'),
        findsOneWidget,
      );
    },
  );
}
