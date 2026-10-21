import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/command_palette.dart';
import 'package:jarvis_mobile/features/search/context_commands.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'harness.dart';

class _Subject extends StatefulWidget {
  const _Subject();

  @override
  State<_Subject> createState() => _SubjectState();
}

class _SubjectState extends State<_Subject> with PaletteContextMixin<_Subject> {
  @override
  String get paletteContextValue => 'project:p2';

  @override
  Widget build(BuildContext context) => const Scaffold(body: SizedBox());
}

/// The command palette opened on a project, on a phone and at the largest text
/// size. Run: flutter test test/screenshots/palette_context.dart
void main() {
  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    SharedPreferences.setMockInitialValues({});
  });

  for (final (name, size, scale) in [
    ('phone', const Size(393, 852), 1.0),
    ('large-text', const Size(320, 640), 3.0),
  ]) {
    screenshotTest('palette on a project on $name', (tester) async {
      usePhone(tester, size: size);
      final dio = Dio(BaseOptions(baseUrl: 'https://fixture.invalid'));
      await tester.pumpWidget(
        RepaintBoundary(
          key: screenshotKey,
          child: MaterialApp(
            debugShowCheckedModeBanner: false,
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(scale)),
              child: child!,
            ),
            theme: buildJarvisTheme(),
            home: Builder(
              builder: (context) => Scaffold(
                body: const _Subject(),
                floatingActionButton: FloatingActionButton(
                  key: const Key('go'),
                  onPressed: () => showJarvisCommandPalette(
                    context,
                    http: dio,
                    onConversation: (_) async {},
                    onUtility: (_) {},
                  ),
                ),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('go')));
      for (var i = 0; i < 10; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await capture(tester, 'palette-context-$name');
    });
  }
}
