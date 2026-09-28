import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/learning/learning_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _settings({
  bool heartbeat = false,
  bool dreaming = true,
}) => {
  'heartbeatEnabled': heartbeat,
  'heartbeatMinutes': 60,
  'learnPersona': true,
  'autoCreateSkills': true,
  'autoActivateSkills': true,
  'proactiveCheckIns': true,
  'quietHoursStart': 22,
  'quietHoursEnd': 7,
  'dreamingEnabled': dreaming,
  'dreamingHour': 3,
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 2800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: LearningScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('turning on the heartbeat saves the full learning settings', (
    tester,
  ) async {
    http.on('GET', '/api/v1/learning/status', {
      'settings': _settings(),
      'state': {'lastSummary': null},
      'dreaming': {'lastSummary': null, 'diary': <Object>[]},
      'activity': [
        {
          'id': 'a1',
          'tool': 'skills',
          'action': 'skill.learned',
          'riskClass': 'moderate',
          'timestamp': DateTime.now().toIso8601String(),
          'success': true,
          'metadataJson':
              '{"resourceId":"x","name":"trip-planning","version":1}',
        },
      ],
    });
    http.on('PUT', '/api/v1/settings/learning', _settings(heartbeat: true));
    await show(tester);

    expect(find.text('Learned the skill trip-planning'), findsOneWidget);
    expect(find.textContaining('Off — Jarvis only learns'), findsOneWidget);

    await tester.tap(find.byKey(const Key('heartbeat-switch')));
    await tester.pumpAndSettle();

    final body =
        http.sent('PUT', '/api/v1/settings/learning').single.body
            as Map<String, dynamic>;
    expect(body['heartbeatEnabled'], true);
    expect(body['dreamingEnabled'], true);
    expect(body['quietHoursStart'], 22);
    expect(
      find.textContaining('Reflects and checks in every 1 h'),
      findsOneWidget,
    );
  });

  testWidgets('reflect now shows what the heartbeat learned', (tester) async {
    http.on('GET', '/api/v1/learning/status', {
      'settings': _settings(heartbeat: true),
      'state': {'lastSummary': 'Nothing new to learn.'},
      'dreaming': {'lastSummary': 'No dream has run yet.', 'diary': <Object>[]},
      'activity': <Object>[],
    });
    http.on('POST', '/api/v1/learning/run', {
      'summary':
          '1 new preference, 1 skill. Review them in Settings. Sent 1 check-in.',
      'checkIns': <Object>[],
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('run-heartbeat')));
    await tester.pumpAndSettle();

    expect(find.textContaining('1 new preference, 1 skill'), findsOneWidget);
  });

  testWidgets('dream now shows what consolidation changed', (tester) async {
    http.on('GET', '/api/v1/learning/status', {
      'settings': _settings(heartbeat: true),
      'state': {'lastSummary': 'Nothing new to learn.'},
      'dreaming': {
        'lastSummary': 'No dream has run yet.',
        'diary': <Object>[],
        'userSummary': 'The user lives in Amsterdam and builds Jarvis.',
        'userSummaryUpdatedAt': DateTime.now().toIso8601String(),
      },
      'activity': <Object>[],
    });
    http.on('POST', '/api/v1/learning/dream', {
      'summary': '1 merged memory, 1 tone/preference.',
      'promoted': 0,
      'merged': 1,
      'personaUpdated': 1,
    });
    await show(tester);

    await tester.ensureVisible(find.byKey(const Key('run-dreaming')));
    await tester.tap(find.byKey(const Key('run-dreaming')));
    await tester.pumpAndSettle();

    expect(find.textContaining('1 merged memory'), findsOneWidget);
    expect(
      find.textContaining('The user lives in Amsterdam and builds Jarvis.'),
      findsOneWidget,
    );
    expect(
      find.textContaining('Included in chat as background'),
      findsOneWidget,
    );
  });

  testWidgets('malformed learning status does not crash the screen', (
    tester,
  ) async {
    http.on('GET', '/api/v1/learning/status', {
      'settings': 'nope',
      'state': <Object>[],
      'dreaming': 3,
      'activity': 'x',
    });
    await show(tester);

    expect(tester.takeException(), isNull);
    expect(find.byKey(const Key('heartbeat-switch')), findsOneWidget);
    expect(
      find.textContaining('Could not load learning settings.'),
      findsNothing,
    );
  });
}
