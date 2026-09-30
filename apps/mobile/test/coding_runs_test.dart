import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/coding/coding_run_detail_screen.dart';
import 'package:jarvis_mobile/features/coding/coding_runs_screen.dart';

import 'support/fixture_http.dart';

void main() {
  testWidgets('coding runs list a completed worktree and open the diff', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final http = FixtureHttp();
    http.on('GET', '/api/v1/coding/runs', [
      {
        'id': '11111111-1111-1111-1111-111111111111',
        'repository': 'jarvis',
        'task': 'Add a health check',
        'status': 'completed',
        'changedFiles': ['src/Jarvis.Api/Program.cs'],
      },
    ]);
    http.on('GET', '/api/v1/coding/runs/11111111-1111-1111-1111-111111111111', {
      'id': '11111111-1111-1111-1111-111111111111',
      'repository': 'jarvis',
      'task': 'Add a health check',
      'status': 'completed',
      'worktreePath': '/tmp/worktree',
      'changedFiles': ['src/Jarvis.Api/Program.cs'],
      'diffSummary': '@@ -1,0 +1,1 @@\n+ok',
      'summary': 'Added a health endpoint.',
      'exitCode': 0,
    });

    await tester.pumpWidget(
      MaterialApp(home: CodingRunsScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
    expect(find.text('Add a health check'), findsOneWidget);
    await tester.tap(find.text('Add a health check'));
    await tester.pumpAndSettle();
    expect(find.text('Added a health endpoint.'), findsOneWidget);
    expect(find.textContaining('Program.cs'), findsOneWidget);
  });

  const runId = '22222222-2222-2222-2222-222222222222';
  const patch = 'diff --git a/src/A.cs b/src/A.cs\n'
      'index 1..2 100644\n--- a/src/A.cs\n+++ b/src/A.cs\n'
      '@@ -1,2 +1,2 @@\n keep\n-old line\n+new line\n';

  Map<String, Object?> run({Object? number, String? state}) => {
    'id': runId,
    'repository': 'jarvis',
    'task': 'Fix the greeting',
    'status': 'completed',
    'summary': 'Changed the greeting.',
    'changedFiles': ['src/A.cs'],
    'pullRequestNumber': number,
    'pullRequestState': state,
  };

  Future<void> openDetail(WidgetTester tester, FixtureHttp http) async {
    tester.view.physicalSize = const Size(900, 2200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: CodingRunDetailScreen(http: http.client(), runId: runId),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('a finished run shows its diff and opens a pull request', (
    tester,
  ) async {
    final http = FixtureHttp();
    http.on('GET', '/api/v1/coding/runs/$runId', run());
    http.on('GET', '/api/v1/coding/runs/$runId/diff', {
      'patch': patch,
      'files': [
        {'path': 'src/A.cs', 'status': 'modified', 'protected': false},
      ],
      'truncated': false,
      'warnings': <String>[],
    });
    http.on('POST', '/api/v1/coding/runs/$runId/pull-request', {
      'number': 7,
      'url': 'https://github.com/o/r/pull/7',
      'title': 'Fix the greeting',
      'state': 'open',
      'checksState': 'none',
      'checks': <Object>[],
    });
    await openDetail(tester, http);

    expect(find.text('+new line'), findsOneWidget);
    expect(find.text('-old line'), findsOneWidget);
    await tester.tap(find.text('Open pull request'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/coding/runs/$runId/pull-request'), hasLength(1));
    expect(find.text('Pull request #7'), findsOneWidget);
    expect(find.text('Approve & merge'), findsOneWidget);
  });

  testWidgets('merging asks first and is blocked while checks fail', (
    tester,
  ) async {
    final http = FixtureHttp();
    http.on('GET', '/api/v1/coding/runs/$runId', run(number: 7, state: 'open'));
    http.on('GET', '/api/v1/coding/runs/$runId/diff', {
      'patch': patch,
      'files': [
        {'path': 'src/A.cs', 'status': 'modified', 'protected': true},
      ],
      'truncated': false,
      'warnings': ['This change touches security-sensitive code.'],
    });
    http.on('GET', '/api/v1/coding/runs/$runId/pull-request', {
      'number': 7,
      'url': 'https://github.com/o/r/pull/7',
      'title': 'Fix the greeting',
      'state': 'open',
      'checksState': 'failure',
      'checks': [
        {'name': 'build', 'status': 'completed', 'conclusion': 'failure'},
      ],
    });
    await openDetail(tester, http);

    expect(find.text('This change touches security-sensitive code.'), findsOneWidget);
    expect(find.text('Merging is blocked while checks fail.'), findsOneWidget);
    await tester.tap(find.text('Approve & merge'), warnIfMissed: false);
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/coding/runs/$runId/pull-request/merge'), isEmpty);
  });

  testWidgets('a passing pull request merges only after confirmation', (
    tester,
  ) async {
    final http = FixtureHttp();
    http.on('GET', '/api/v1/coding/runs/$runId', run(number: 7, state: 'open'));
    http.on('GET', '/api/v1/coding/runs/$runId/diff', {
      'patch': patch,
      'files': [
        {'path': 'src/A.cs', 'status': 'modified', 'protected': false},
      ],
      'truncated': false,
      'warnings': <String>[],
    });
    const open = {
      'number': 7,
      'url': 'https://github.com/o/r/pull/7',
      'title': 'Fix the greeting',
      'state': 'open',
      'checksState': 'success',
      'checks': <Object>[],
    };
    http.on('GET', '/api/v1/coding/runs/$runId/pull-request', open);
    http.on('POST', '/api/v1/coding/runs/$runId/pull-request/merge', {
      ...open,
      'state': 'merged',
      'merged': true,
    });
    await openDetail(tester, http);

    await tester.tap(find.text('Approve & merge'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/coding/runs/$runId/pull-request/merge'), isEmpty);
    await tester.tap(find.text('Merge'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/coding/runs/$runId/pull-request/merge'), hasLength(1));
    expect(find.text('Merged'), findsOneWidget);
  });
}
