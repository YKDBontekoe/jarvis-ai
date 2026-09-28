import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';

const _firebaseApiKey = String.fromEnvironment('JARVIS_FIREBASE_API_KEY');
const _firebaseProjectId = String.fromEnvironment('JARVIS_FIREBASE_PROJECT_ID');
const _firebaseSenderId = String.fromEnvironment('JARVIS_FIREBASE_SENDER_ID');
const _firebaseAndroidAppId = String.fromEnvironment(
  'JARVIS_FIREBASE_ANDROID_APP_ID',
);
const _firebaseIosAppId = String.fromEnvironment('JARVIS_FIREBASE_IOS_APP_ID');
const _firebaseIosBundleId = String.fromEnvironment(
  'JARVIS_FIREBASE_IOS_BUNDLE_ID',
  defaultValue: 'com.example.jarvis_mobile',
);
const _firebaseStorageBucket = String.fromEnvironment(
  'JARVIS_FIREBASE_STORAGE_BUCKET',
);

@pragma('vm:entry-point')
Future<void> firebaseBackgroundHandler(RemoteMessage message) async {
  if (Firebase.apps.isEmpty) return;
}

Future<void> initializeFirebase() async {
  if (_firebaseApiKey.isEmpty ||
      _firebaseProjectId.isEmpty ||
      _firebaseSenderId.isEmpty) {
    return;
  }
  final appId = switch (defaultTargetPlatform) {
    TargetPlatform.android => _firebaseAndroidAppId,
    TargetPlatform.iOS => _firebaseIosAppId,
    _ => '',
  };
  if (appId.isEmpty) return;
  FirebaseMessaging.onBackgroundMessage(firebaseBackgroundHandler);
  await Firebase.initializeApp(
    options: FirebaseOptions(
      apiKey: _firebaseApiKey,
      appId: appId,
      messagingSenderId: _firebaseSenderId,
      projectId: _firebaseProjectId,
      storageBucket: _firebaseStorageBucket.isEmpty
          ? null
          : _firebaseStorageBucket,
      iosBundleId: defaultTargetPlatform == TargetPlatform.iOS
          ? _firebaseIosBundleId
          : null,
    ),
  );
}
