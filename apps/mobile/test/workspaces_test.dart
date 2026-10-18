import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/profiles/workspaces_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> view({String id = 'w1', String? problem}) => {
  'workspace': {
    'id': id,
    'name': 'Weekly research',
    'instruction': 'Summarise what changed',
  },
  'profileName': 'Research analyst',
  'projectName': 'Renovation',
  'problem': problem,
  'scope': problem == null
      ? {
          'memory': 'Only pinned memories',
          'files': '1 document collection',
          'limits': ['Cannot save new memories.'],
        }
      : null,
};

void main() {
  late FixtureHttp http;
  late List<String> opened;

  setUp(() {
    http = FixtureHttp();
    opened = [];
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: WorkspacesScreen(
          http: http.client(),
          onOpenConversation: (id) async => opened.add(id),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'an empty list explains that a workspace holds no access of its own',
    (tester) async {
      http.on('GET', '/api/v1/workspaces', <Object>[]);
      await show(tester);

      expect(find.byKey(const Key('workspace-start')), findsOneWidget);
      expect(
        find.textContaining('that profile and its limits'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'each workspace shows its profile limits and starts an ordinary chat',
    (tester) async {
      http.on('GET', '/api/v1/workspaces', [view()]);
      http.on('POST', '/api/v1/workspaces/w1/launch', {
        'profileId': 'p1',
        'projectId': 'pr1',
        'title': 'Weekly research',
        'openingMessage': 'Summarise what changed',
      });
      http.on('POST', '/api/v1/conversations', {'id': 'c1'}, status: 201);
      await show(tester);

      expect(find.textContaining('Cannot save new memories.'), findsOneWidget);
      await tester.tap(find.byKey(const Key('workspace-launch-w1')));
      await tester.pumpAndSettle();

      final body =
          http.sent('POST', '/api/v1/conversations').single.body as Map;
      expect(body['profileId'], 'p1');
      expect(body['projectId'], 'pr1');
      expect(opened, ['c1']);
      expect(
        find.textContaining('Suggested first message: Summarise what changed'),
        findsOneWidget,
      );
    },
  );

  testWidgets('a workspace whose profile is gone cannot start and says why', (
    tester,
  ) async {
    http.on('GET', '/api/v1/workspaces', [
      view(problem: 'Its profile was deleted. Choose another one.'),
    ]);
    await show(tester);

    expect(find.text('Needs a fix'), findsOneWidget);
    expect(find.textContaining('Its profile was deleted'), findsOneWidget);
    await tester.tap(find.byKey(const Key('workspace-launch-w1')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/workspaces/w1/launch'), isEmpty);
    expect(opened, isEmpty);
  });

  testWidgets('creating one posts the chosen profile and project', (
    tester,
  ) async {
    http.on('GET', '/api/v1/workspaces', <Object>[]);
    http.on('GET', '/api/v1/profiles', [
      {'id': 'p1', 'name': 'Research analyst'},
    ]);
    http.on('GET', '/api/v1/projects', [
      {'id': 'pr1', 'name': 'Renovation'},
    ]);
    http.on('POST', '/api/v1/workspaces', view(), status: 201);
    await show(tester);

    await tester.tap(find.byKey(const Key('workspace-start')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('workspace-name')),
      'Weekly research',
    );
    await tester.tap(find.byKey(const Key('workspace-profile-p1')));
    await tester.tap(find.byKey(const Key('workspace-project-pr1')));
    await tester.tap(find.byKey(const Key('workspace-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/workspaces').single.body as Map;
    expect(body['name'], 'Weekly research');
    expect(body['profileId'], 'p1');
    expect(body['projectId'], 'pr1');
  });
}
