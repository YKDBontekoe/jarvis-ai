import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/skills/skills_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _skill(
  String id,
  String name,
  String status, {
  String source = 'learned',
}) => {
  'id': id,
  'name': name,
  'description': 'Use for $name.',
  'instructions': '1. First step for $name.\n2. Second step.',
  'source': source,
  'status': status,
  'isLocked': source == 'user',
  'version': 2,
  'useCount': 3,
  'createdAt': '2026-09-27T10:00:00Z',
  'updatedAt': '2026-09-27T11:00:00Z',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets('groups proposed, active, and disabled skills with sources', (
    tester,
  ) async {
    http.on('GET', '/api/v1/skills', [
      _skill('1', 'trip-planning', 'proposed'),
      _skill('2', 'weekly-review', 'active', source: 'user'),
      _skill('3', 'old-habit', 'disabled', source: 'imported'),
    ]);
    await show(tester, SkillsScreen(http: http.client()));

    expect(find.text('Needs your review'), findsOneWidget);
    expect(find.text('Active'), findsOneWidget);
    expect(find.text('Disabled'), findsOneWidget);
    expect(find.textContaining('Learned · v2 · used 3×'), findsOneWidget);
    expect(find.textContaining('Yours · v2'), findsOneWidget);
    expect(find.textContaining('Imported · v2'), findsOneWidget);
  });

  testWidgets('approving a proposed skill posts the active status', (
    tester,
  ) async {
    http.on('GET', '/api/v1/skills/1', {
      'skill': _skill('1', 'trip-planning', 'proposed'),
      'revisions': [
        {
          'version': 2,
          'description': 'Use for trip-planning.',
          'instructions': '1. First step.',
          'source': 'learned',
          'changeNote': 'User prefers trains',
          'createdAt': '2026-09-27T11:00:00Z',
        },
      ],
    });
    http.on(
      'POST',
      '/api/v1/skills/1/status',
      _skill('1', 'trip-planning', 'active'),
    );
    await show(tester, SkillDetailScreen(http: http.client(), skillId: '1'));

    expect(find.textContaining('User prefers trains'), findsOneWidget);
    expect(find.textContaining('First step for trip-planning'), findsOneWidget);

    await tester.tap(find.byKey(const Key('activate-skill')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/skills/1/status').single.body, {
      'status': 'active',
    });
  });

  testWidgets('importing a SKILL.md posts the pasted markdown', (tester) async {
    http.on('GET', '/api/v1/skills', <Object>[]);
    http.on(
      'POST',
      '/api/v1/skills/import',
      _skill('9', 'imported-skill', 'active'),
    );
    await show(tester, SkillsScreen(http: http.client()));

    expect(find.text('No skills yet'), findsOneWidget);
    await tester.tap(find.byTooltip('Import SKILL.md'));
    await tester.pumpAndSettle();
    const markdown =
        '---\nname: imported-skill\ndescription: Use it well\n---\n\nSteps.';
    await tester.enterText(find.byKey(const Key('skill-markdown')), markdown);
    await tester.tap(find.byKey(const Key('save-skill')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/skills/import').single.body, {
      'markdown': markdown,
    });
  });

  testWidgets('invalid skill payload shows an error instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/skills/1', {'skill': 'nope', 'revisions': 'x'});
    await show(tester, SkillDetailScreen(http: http.client(), skillId: '1'));

    expect(tester.takeException(), isNull);
    expect(find.text('Jarvis returned an invalid skill.'), findsOneWidget);
  });
}
