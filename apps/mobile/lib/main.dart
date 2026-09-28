import 'package:flutter/material.dart';

import 'features/chat/chat_screen.dart';
import 'push/firebase_bootstrap.dart';
import 'theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeFirebase();
  runApp(const JarvisApp());
}

class JarvisApp extends StatelessWidget {
  const JarvisApp({this.skipAuthentication = false, super.key});

  /// Widget tests render the assistant shell without an account session.
  final bool skipAuthentication;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Jarvis',
    debugShowCheckedModeBanner: false,
    theme: buildJarvisTheme(),
    home: ChatScreen(skipAuthentication: skipAuthentication),
  );
}
