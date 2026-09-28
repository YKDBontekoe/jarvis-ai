import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
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
    expect(find.text('/tmp/worktree'), findsOneWidget);
    expect(find.text('Added a health endpoint.'), findsOneWidget);
    expect(find.textContaining('Program.cs'), findsOneWidget);
  });
}
