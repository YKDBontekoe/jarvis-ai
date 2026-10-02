import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/projects/project_editor.dart';
import 'package:jarvis_mobile/features/projects/project_screen.dart';
import 'package:jarvis_mobile/features/projects/project_style.dart';
import 'package:jarvis_mobile/features/projects/projects_screen.dart';
import 'package:jarvis_mobile/features/shell/sidebar.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _id = 'p1';

Map<String, Object?> _project({
  String name = 'Kitchen renovation',
  String? instructions = 'Compare at least three quotes.',
  int chats = 1,
  int files = 1,
  int tasks = 1,
}) => {
  'id': _id,
  'name': name,
  'description': 'A new kitchen before summer',
  'instructions': instructions,
  'color': 'green',
  'createdAt': '2026-10-01T10:00:00Z',
  'updatedAt': '2026-10-01T10:00:00Z',
  'conversationCount': chats,
  'fileCount': files,
  'taskCount': tasks,
};

Map<String, Object?> _details() => {
  'project': _project(),
  'conversations': [
    {
      'id': 'c1',
      'title': 'Which tiles?',
      'createdAt': '2026-10-01T10:00:00Z',
      'updatedAt': '2026-10-01T10:00:00Z',
      'pinned': false,
    },
  ],
  'files': [
    {
      'id': 'f1',
      'fileName': 'quote-bosch.pdf',
      'contentType': 'application/pdf',
      'sizeBytes': 2400000,
      'sha256': 'x',
      'createdAt': '2026-10-01T10:00:00Z',
      'processingStatus': 'indexed',
    },
  ],
  'tasks': [
    {
      'id': 't1',
      'title': 'Compare the quotes',
      'prompt': 'Compare',
      'status': 'completed',
      'conversationId': 'tc1',
      'createdAt': '2026-10-01T10:00:00Z',
    },
  ],
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(_host(child));
    await tester.pumpAndSettle();
  }

  test('counts read naturally and leave out empty parts', () {
    expect(projectCountsLabel(_project()), '1 chat · 1 file · 1 task');
    expect(projectCountsLabel(_project(chats: 3, files: 0)), '3 chats · 1 task');
    expect(projectCountsLabel(_project(chats: 0, files: 0, tasks: 0)), 'Empty');
  });

  testWidgets('projects list invites a first project, then shows them', (
    tester,
  ) async {
    http.on('GET', '/api/v1/projects', <Object>[]);
    await show(tester, ProjectsScreen(http: http.client()));
    expect(find.text('Start a project'), findsOneWidget);
    expect(find.text('New project'), findsOneWidget);

    http.on('GET', '/api/v1/projects', [_project(chats: 2)]);
    await show(tester, ProjectsScreen(http: http.client(), key: UniqueKey()));
    expect(find.text('Kitchen renovation'), findsOneWidget);
    expect(find.text('2 chats · 1 file · 1 task'), findsOneWidget);
  });

  testWidgets('project page starts a chat inside the project', (tester) async {
    http
      ..on('GET', '/api/v1/projects/$_id', _details())
      ..on('POST', '/api/v1/conversations', {'id': 'new-chat'}, status: 201);
    String? opened;
    await show(
      tester,
      ProjectScreen(
        http: http.client(),
        projectId: _id,
        onOpenConversation: (id) async => opened = id,
      ),
    );

    expect(find.text('Kitchen renovation'), findsOneWidget);
    expect(find.text('Compare at least three quotes.'), findsOneWidget);
    expect(find.text('Which tiles?'), findsOneWidget);

    await tester.tap(find.text('New chat in this project'));
    await tester.pumpAndSettle();
    final sent = http.sent('POST', '/api/v1/conversations').single.body as Map;
    expect(sent['projectId'], _id);
    expect(opened, 'new-chat');
  });

  testWidgets('project tabs show files and tasks and remove an item', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/projects/$_id', _details())
      ..on('PUT', '/api/v1/files/f1/project', null, status: 204);
    await show(tester, ProjectScreen(http: http.client(), projectId: _id));

    await tester.tap(find.text('Files  1'));
    await tester.pumpAndSettle();
    expect(find.text('quote-bosch.pdf'), findsOneWidget);
    expect(find.text('2.3 MB'), findsOneWidget);

    await tester.tap(find.byTooltip('Actions for quote-bosch.pdf'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Remove from project'));
    await tester.pumpAndSettle();
    final sent = http.sent('PUT', '/api/v1/files/f1/project').single.body as Map;
    expect(sent.containsKey('projectId'), isTrue);
    expect(sent['projectId'], isNull);

    await tester.tap(find.text('Tasks  1'));
    await tester.pumpAndSettle();
    expect(find.text('Compare the quotes'), findsOneWidget);
    expect(find.text('Completed'), findsOneWidget);
  });

  testWidgets('a deleted project says so instead of failing', (tester) async {
    http.on('GET', '/api/v1/projects/$_id', null, status: 404);
    await show(tester, ProjectScreen(http: http.client(), projectId: _id));
    expect(find.text('This project is gone'), findsOneWidget);
  });

  testWidgets('editor requires a name and saves color and instructions', (
    tester,
  ) async {
    http.on('POST', '/api/v1/projects', _project(), status: 201);
    await show(tester, ProjectEditorScreen(http: http.client()));

    await tester.tap(find.text('Create'));
    await tester.pumpAndSettle();
    expect(find.text('Give the project a name.'), findsOneWidget);
    expect(http.sent('POST', '/api/v1/projects'), isEmpty);

    await tester.enterText(find.byType(TextFormField).first, 'Taxes');
    await tester.enterText(find.byType(TextFormField).last, 'Use 2026 rules.');
    await tester.tap(find.bySemanticsLabel('Teal color'));
    await tester.tap(find.text('Create'));
    await tester.pumpAndSettle();
    final sent = http.sent('POST', '/api/v1/projects').single.body as Map;
    expect(sent['name'], 'Taxes');
    expect(sent['instructions'], 'Use 2026 rules.');
    expect(sent['color'], 'teal');
  });

  testWidgets('sidebar lists projects and opens one', (tester) async {
    String? opened;
    var created = false;
    Widget sidebar(List<Map<String, dynamic>> projects) => Scaffold(
      body: JarvisSidebar(
        conversations: const [],
        selectedConversationId: null,
        homeSelected: true,
        connected: true,
        onHome: () {},
        onNewChat: () {},
        onVoice: () {},
        onConversation: (_) {},
        onSeeAll: () {},
        onUtility: (_) {},
        onSettings: () {},
        onJarvisSearch: () {},
        projects: projects,
        onProject: (id) => opened = id,
        onAllProjects: () {},
        onNewProject: () => created = true,
      ),
    );

    await show(tester, sidebar(const []));
    await tester.tap(find.text('New project'));
    expect(created, isTrue);

    await show(tester, sidebar([_project()]));
    expect(find.text('Projects'), findsOneWidget);
    await tester.tap(find.text('Kitchen renovation'));
    expect(opened, _id);
  });
}
