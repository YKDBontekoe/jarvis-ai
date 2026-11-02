import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

void main() {
  Future<void> show(
    WidgetTester tester, {
    bool loading = false,
    String? error,
    bool isEmpty = false,
    required Future<void> Function() onRetry,
  }) => tester.pumpWidget(
    MaterialApp(
      theme: buildJarvisTheme(),
      home: Scaffold(
        body: ListScreenBody(
          loading: loading,
          error: error,
          isEmpty: isEmpty,
          empty: const Text('Nothing here'),
          onRetry: onRetry,
          child: ListView(children: const [Text('Row one'), Text('Row two')]),
        ),
      ),
    ),
  );

  testWidgets('an empty list can be pulled to refresh', (tester) async {
    var loads = 0;
    await show(tester, isEmpty: true, onRetry: () async => loads++);
    expect(find.text('Nothing here'), findsOneWidget);

    await tester.fling(find.text('Nothing here'), const Offset(0, 400), 1000);
    await tester.pump();
    await tester.pump(const Duration(seconds: 1));
    await tester.pumpAndSettle();
    expect(loads, 1);
  });

  testWidgets('a failed first load can be pulled to refresh', (tester) async {
    var loads = 0;
    await show(
      tester,
      isEmpty: true,
      error: 'Offline',
      onRetry: () async => loads++,
    );

    await tester.fling(find.text('Offline'), const Offset(0, 400), 1000);
    await tester.pumpAndSettle();
    expect(loads, 1);
  });

  testWidgets('the refresh lasts as long as the request, not a fixed time', (
    tester,
  ) async {
    final request = Completer<void>();
    var finished = false;
    await show(
      tester,
      onRetry: () async {
        await request.future;
        finished = true;
      },
    );

    await tester.fling(find.text('Row one'), const Offset(0, 400), 1000);
    await tester.pump();
    // Far longer than the old fixed delay: still waiting for the request.
    await tester.pump(const Duration(seconds: 5));
    expect(finished, isFalse);
    expect(
      tester
          .state<RefreshIndicatorState>(find.byType(RefreshIndicator))
          .mounted,
      isTrue,
    );

    request.complete();
    await tester.pumpAndSettle();
    expect(finished, isTrue);
  });

  testWidgets('rows stay visible with a retry notice when a refresh fails', (
    tester,
  ) async {
    var loads = 0;
    await show(
      tester,
      error: 'Could not refresh',
      onRetry: () async => loads++,
    );

    expect(find.text('Row one'), findsOneWidget);
    expect(find.text('Could not refresh'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    await tester.pump();
    expect(loads, 1);
    expect(find.text('Row two'), findsOneWidget);
  });
}
