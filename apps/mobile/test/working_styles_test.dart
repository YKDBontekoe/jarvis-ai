import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/profiles/working_styles_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> preset(
  String id,
  String name, {
  bool exists = false,
  bool needs = false,
}) => {
  'preset': {
    'id': id,
    'name': name,
    'description': '$name does things.',
    'needsSources': needs,
  },
  'existingProfileId': exists ? 'p1' : null,
  'scope': {
    'memory': 'All your memories',
    'files': 'All your documents',
    'skills': 'All your skills',
    'limits': needs ? ['Cannot save new memories.'] : <String>[],
  },
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/profiles/presets', [
      preset('planning', 'Planning partner', exists: true),
      preset('research', 'Research analyst', needs: true),
    ]);
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: WorkingStylesScreen(http: http.client(), currentProfileId: 'p0'),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('styles show what they cover and what is still to choose', (
    tester,
  ) async {
    await show(tester);

    expect(find.text('Planning partner'), findsOneWidget);
    expect(
      find.textContaining('Choose its documents or skills'),
      findsOneWidget,
    );
    expect(find.textContaining('Cannot save new memories.'), findsOneWidget);
    expect(find.text('Created'), findsOneWidget);
  });

  testWidgets(
    'a suggestion that widens access says so and creates nothing by itself',
    (tester) async {
      http.on('POST', '/api/v1/profiles/suggest', {
        'kind': 'preset',
        'presetId': 'research',
        'name': 'Research analyst',
        'reason': 'You mentioned sources.',
        'scopeChange': {
          'details': ['Memory recall scope is different.'],
        },
        'broadens': ['It can save new memories.'],
        'requiresConfirmation': true,
        'note': 'You do not have this profile yet.',
      });
      await show(tester);

      await tester.enterText(
        find.byKey(const Key('styles-task')),
        'compare these sources',
      );
      await tester.tap(find.byKey(const Key('styles-suggest')));
      await tester.pumpAndSettle();

      final body =
          http.sent('POST', '/api/v1/profiles/suggest').single.body as Map;
      expect(body['text'], 'compare these sources');
      expect(body['currentProfileId'], 'p0');
      expect(find.byKey(const Key('styles-broadens')), findsOneWidget);
      expect(
        find.textContaining('Memory recall scope is different.'),
        findsOneWidget,
      );
      expect(
        http.requests.where(
          (r) => r.method == 'POST' && r.path.contains('/create'),
        ),
        isEmpty,
      );
    },
  );

  testWidgets('creating a style posts once and reloads', (tester) async {
    http.on('POST', '/api/v1/profiles/presets/research/create', {
      'id': 'p2',
    }, status: 201);
    await show(tester);

    await tester.tap(find.byKey(const Key('style-create-research')));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/profiles/presets/research/create'),
      hasLength(1),
    );
    expect(http.sent('GET', '/api/v1/profiles/presets'), hasLength(2));
  });

  testWidgets('no match says nothing changes', (tester) async {
    http.on('POST', '/api/v1/profiles/suggest', {
      'kind': 'none',
      'note': 'Nothing here points to a particular working style, so nothing changes.',
      'broadens': <String>[],
    });
    await show(tester);

    await tester.enterText(find.byKey(const Key('styles-task')), 'weather');
    await tester.tap(find.byKey(const Key('styles-suggest')));
    await tester.pumpAndSettle();

    expect(find.text('NO CHANGE'), findsOneWidget);
    expect(find.byKey(const Key('styles-create-suggested')), findsNothing);
  });
}
