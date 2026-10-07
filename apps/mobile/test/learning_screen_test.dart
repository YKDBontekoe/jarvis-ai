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

  Map<String, Object?> autonomy({bool enabled = true}) => {
    'enabled': enabled,
    'heartbeatMayStartTasks': true,
    'maxHeartbeatTasksPerDay': 3,
    'maxHeartbeatTasksPerRun': 1,
    'triageInbox': true,
    'digestInsteadOfDrop': true,
  };

  void serveLearning() => http.on('GET', '/api/v1/learning/status', {
    'settings': _settings(),
    'state': {'lastSummary': null},
    'dreaming': {'lastSummary': null, 'diary': <Object>[]},
    'activity': <Object>[],
  });

  testWidgets(
    'switching off background tasks saves the whole autonomy record',
    (tester) async {
      serveLearning();
      http.on('GET', '/api/v1/settings/autonomy', autonomy());
      http.on('PUT', '/api/v1/settings/autonomy', {
        ...autonomy(),
        'heartbeatMayStartTasks': false,
      });
      await show(tester);

      await tester.ensureVisible(
        find.byKey(const Key('autonomy-heartbeatMayStartTasks')),
      );
      await tester.tap(
        find.byKey(const Key('autonomy-heartbeatMayStartTasks')),
      );
      await tester.pumpAndSettle();

      final body =
          http.sent('PUT', '/api/v1/settings/autonomy').single.body!
              as Map<String, dynamic>;
      expect(body['heartbeatMayStartTasks'], false);
      expect(body['enabled'], true);
      expect(body['maxHeartbeatTasksPerDay'], 3);
    },
  );

  testWidgets('the master switch off greys out the other autonomy switches', (
    tester,
  ) async {
    serveLearning();
    http.on('GET', '/api/v1/settings/autonomy', autonomy(enabled: false));
    await show(tester);

    await tester.ensureVisible(find.byKey(const Key('autonomy-triageInbox')));
    final triage = tester.widget<SwitchListTile>(
      find.byKey(const Key('autonomy-triageInbox')),
    );
    final master = tester.widget<SwitchListTile>(
      find.byKey(const Key('autonomy-enabled')),
    );

    expect(triage.onChanged, isNull);
    expect(master.onChanged, isNotNull);
    expect(master.value, false);
  });

  testWidgets('an older server without autonomy settings hides the card', (
    tester,
  ) async {
    serveLearning();
    await show(tester);

    expect(find.byKey(const Key('autonomy-enabled')), findsNothing);
    expect(find.text('Acting on its own'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  Map<String, Object?> status() => {
    'settings': _settings(heartbeat: true),
    'state': {'lastSummary': null},
    'dreaming': {'lastSummary': null, 'diary': <Object>[]},
    'activity': <Object>[],
  };

  Map<String, Object?> proposal(
    String id,
    String kind,
    String status, {
    bool canUndo = false,
  }) => {
    'id': id,
    'kind': kind,
    'title': 'Title $id',
    'evidence': 'Evidence $id',
    'confidence': 0.7,
    'status': status,
    'createdAt': DateTime.now().toIso8601String(),
    'updatedAt': DateTime.now().toIso8601String(),
    'canUndo': canUndo,
  };

  testWidgets('suggestions to review can be accepted or dismissed', (
    tester,
  ) async {
    http.on('GET', '/api/v1/learning/status', status());
    http.on('GET', '/api/v1/improvements', [
      proposal('p1', 'skill', 'pending'),
      proposal('p2', 'memory', 'pending'),
    ]);
    http.on('POST', '/api/v1/improvements/p1/accept', {});
    http.on('POST', '/api/v1/improvements/p2/dismiss', null, status: 204);
    await show(tester);

    expect(find.text('Suggestions to review'), findsOneWidget);
    expect(find.text('Title p1'), findsOneWidget);
    expect(find.text('Save skill'), findsOneWidget);
    expect(find.text('Remember it'), findsOneWidget);

    await tester.ensureVisible(find.byKey(const Key('improvement-accept-p1')));
    await tester.tap(find.byKey(const Key('improvement-accept-p1')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/improvements/p1/accept'), hasLength(1));

    await tester.ensureVisible(find.byKey(const Key('improvement-dismiss-p2')));
    await tester.tap(find.byKey(const Key('improvement-dismiss-p2')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/improvements/p2/dismiss'), hasLength(1));
  });

  testWidgets('what Jarvis saved on its own can be undone', (tester) async {
    http.on('GET', '/api/v1/learning/status', status());
    http.on('GET', '/api/v1/improvements', [
      proposal('p3', 'memory', 'applied', canUndo: true),
    ]);
    http.on('POST', '/api/v1/improvements/p3/undo', null, status: 204);
    await show(tester);

    expect(find.text('Jarvis did this'), findsOneWidget);
    expect(find.text('Saved automatically'), findsOneWidget);
    expect(find.text('Suggestions to review'), findsNothing);

    await tester.ensureVisible(find.byKey(const Key('improvement-undo-p3')));
    await tester.tap(find.byKey(const Key('improvement-undo-p3')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/improvements/p3/undo'), hasLength(1));
  });

  testWidgets('an older server without improvements shows no section', (
    tester,
  ) async {
    http.on('GET', '/api/v1/learning/status', status());
    await show(tester);

    expect(find.byKey(const Key('improvement-suggestions')), findsNothing);
  });

  testWidgets('the improvement switches save the full learning settings', (
    tester,
  ) async {
    http.on('GET', '/api/v1/learning/status', status());
    http.on('PUT', '/api/v1/settings/learning', _settings(heartbeat: true));
    await show(tester);

    await tester.ensureVisible(
      find.byKey(const Key('learning-proposeImprovements')),
    );
    await tester.tap(find.byKey(const Key('learning-proposeImprovements')));
    await tester.pumpAndSettle();

    final body =
        http.sent('PUT', '/api/v1/settings/learning').single.body
            as Map<String, dynamic>;
    expect(body['proposeImprovements'], false);
    expect(body['heartbeatEnabled'], true);
  });
}
