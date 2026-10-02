import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Answers by "METHOD /path" (exact, then by longest matching prefix) and
/// logs everything it could not answer so fixtures are easy to fill in.
class ScreenshotHttp implements HttpClientAdapter {
  ScreenshotHttp(this.routes);

  final Map<String, Object?> routes;
  final Set<String> missing = {};

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final key = '${options.method} ${options.path}';
    Object? body;
    var found = false;
    if (routes.containsKey(key)) {
      body = routes[key];
      found = true;
    } else {
      final prefixes = routes.keys.where(
        (route) =>
            route.endsWith('*') &&
            key.startsWith(route.substring(0, route.length - 1)),
      );
      if (prefixes.isNotEmpty) {
        final best = prefixes.reduce((a, b) => a.length >= b.length ? a : b);
        body = routes[best];
        found = true;
      }
    }
    if (!found) missing.add(key);
    return ResponseBody.fromString(
      jsonEncode(
        body ?? (options.method == 'GET' ? <Object>[] : <String, Object>{}),
      ),
      found ? 200 : (options.method == 'GET' ? 404 : 200),
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

Future<void> loadAppFonts() async {
  final fonts = <String, List<String>>{
    'Inter': [
      'Inter-Regular.ttf',
      'Inter-Medium.ttf',
      'Inter-SemiBold.ttf',
      'Inter-Bold.ttf',
    ],
    'InstrumentSerif': ['InstrumentSerif-Regular.ttf'],
    'PhosphorRegular': ['Phosphor-Regular.ttf'],
    'PhosphorFill': ['Phosphor-Fill.ttf'],
    'PhosphorBold': ['Phosphor-Bold.ttf'],
  };
  final emojiFile = File('/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf');
  for (final MapEntry(key: family, value: files) in fonts.entries) {
    final loader = FontLoader(family);
    for (final file in files) {
      final bytes = File('assets/fonts/$file').readAsBytesSync();
      loader.addFont(Future.value(ByteData.view(bytes.buffer)));
    }
    if (family == 'Inter' && emojiFile.existsSync()) {
      loader.addFont(
        Future.value(ByteData.view(emojiFile.readAsBytesSync().buffer)),
      );
    }
    await loader.load();
  }
  final emoji = File('/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf');
  if (emoji.existsSync()) {
    final loader = FontLoader('Noto Color Emoji')
      ..addFont(Future.value(ByteData.view(emoji.readAsBytesSync().buffer)));
    await loader.load();
  }
  final mono = File('/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf');
  if (mono.existsSync()) {
    final loader = FontLoader('monospace')
      ..addFont(Future.value(ByteData.view(mono.readAsBytesSync().buffer)));
    await loader.load();
  }
  final material = Platform.environment['FLUTTER_ROOT'] ?? '/root/flutter';
  final icons = File(
    '$material/bin/cache/artifacts/material_fonts/MaterialIcons-Regular.otf',
  );
  if (icons.existsSync()) {
    final loader = FontLoader('MaterialIcons')
      ..addFont(Future.value(ByteData.view(icons.readAsBytesSync().buffer)));
    await loader.load();
  }
  // Roboto fallback for Material widgets that do not set a family.
  final roboto = FontLoader('Roboto');
  roboto.addFont(
    Future.value(
      ByteData.view(
        File('assets/fonts/Inter-Regular.ttf').readAsBytesSync().buffer,
      ),
    ),
  );
  await roboto.load();
}

final screenshotKey = GlobalKey();

/// Phone-sized viewport (logical 393x852, like an iPhone 15).
void usePhone(WidgetTester tester, {Size size = const Size(393, 852)}) {
  tester.view.physicalSize = size * 3;
  tester.view.devicePixelRatio = 3;
  tester.view.padding = const FakeViewPadding(top: 59 * 3, bottom: 34 * 3);
  tester.view.viewPadding = const FakeViewPadding(top: 59 * 3, bottom: 34 * 3);
  addTearDown(tester.view.reset);
}

void useDesktop(WidgetTester tester, {Size size = const Size(1440, 900)}) {
  tester.view.physicalSize = size * 2;
  tester.view.devicePixelRatio = 2;
  addTearDown(tester.view.reset);
}

Future<void> capture(WidgetTester tester, String name) async {
  final dir = Directory(
    Platform.environment['SCREENSHOT_DIR'] ?? 'build/screenshots',
  )..createSync(recursive: true);
  await tester.runAsync(() async {
    final boundary =
        screenshotKey.currentContext!.findRenderObject()!
            as RenderRepaintBoundary;
    final image = await boundary.toImage(
      pixelRatio: tester.view.devicePixelRatio,
    );
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    File('${dir.path}/$name.png').writeAsBytesSync(data!.buffer.asUint8List());
  });
}

void mockPlatformChannels() {
  SharedPreferences.setMockInitialValues({});
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
  messenger.setMockMethodCallHandler(
    const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
    (call) async => null,
  );
}

/// A widget test that paints real (blurred) shadows, as devices do; flutter_test
/// otherwise draws them hard-edged.
void screenshotTest(
  String description,
  Future<void> Function(WidgetTester) body,
) {
  testWidgets(description, (tester) async {
    debugDisableShadows = false;
    try {
      await body(tester);
      // Let refresh timers on the captured screens run out.
      await tester.pumpWidget(const SizedBox());
      await tester.pump(const Duration(minutes: 5));
    } finally {
      debugDisableShadows = true;
    }
  });
}
