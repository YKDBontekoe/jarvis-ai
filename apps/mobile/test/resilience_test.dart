import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/error_reporting.dart';
import 'package:jarvis_mobile/main.dart';
import 'package:sentry_flutter/sentry_flutter.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

void main() {
  const secureStorage = MethodChannel(
    'plugins.it_nomads.com/flutter_secure_storage',
  );

  tearDown(() {
    errorSink = null;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(secureStorage, null);
  });

  test('reportError reaches the sink and survives a failing sink', () {
    sentryTracingEnabled = false;
    Object? seen;
    errorSink = (error, stack, context) =>
        seen = '${error.runtimeType}/$context';
    reportError(StateError('boom'), StackTrace.current, context: 'test');
    expect(seen, 'StateError/test');

    errorSink = (_, _, _) => throw StateError('sink broke');
    expect(() => reportError(StateError('x'), null), returnsNormally);
  });

  test('sentry logs below warning are dropped', () {
    SentryLog log(SentryLogLevel level) => SentryLog(
      timestamp: DateTime.utc(2026),
      level: level,
      body: 'status',
      attributes: const {},
    );

    expect(filterSentryLog(log(SentryLogLevel.info)), isNull);
    final warning = log(SentryLogLevel.warn);
    expect(filterSentryLog(warning), same(warning));
  });

  testWidgets('a storage failure at startup keeps the user signed in', (
    tester,
  ) async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(secureStorage, (call) async {
          throw PlatformException(code: 'keystore', message: 'unavailable');
        });
    await tester.pumpWidget(const JarvisApp());
    await tester.pumpAndSettle();

    expect(find.text('Sign in to Jarvis'), findsNothing);
    expect(find.textContaining('Could not connect to Jarvis'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);
  });

  testWidgets('short lists can be pulled to refresh', (tester) async {
    var refreshed = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ListScreenBody(
            loading: false,
            error: null,
            isEmpty: false,
            empty: const SizedBox(),
            onRetry: () {},
            onRefresh: () async => refreshed++,
            child: ListView(children: const [ListTile(title: Text('one'))]),
          ),
        ),
      ),
    );

    await tester.fling(find.text('one'), const Offset(0, 300), 1000);
    await tester.pumpAndSettle();
    expect(refreshed, 1);
  });
}
