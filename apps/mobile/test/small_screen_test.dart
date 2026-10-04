import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/remote_query.dart';
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

  for (final scale in [1.0, 2.0]) {
    for (final dark in [false, true]) {
      testWidgets(
        'chat shell fits a 360x640 phone (text ${(scale * 100).round()}%, '
        '${dark ? 'dark' : 'light'})',
        (tester) async {
          tester.view.physicalSize = const Size(360, 640);
          tester.view.devicePixelRatio = 1;
          tester.platformDispatcher.textScaleFactorTestValue = scale;
          tester.platformDispatcher.platformBrightnessTestValue = dark
              ? Brightness.dark
              : Brightness.light;
          addTearDown(tester.view.resetPhysicalSize);
          addTearDown(tester.view.resetDevicePixelRatio);
          addTearDown(tester.platformDispatcher.clearAllTestValues);

          await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
          await tester.pumpAndSettle();

          expect(tester.takeException(), isNull);
          expect(find.byKey(const Key('home-clock')), findsOneWidget);
          expect(find.byKey(const Key('tab-jarvis')), findsOneWidget);
        },
      );
    }
  }

  test('transcript follows a stream only while near the bottom', () {
    expect(isNearTranscriptBottom(980, 1000), isTrue);
    expect(isNearTranscriptBottom(1000, 1000), isTrue);
    expect(isNearTranscriptBottom(400, 1000), isFalse);
  });

  test('pinned question card leaves room on small screens', () {
    expect(pinnedSurfaceMaxHeight(300), 120);
    expect(pinnedSurfaceMaxHeight(600), 210);
    expect(pinnedSurfaceMaxHeight(2000), 360);
  });
}
