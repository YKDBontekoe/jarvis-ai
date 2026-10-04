// Run the actual app with local sample data, without a backend or account.
// flutter run -d <device> -t tool/ui_preview.dart
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/appearance.dart';
import 'package:jarvis_mobile/main.dart';

import '../test/screenshots/fixture_adapter.dart';
import '../test/screenshots/fixtures.dart';

Future<void> main() async {
  if (kReleaseMode) {
    throw StateError('UI preview is for local development only');
  }
  WidgetsFlutterBinding.ensureInitialized();
  // This development entry point intentionally uses the fixture hook.
  // ignore: invalid_use_of_visible_for_testing_member
  debugJarvisHttpAdapter = ScreenshotHttp(fixtureRoutes(withApproval: false));
  final appearance = AppearanceController(store: MemoryAppearanceStore('dark'));
  await appearance.load();
  runApp(JarvisApp(skipAuthentication: true, appearance: appearance));
}
