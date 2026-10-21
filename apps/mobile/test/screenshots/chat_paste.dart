import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/main.dart';
import 'package:jarvis_mobile/theme.dart';

import 'fixtures.dart';
import 'harness.dart';

void main() {
  late ScreenshotHttp http;

  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    http = ScreenshotHttp(fixtureRoutes(withApproval: false));
    debugJarvisHttpAdapter = http;
  });
  tearDown(() {
    debugJarvisHttpAdapter = null;
  });

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 30; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  screenshotTest('paste photo sheet', (tester) async {
    usePhone(tester);
    await tester.pumpWidget(
      RepaintBoundary(
        key: screenshotKey,
        child: const JarvisApp(skipAuthentication: true),
      ),
    );
    await settle(tester);
    await tester.tap(find.byKey(const Key('tab-jarvis')));
    await settle(tester);
    expect(find.byTooltip('Add a photo'), findsOneWidget);
    await tester.tap(find.byTooltip('Add a photo'));
    await settle(tester);
    expect(find.text('Paste a photo'), findsOneWidget);
    await capture(tester, 'chat-paste-sheet');
  });

  screenshotTest('composer with pasted photo', (tester) async {
    usePhone(tester, size: const Size(393, 360));
    final bytes = File('test/screenshots/avatars/piet.png').readAsBytesSync();
    final controller = TextEditingController(text: 'What plant is this?');
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      RepaintBoundary(
        key: screenshotKey,
        child: MaterialApp(
          debugShowCheckedModeBanner: false,
          theme: buildJarvisTheme(),
          home: Scaffold(
            backgroundColor: JarvisColors.light.canvas,
            body: SafeArea(
                child: Padding(
                padding: const EdgeInsets.fromLTRB(14, 16, 14, 14),
                child: Column(
                  children: [
                    const Spacer(),
                    ChatComposer(
                      controller: controller,
                      onSend: () {},
                      onVoice: null,
                      sending: false,
                      voiceActive: false,
                      voiceStarting: false,
                      onPhoto: () {},
                      onImages: (_) {},
                      photos: [
                        PendingPhoto(
                          localId: 'pasted',
                          bytes: bytes,
                          fileName: 'photo.png',
                          fileId: 'f1',
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
    await _waitForMemoryImages(tester);
    await capture(tester, 'chat-paste-composer');
  });
}

Future<void> _waitForMemoryImages(WidgetTester tester) async {
  await tester.runAsync(() async {
    await Future.wait([
      for (final element in find.byType(Image).evaluate())
        _waitForImage((element.widget as Image).image, element),
    ]);
  });
  await tester.pump();
}

Future<void> _waitForImage(ImageProvider provider, BuildContext context) async {
  final stream = provider.resolve(createLocalImageConfiguration(context));
  final ready = Completer<void>();
  late final ImageStreamListener listener;
  listener = ImageStreamListener(
    (_, _) {
      if (!ready.isCompleted) ready.complete();
    },
    onError: (Object error, StackTrace? trace) {
      if (!ready.isCompleted) ready.completeError(error, trace);
    },
  );
  stream.addListener(listener);
  try {
    await ready.future;
  } finally {
    stream.removeListener(listener);
  }
}
