// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:jarvis_mobile/main.dart';

void main() {
  const secureStorage = MethodChannel(
    'plugins.it_nomads.com/flutter_secure_storage',
  );

  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(secureStorage, (call) async => null);
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(secureStorage, null);
  });

  testWidgets('Jarvis chat shell renders', (WidgetTester tester) async {
    await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
    await tester.pumpAndSettle();
    expect(find.text('Jarvis'), findsOneWidget);
    expect(find.text('What do you need?'), findsOneWidget);
  });

  testWidgets('account sign-in is shown before a session exists', (
    tester,
  ) async {
    await tester.pumpWidget(const JarvisApp());
    await tester.pumpAndSettle();
    expect(find.text('Sign in to Jarvis'), findsOneWidget);
    expect(find.text('Email'), findsOneWidget);
    expect(find.text('Password'), findsOneWidget);
    expect(find.text('Sign in'), findsOneWidget);

    await tester.ensureVisible(find.text('Need an account? Create one'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Need an account? Create one'));
    await tester.pumpAndSettle();
    expect(find.text('Create your account'), findsOneWidget);
    expect(find.text('Create account'), findsOneWidget);
  });

  testWidgets('on a wide screen tasks open beside the sidebar', (tester) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
    await tester.pumpAndSettle();
    expect(find.text('Recents'), findsOneWidget);

    await tester.tap(find.text('Tasks').first);
    await tester.pumpAndSettle();
    expect(find.text('Recents'), findsOneWidget, reason: 'sidebar stays visible');
    expect(find.text('New task'), findsOneWidget);

    await tester.tap(find.text('Memory').first);
    await tester.pumpAndSettle();
    expect(find.text('New task'), findsNothing);
    expect(find.text('Recents'), findsOneWidget);

    await tester.tap(find.text('Jarvis').first);
    await tester.pumpAndSettle();
    expect(find.text('Ask Jarvis anything'), findsOneWidget);
  });
}
