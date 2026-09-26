import 'dart:async';
import 'package:flutter/foundation.dart';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'oidc_web_stub.dart'
    if (dart.library.js_interop) 'oidc_web.dart'
    as web_oidc;
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:signalr_netcore/signalr_client.dart';
import 'package:livekit_client/livekit_client.dart';

import 'memory_screen.dart';
import 'approvals_screen.dart';
import 'reminders_screen.dart';
import 'files_screen.dart';
import 'tasks_screen.dart';
import 'audit_screen.dart';
import 'conversations_screen.dart';
import 'notification_details_screen.dart';
import 'condition_watches_screen.dart';
import 'daily_briefing_screen.dart';
import 'integrations_screen.dart';
import 'features/home/home_overview.dart';

const _apiBaseUrl = String.fromEnvironment(
  'JARVIS_API_URL',
  defaultValue: 'http://localhost:5082',
);
const _oidcIssuer = String.fromEnvironment('JARVIS_OIDC_ISSUER');
const _oidcClientId = String.fromEnvironment('JARVIS_OIDC_CLIENT_ID');
const _oidcRedirectUri = String.fromEnvironment(
  'JARVIS_OIDC_REDIRECT_URI',
  defaultValue: 'com.example.jarvis_mobile:/oauth2redirect',
);
const _webOidcRedirectUri = String.fromEnvironment(
  'JARVIS_WEB_OIDC_REDIRECT_URI',
);
const _oidcScopes = ['openid', 'profile', 'offline_access', 'jarvis-api'];
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
Future<void> _firebaseBackgroundHandler(RemoteMessage message) async {
  if (Firebase.apps.isEmpty) return;
}

Future<void> _initializeFirebase() async {
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
  FirebaseMessaging.onBackgroundMessage(_firebaseBackgroundHandler);
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

class _AuthSession {
  _AuthSession()
    : enabled =
          _oidcIssuer.isNotEmpty ||
          _oidcClientId.isNotEmpty ||
          (kIsWeb && kReleaseMode);

  final bool enabled;
  final _appAuth = const FlutterAppAuth();
  final _storage = const FlutterSecureStorage();

  String get _redirectUri => kIsWeb
      ? web_oidc.currentRedirectUri(_webOidcRedirectUri)
      : _oidcRedirectUri;

  void _ensureConfigured() {
    if (_oidcIssuer.isEmpty || _oidcClientId.isEmpty) {
      throw StateError(
        'Set both JARVIS_OIDC_ISSUER and JARVIS_OIDC_CLIENT_ID.',
      );
    }
  }

  Future<String?> accessToken() async {
    if (!enabled) return null;
    _ensureConfigured();
    if (kIsWeb) {
      final response = await web_oidc.completeAuthorizationCode(
        _oidcIssuer,
        _oidcClientId,
        _redirectUri,
        _oidcScopes,
      );
      if (response != null) await _saveWebResponse(response, null);
    }
    final expiration = int.tryParse(
      await _storage.read(key: 'token_expiration') ?? '',
    );
    final accessToken = await _storage.read(key: 'access_token');
    if (accessToken != null &&
        expiration != null &&
        expiration > DateTime.now().millisecondsSinceEpoch + 30000) {
      return accessToken;
    }

    final refreshToken = await _storage.read(key: 'refresh_token');
    if (refreshToken == null) return null;
    if (kIsWeb) {
      final response = await web_oidc.refreshAuthorizationTokens(
        _oidcIssuer,
        _oidcClientId,
        refreshToken,
      );
      await _saveWebResponse(response, refreshToken);
      return response['access_token'] as String;
    }
    final response = await _appAuth.token(
      TokenRequest(
        _oidcClientId,
        _redirectUri,
        issuer: _oidcIssuer,
        refreshToken: refreshToken,
        scopes: _oidcScopes,
      ),
    );
    await _save(
      response.accessToken,
      response.refreshToken ?? refreshToken,
      response.accessTokenExpirationDateTime,
    );
    return response.accessToken;
  }

  Future<void> signIn() async {
    if (kIsWeb) {
      _ensureConfigured();
      await web_oidc.beginAuthorizationCode(
        _oidcIssuer,
        _oidcClientId,
        _redirectUri,
        _oidcScopes,
      );
      return;
    }
    _ensureConfigured();
    final response = await _appAuth.authorizeAndExchangeCode(
      AuthorizationTokenRequest(
        _oidcClientId,
        _redirectUri,
        issuer: _oidcIssuer,
        scopes: _oidcScopes,
      ),
    );
    await _save(
      response.accessToken,
      response.refreshToken,
      response.accessTokenExpirationDateTime,
    );
  }

  Future<void> signOut() => _storage.deleteAll();

  Future<void> _saveWebResponse(
    Map<String, dynamic> response,
    String? previousRefreshToken,
  ) async {
    final expiresIn = response['expires_in'];
    if (expiresIn is! num || expiresIn <= 0) {
      throw StateError(
        'The identity provider returned an invalid token lifetime.',
      );
    }
    final refreshToken =
        response['refresh_token'] as String? ?? previousRefreshToken;
    await _save(
      response['access_token'] as String?,
      refreshToken,
      DateTime.now().add(Duration(seconds: expiresIn.toInt())),
    );
  }

  Future<void> _save(
    String? accessToken,
    String? refreshToken,
    DateTime? expiresAt,
  ) async {
    if (accessToken == null || expiresAt == null) {
      throw StateError(
        'The identity provider returned an incomplete token response.',
      );
    }
    await _storage.write(key: 'access_token', value: accessToken);
    await _storage.write(
      key: 'token_expiration',
      value: expiresAt.millisecondsSinceEpoch.toString(),
    );
    if (refreshToken != null) {
      await _storage.write(key: 'refresh_token', value: refreshToken);
    }
  }
}

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await _initializeFirebase();
  runApp(const JarvisApp());
}

class JarvisApp extends StatelessWidget {
  const JarvisApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Jarvis',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(
      brightness: Brightness.dark,
      scaffoldBackgroundColor: const Color(0xff101117),
      colorScheme: ColorScheme.fromSeed(
        seedColor: const Color(0xffa895ff),
        brightness: Brightness.dark,
      ),
      useMaterial3: true,
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: const Color(0xff1c1e28),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(26),
          borderSide: BorderSide.none,
        ),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 20,
          vertical: 15,
        ),
      ),
    ),
    home: const ChatScreen(),
  );
}

class _Message {
  const _Message({
    required this.role,
    required this.content,
    this.pending = false,
  });
  final String role;
  final String content;
  final bool pending;
}

class ChatScreen extends StatefulWidget {
  const ChatScreen({super.key});

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  final _auth = _AuthSession();
  final _http = Dio(
    BaseOptions(
      baseUrl: _apiBaseUrl,
      connectTimeout: const Duration(seconds: 10),
      receiveTimeout: const Duration(minutes: 3),
    ),
  );
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final _messages = <_Message>[];
  HubConnection? _hub;
  Room? _voiceRoom;
  String? _conversationId;
  String? _error;
  bool _connected = false;
  bool _sending = false;
  bool _signedOut = false;
  bool _authBusy = false;
  bool _voiceActive = false;
  bool _voiceStarting = false;
  int _selectedDestination = 0;
  int _homeRevision = 0;
  bool _showHome = true;
  String? _agentActivity;
  bool _agentActivityBusy = false;
  String? _pushToken;
  StreamSubscription<String>? _pushTokenSubscription;
  StreamSubscription<RemoteMessage>? _pushOpenedSubscription;
  StreamSubscription<RemoteMessage>? _pushForegroundSubscription;
  final Set<String> _shownPushNotifications = {};

  @override
  void initState() {
    super.initState();
    if (Firebase.apps.isNotEmpty) {
      _pushOpenedSubscription = FirebaseMessaging.onMessageOpenedApp.listen(
        (message) => _handlePushPayload(message.data),
      );
      _pushForegroundSubscription = FirebaseMessaging.onMessage.listen(
        _onForegroundPush,
      );
    }
    _http.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          try {
            final token = await _auth.accessToken();
            if (token != null) {
              options.headers['Authorization'] = 'Bearer $token';
            }
            handler.next(options);
          } catch (error) {
            handler.reject(DioException(requestOptions: options, error: error));
          }
        },
      ),
    );
    unawaited(_initialize());
  }

  Future<void> _initialize() async {
    try {
      if (_auth.enabled && await _auth.accessToken() == null) {
        if (mounted) setState(() => _signedOut = true);
        return;
      }
      await _enablePush();
      final list = await _http.get<List<dynamic>>('/api/v1/conversations');
      final items = list.data ?? [];
      late final String conversationId;
      if (items.isNotEmpty) {
        conversationId = (items.first as Map<String, dynamic>)['id'] as String;
      } else {
        final created = await _http.post<Map<String, dynamic>>(
          '/api/v1/conversations',
          data: const {'title': 'Chat with Jarvis'},
        );
        conversationId = created.data?['id'] as String;
      }
      await _openConversation(conversationId, showHome: true);
      if (Firebase.apps.isNotEmpty) {
        final initialPush = await FirebaseMessaging.instance
            .getInitialMessage();
        if (initialPush != null) _handlePushPayload(initialPush.data);
      }
      if (mounted) setState(() => _error = null);
    } on DioException catch (error) {
      if (mounted) {
        setState(() {
          _error = _describeError(error);
          if (_auth.enabled) _signedOut = true;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(() {
          _error = 'Could not connect to Jarvis: $error';
          if (_auth.enabled) _signedOut = true;
        });
      }
    }
  }

  Future<void> _openConversation(
    String conversationId, {
    bool showHome = false,
  }) async {
    await _stopVoice();
    await _hub?.stop();
    _hub = null;
    if (mounted) {
      setState(() {
        _conversationId = conversationId;
        _connected = false;
        _selectedDestination = 0;
        _showHome = showHome;
        _messages.clear();
        _error = null;
        _agentActivity = null;
        _agentActivityBusy = false;
      });
    }
    final details = await _http.get<Map<String, dynamic>>(
      '/api/v1/conversations/$conversationId',
    );
    final records = details.data?['messages'] as List<dynamic>? ?? [];
    if (mounted) {
      setState(() {
        _messages.addAll(
          records.map((item) {
            final message = item as Map<String, dynamic>;
            return _Message(
              role: message['role'] as String,
              content: message['content'] as String,
            );
          }),
        );
      });
    }
    await _connectRealtime();
  }

  Future<void> _chooseConversation() async {
    final selection = await Navigator.of(context)
        .push<ConversationPickerResult>(
          MaterialPageRoute<ConversationPickerResult>(
            builder: (_) => ConversationsScreen(
              http: _http,
              selectedConversationId: _conversationId,
            ),
          ),
        );
    if (selection?.deletedCurrent == true) {
      await _createAndOpenConversation();
    } else if (selection?.conversationId != null &&
        selection!.conversationId != _conversationId) {
      try {
        await _openConversation(selection.conversationId!);
      } on DioException catch (error) {
        if (mounted) setState(() => _error = _describeError(error));
      }
    }
  }

  Future<void> _createAndOpenConversation() async {
    try {
      final response = await _http.post<Map<String, dynamic>>(
        '/api/v1/conversations',
        data: const {'title': 'New conversation'},
      );
      final id = response.data?['id'] as String?;
      if (id == null) throw const FormatException('Missing conversation ID.');
      await _openConversation(id);
    } on DioException catch (error) {
      if (mounted) setState(() => _error = _describeError(error));
    } on FormatException {
      if (mounted) {
        setState(() => _error = 'Jarvis returned an invalid conversation.');
      }
    }
  }

  void _openUtility(String destination) {
    final page = switch (destination) {
      'tasks' => TasksScreen(http: _http),
      'memory' => MemoryScreen(http: _http),
      'approvals' => ApprovalsScreen(http: _http),
      'reminders' => RemindersScreen(http: _http),
      'files' => FilesScreen(http: _http),
      'audit' => AuditScreen(http: _http),
      'watches' => ConditionWatchesScreen(http: _http),
      'briefing' => DailyBriefingScreen(http: _http),
      'integrations' => IntegrationsScreen(http: _http),
      _ => null,
    };
    if (destination == 'sign_out') {
      unawaited(_signOut());
    } else if (page != null) {
      unawaited(
        Navigator.of(
          context,
        ).push<void>(MaterialPageRoute<void>(builder: (_) => page)),
      );
    }
  }

  void _selectDestination(int index) {
    if (_voiceStarting) return;
    if (index == 2) {
      setState(() => _selectedDestination = 2);
      unawaited(_toggleVoice());
      return;
    }
    if (_voiceActive) unawaited(_stopVoice());
    setState(() => _selectedDestination = index);
  }

  Future<void> _connectRealtime() async {
    final conversationId = _conversationId;
    if (conversationId == null) return;
    final hub = HubConnectionBuilder()
        .withUrl(
          '$_apiBaseUrl/hubs/events',
          options: HttpConnectionOptions(
            accessTokenFactory: () async => await _auth.accessToken() ?? '',
          ),
        )
        .withAutomaticReconnect()
        .build();
    hub.on('message.delta', (arguments) {
      if (arguments == null || arguments.isEmpty || !mounted) return;
      final event = arguments.first as Map<Object?, Object?>;
      final delta = event['delta'] as String? ?? '';
      if (delta.isEmpty) return;
      setState(() {
        _showHome = false;
        if (_messages.isEmpty || !_messages.last.pending) {
          _messages.add(
            _Message(role: 'assistant', content: delta, pending: true),
          );
        } else {
          final previous = _messages.removeLast();
          _messages.add(
            _Message(
              role: 'assistant',
              content: '${previous.content}$delta',
              pending: true,
            ),
          );
        }
      });
      _scrollToBottom();
    });
    hub.on('tool.started', (arguments) {
      if (arguments == null || arguments.isEmpty || !mounted) return;
      final event = arguments.first as Map<Object?, Object?>;
      final tool = event['tool'] as String? ?? '';
      setState(() {
        _agentActivity = _toolActivity(tool);
        _agentActivityBusy = true;
      });
    });
    hub.on('tool.completed', (arguments) {
      if (arguments == null || arguments.isEmpty || !mounted) return;
      final event = arguments.first as Map<Object?, Object?>;
      final tool = event['tool'] as String? ?? '';
      setState(() {
        _agentActivity = 'Finished ${_toolName(tool)}';
        _agentActivityBusy = false;
      });
    });
    hub.on('tool.failed', (arguments) {
      if (arguments == null || arguments.isEmpty || !mounted) return;
      final event = arguments.first as Map<Object?, Object?>;
      final tool = event['tool'] as String? ?? '';
      setState(() {
        _agentActivity = '${_toolName(tool)} failed';
        _agentActivityBusy = false;
      });
    });
    hub.on('tool.approval_required', (_) {
      if (mounted) {
        setState(() {
          _agentActivity = 'Waiting for your approval';
          _agentActivityBusy = false;
        });
      }
    });
    hub.on('notification.created', (arguments) {
      if (arguments == null || arguments.isEmpty || !mounted) return;
      final event = arguments.first as Map<Object?, Object?>;
      if (event['type'] == 'task.completed' ||
          event['type'] == 'approval.required') {
        setState(() => _homeRevision++);
      }
      if (event['type'] != 'approval.required') return;
      final notificationId = event['notificationId'] as String?;
      if (notificationId != null) _shownPushNotifications.add(notificationId);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            event['body'] as String? ?? 'Jarvis needs your approval.',
          ),
          action: SnackBarAction(
            label: 'Review',
            onPressed: () => _openUtility('approvals'),
          ),
        ),
      );
    });
    hub.on('agent.waiting_for_approval', (_) {
      if (mounted) {
        setState(() {
          _agentActivity = 'Waiting for your approval';
          _agentActivityBusy = false;
        });
      }
    });
    hub.on('agent.completed', (_) {
      if (mounted) {
        setState(() {
          _agentActivity = null;
          _agentActivityBusy = false;
        });
      }
    });
    hub.on('agent.failed', (arguments) {
      if (!mounted) return;
      final event = arguments?.first as Map<Object?, Object?>?;
      setState(() {
        _agentActivity = null;
        _agentActivityBusy = false;
        _error =
            event?['message'] as String? ??
            'Jarvis could not complete this response.';
      });
    });
    hub.on('voice.transcript', (arguments) {
      if (arguments == null || arguments.isEmpty || !mounted) return;
      final event = arguments.first as Map<Object?, Object?>;
      final transcript = event['text'] as String? ?? '';
      if (transcript.isEmpty) return;
      setState(() {
        _showHome = false;
        _messages.add(_Message(role: 'user', content: transcript));
      });
      _scrollToBottom();
    });
    hub.on('voice.failed', (arguments) {
      if (!mounted) return;
      final event = arguments?.first as Map<Object?, Object?>?;
      setState(
        () => _error = event?['message'] as String? ?? 'Voice session failed.',
      );
      unawaited(_stopVoice());
    });
    hub.onreconnecting(({error}) {
      if (!mounted || _hub != hub) return;
      unawaited(_stopVoice());
      setState(() => _connected = false);
    });
    hub.onreconnected(({connectionId}) {
      if (!mounted || _hub != hub) return;
      unawaited(_restoreRealtime(hub, conversationId));
    });
    hub.onclose(({error}) {
      if (_hub != hub) return;
      unawaited(_stopVoice());
      if (mounted) {
        setState(() {
          _connected = false;
          _agentActivity = null;
          _agentActivityBusy = false;
        });
      }
    });
    _hub = hub;
    try {
      await hub.start();
      await hub.invoke('JoinConversation', args: [conversationId]);
      if (mounted && _hub == hub) setState(() => _connected = true);
    } catch (_) {
      await hub.stop();
      if (_hub == hub) _hub = null;
      rethrow;
    }
  }

  Future<void> _restoreRealtime(
    HubConnection hub,
    String conversationId,
  ) async {
    try {
      await hub.invoke('JoinConversation', args: [conversationId]);
      if (mounted && _hub == hub) {
        setState(() {
          _connected = true;
          _homeRevision++;
        });
      }
    } catch (_) {
      if (mounted && _hub == hub) {
        setState(() {
          _connected = false;
          _error = 'Could not restore realtime updates. Retry the connection.';
        });
      }
    }
  }

  Future<void> _signIn() async {
    setState(() => _authBusy = true);
    try {
      await _auth.signIn();
      if (kIsWeb) return;
      if (mounted) setState(() => _signedOut = false);
      await _initialize();
    } catch (error) {
      if (mounted) setState(() => _error = 'Sign in failed: $error');
    } finally {
      if (mounted) setState(() => _authBusy = false);
    }
  }

  Future<void> _signOut() async {
    await _stopVoice();
    final pushToken = _pushToken;
    if (pushToken != null) {
      try {
        await _http.delete<void>(
          '/api/v1/push-devices',
          data: {'token': pushToken},
        );
      } on DioException {
        // The token is owner-scoped on the server; stale registrations expire at Firebase.
      }
      _pushToken = null;
    }
    await _pushTokenSubscription?.cancel();
    _pushTokenSubscription = null;
    if (Firebase.apps.isNotEmpty) {
      try {
        await FirebaseMessaging.instance.deleteToken();
      } on FirebaseException {
        // The server registration is also removed above when the API is reachable.
      }
    }
    await _auth.signOut();
    await _hub?.stop();
    if (mounted) {
      setState(() {
        _signedOut = true;
        _connected = false;
        _conversationId = null;
        _messages.clear();
        _agentActivity = null;
        _agentActivityBusy = false;
      });
    }
  }

  Future<void> _enablePush() async {
    if (Firebase.apps.isEmpty || kIsWeb) return;
    try {
      final messaging = FirebaseMessaging.instance;
      final settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );
      if (settings.authorizationStatus == AuthorizationStatus.denied) return;
      _pushTokenSubscription ??= messaging.onTokenRefresh.listen((token) {
        unawaited(_registerPushToken(token).catchError((_) {}));
      });
      final token = await messaging.getToken();
      if (token != null) await _registerPushToken(token);
    } on FirebaseException catch (error) {
      if (mounted) {
        setState(
          () => _error = 'Push notifications unavailable: ${error.code}.',
        );
      }
    } on DioException {
      // The API may be offline while Firebase initialization and chat recovery continue.
    }
  }

  Future<void> _registerPushToken(String token) async {
    final platform = defaultTargetPlatform == TargetPlatform.iOS
        ? 'ios'
        : 'android';
    await _http.put<void>(
      '/api/v1/push-devices',
      data: {'token': token, 'platform': platform},
    );
    _pushToken = token;
  }

  void _onForegroundPush(RemoteMessage message) {
    if (mounted &&
        (message.data['type'] == 'task.completed' ||
            message.data['type'] == 'approval.required')) {
      setState(() => _homeRevision++);
    }
    final notificationId = message.data['notificationId'];
    if (notificationId != null &&
        !_shownPushNotifications.add(notificationId)) {
      return;
    }
    if (!mounted) return;
    final notification = message.notification;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(notification?.title ?? 'Jarvis needs your attention.'),
        action: SnackBarAction(
          label: 'Open',
          onPressed: () => _handlePushPayload(message.data),
        ),
      ),
    );
  }

  void _handlePushPayload(Map<String, dynamic> data) {
    if (!mounted) return;
    final type = data['type'] as String?;
    final sourceId = data['sourceId'] as String?;
    if (type == 'approval.required') {
      _openUtility('approvals');
      return;
    }
    if (sourceId == null ||
        (type != 'task.completed' &&
            type != 'reminder.due' &&
            type != 'watch.triggered' &&
            type != 'watch.failed')) {
      _openUtility('reminders');
      return;
    }
    unawaited(
      Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => NotificationDetailsScreen(
            http: _http,
            notificationType: type!,
            sourceId: sourceId,
          ),
        ),
      ),
    );
  }

  Future<void> _send() async {
    final content = _input.text.trim();
    final conversationId = _conversationId;
    if (content.isEmpty ||
        conversationId == null ||
        _sending ||
        _voiceActive ||
        _voiceStarting) {
      return;
    }
    _input.clear();
    setState(() {
      _sending = true;
      _showHome = false;
      _error = null;
      _messages.add(_Message(role: 'user', content: content));
    });
    _scrollToBottom();
    try {
      final response = await _http.post<Map<String, dynamic>>(
        '/api/v1/conversations/$conversationId/messages',
        data: {'content': content},
      );
      final answer = response.data?['content'] as String? ?? '';
      if (mounted) {
        setState(() {
          if (_messages.isNotEmpty &&
              _messages.last.role == 'assistant' &&
              _messages.last.pending) {
            _messages.removeLast();
          }
          _messages.add(_Message(role: 'assistant', content: answer));
        });
      }
    } on DioException catch (error) {
      if (mounted) setState(() => _error = _describeError(error));
    } finally {
      if (mounted) setState(() => _sending = false);
      _scrollToBottom();
    }
  }

  Future<void> _toggleVoice() async {
    if (_voiceActive) {
      await _stopVoice();
      return;
    }
    if (_voiceStarting || _sending) return;
    final conversationId = _conversationId;
    if (conversationId == null) {
      setState(() => _error = 'Connect to Jarvis before starting voice.');
      return;
    }

    setState(() {
      _voiceStarting = true;
      _error = null;
    });
    Room? room;
    try {
      final sessionResponse = await _http.post<Map<String, dynamic>>(
        '/api/v1/voice/session',
        data: {'conversationId': conversationId},
      );
      final session = sessionResponse.data;
      if (session == null) {
        throw StateError('Jarvis returned no voice session.');
      }
      final serverUrl = session['serverUrl'] as String?;
      final token = session['token'] as String?;
      if (serverUrl == null || token == null) {
        throw StateError('Jarvis returned incomplete LiveKit credentials.');
      }

      await AudioManager.instance.setSpeakerOutputPreferred(true);
      room = Room();
      await room.connect(serverUrl, token);
      await room.localParticipant?.setMicrophoneEnabled(true);
      _voiceRoom = room;
      if (mounted) {
        setState(() {
          _voiceActive = true;
          _selectedDestination = 2;
        });
      }
    } on DioException catch (error) {
      if (mounted) setState(() => _error = _describeError(error));
      await room?.disconnect();
      await room?.dispose();
      await AudioManager.instance.setSpeakerOutputPreferred(false);
    } catch (error) {
      await room?.disconnect();
      await room?.dispose();
      await AudioManager.instance.setSpeakerOutputPreferred(false);
      if (mounted) setState(() => _error = 'Could not start voice: $error');
    } finally {
      if (mounted) setState(() => _voiceStarting = false);
    }
  }

  Future<void> _stopVoice() async {
    final room = _voiceRoom;
    _voiceRoom = null;
    await room?.localParticipant?.setMicrophoneEnabled(false);
    await room?.disconnect();
    await room?.dispose();
    await AudioManager.instance.setSpeakerOutputPreferred(false);
    if (mounted && _voiceActive) setState(() => _voiceActive = false);
  }

  String _describeError(DioException error) {
    final status = error.response?.statusCode;
    if (status == 502) {
      return 'Jarvis could not complete this request. Please try again.';
    }
    if (status != null) return 'Jarvis returned HTTP $status.';
    return 'Could not reach the Jarvis API at $_apiBaseUrl.';
  }

  void _scrollToBottom() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) {
        _scroll.animateTo(
          _scroll.position.maxScrollExtent,
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOut,
        );
      }
    });
  }

  String _toolName(String tool) {
    final cleaned = tool.replaceAll(RegExp(r'Async$'), '').replaceAll('_', ' ');
    return cleaned
        .replaceAllMapped(
          RegExp(r'([a-z])([A-Z])'),
          (match) => '${match[1]} ${match[2]}',
        )
        .toLowerCase();
  }

  String _toolActivity(String tool) {
    final name = _toolName(tool);
    if (name.contains('memory')) return 'Searching memory';
    if (name.contains('file')) return 'Searching files';
    if (name.contains('reminder')) return 'Managing a reminder';
    if (name.contains('task')) return 'Managing a task';
    if (name.startsWith('browser ')) return 'Using the browser';
    return 'Using $name';
  }

  @override
  void dispose() {
    unawaited(_stopVoice());
    _hub?.stop();
    unawaited(_pushTokenSubscription?.cancel());
    unawaited(_pushOpenedSubscription?.cancel());
    unawaited(_pushForegroundSubscription?.cancel());
    _input.dispose();
    _scroll.dispose();
    _http.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_signedOut) {
      return Scaffold(
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const _JarvisMark(size: 56),
              const SizedBox(height: 20),
              const Text('Sign in to Jarvis', style: TextStyle(fontSize: 22)),
              const SizedBox(height: 18),
              FilledButton.icon(
                onPressed: _authBusy ? null : _signIn,
                icon: _authBusy
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.login),
                label: const Text('Continue with your identity provider'),
              ),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.all(20),
                  child: Text(_error!),
                ),
            ],
          ),
        ),
      );
    }

    final destination = _selectedDestination;
    final showIndependentScaffold = destination == 1 || destination == 3;
    return Scaffold(
      appBar: showIndependentScaffold
          ? null
          : AppBar(
              titleSpacing: 20,
              title: Text(
                destination == 4
                    ? 'Settings'
                    : destination == 2
                    ? 'Voice'
                    : 'Jarvis',
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
              actions: [
                if (destination == 0 && _messages.isNotEmpty)
                  IconButton(
                    tooltip: _showHome ? 'Continue conversation' : 'Home',
                    onPressed: _sending || _voiceStarting
                        ? null
                        : () => setState(() => _showHome = !_showHome),
                    icon: Icon(
                      _showHome
                          ? Icons.chat_bubble_outline
                          : Icons.home_outlined,
                      size: 21,
                    ),
                  ),
                if (destination == 0)
                  IconButton(
                    tooltip: 'Conversations',
                    onPressed: _sending ? null : _chooseConversation,
                    icon: const Icon(Icons.forum_outlined, size: 21),
                  ),
                Padding(
                  padding: const EdgeInsets.only(left: 4, right: 20),
                  child: Center(
                    child: Container(
                      width: 8,
                      height: 8,
                      decoration: BoxDecoration(
                        color: _connected
                            ? const Color(0xff68d6a8)
                            : const Color(0xff686a77),
                        shape: BoxShape.circle,
                      ),
                    ),
                  ),
                ),
              ],
              backgroundColor: const Color(0xff101117),
            ),
      body: switch (destination) {
        0 => _chatBody(),
        1 => TasksScreen(http: _http),
        2 => _voiceBody(),
        3 => MemoryScreen(http: _http),
        _ => _settingsBody(),
      },
      bottomNavigationBar: NavigationBar(
        selectedIndex: destination,
        onDestinationSelected: _selectDestination,
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.chat_bubble_outline),
            selectedIcon: Icon(Icons.chat_bubble),
            label: 'Chat',
          ),
          NavigationDestination(
            icon: Icon(Icons.checklist_outlined),
            selectedIcon: Icon(Icons.checklist),
            label: 'Tasks',
          ),
          NavigationDestination(
            icon: Icon(Icons.mic_none_rounded),
            selectedIcon: Icon(Icons.graphic_eq_rounded),
            label: 'Voice',
          ),
          NavigationDestination(
            icon: Icon(Icons.psychology_outlined),
            selectedIcon: Icon(Icons.psychology),
            label: 'Memory',
          ),
          NavigationDestination(
            icon: Icon(Icons.settings_outlined),
            selectedIcon: Icon(Icons.settings),
            label: 'Settings',
          ),
        ],
      ),
    );
  }

  Widget _chatBody() => SafeArea(
    child: Column(
      children: [
        if (_error != null)
          MaterialBanner(
            content: Text(_error!),
            leading: const Icon(Icons.info_outline),
            actions: [
              TextButton(onPressed: _initialize, child: const Text('Retry')),
            ],
          ),
        Expanded(
          child: _showHome || _messages.isEmpty
              ? _welcome()
              : ListView.builder(
                  controller: _scroll,
                  padding: const EdgeInsets.fromLTRB(18, 20, 18, 24),
                  itemCount: _messages.length,
                  itemBuilder: (context, index) =>
                      _MessageBubble(message: _messages[index]),
                ),
        ),
        if (_agentActivity case final activity?)
          Padding(
            padding: const EdgeInsets.fromLTRB(22, 0, 22, 8),
            child: Row(
              children: [
                if (_agentActivityBusy)
                  const SizedBox.square(
                    dimension: 14,
                    child: CircularProgressIndicator(strokeWidth: 1.8),
                  )
                else
                  const Icon(Icons.check_circle_outline, size: 15),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    activity,
                    style: TextStyle(
                      color: Colors.white.withValues(alpha: .62),
                      fontSize: 13,
                    ),
                  ),
                ),
              ],
            ),
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(14, 8, 14, 12),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: TextField(
                  controller: _input,
                  enabled: !_voiceActive && !_voiceStarting,
                  minLines: 1,
                  maxLines: 5,
                  textInputAction: TextInputAction.send,
                  onSubmitted: (_) => _send(),
                  decoration: const InputDecoration(hintText: 'Message Jarvis'),
                ),
              ),
              const SizedBox(width: 9),
              IconButton(
                tooltip: _voiceActive ? 'Stop voice' : 'Talk to Jarvis',
                onPressed: _voiceStarting || _sending ? null : _toggleVoice,
                icon: _voiceStarting
                    ? const SizedBox.square(
                        dimension: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : Icon(
                        _voiceActive
                            ? Icons.stop_rounded
                            : Icons.mic_none_rounded,
                      ),
                color: _voiceActive ? const Color(0xfff28b82) : null,
                style: IconButton.styleFrom(minimumSize: const Size(48, 52)),
              ),
              const SizedBox(width: 4),
              IconButton.filled(
                onPressed: _sending || _voiceActive || _voiceStarting
                    ? null
                    : _send,
                icon: _sending
                    ? const SizedBox(
                        width: 19,
                        height: 19,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.arrow_upward_rounded),
                style: IconButton.styleFrom(minimumSize: const Size(52, 52)),
              ),
            ],
          ),
        ),
      ],
    ),
  );

  Widget _voiceBody() => SafeArea(
    child: Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            _JarvisMark(size: _voiceActive ? 96 : 76),
            const SizedBox(height: 24),
            Text(
              _voiceStarting
                  ? 'Connecting to Jarvis…'
                  : _voiceActive
                  ? 'Listening'
                  : 'Talk to Jarvis',
              style: const TextStyle(fontSize: 25, fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 10),
            Text(
              _voiceActive
                  ? 'Speak naturally. You can interrupt at any time.'
                  : 'Voice continues this conversation and its memory.',
              textAlign: TextAlign.center,
              style: TextStyle(color: Colors.white.withValues(alpha: .62)),
            ),
            if (_error != null) ...[
              const SizedBox(height: 20),
              Text(_error!, textAlign: TextAlign.center),
            ],
            const SizedBox(height: 28),
            FilledButton.icon(
              onPressed: _voiceStarting || _sending ? null : _toggleVoice,
              icon: _voiceStarting
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Icon(
                      _voiceActive
                          ? Icons.stop_rounded
                          : Icons.mic_none_rounded,
                    ),
              label: Text(_voiceActive ? 'End voice chat' : 'Start voice chat'),
            ),
          ],
        ),
      ),
    ),
  );

  Widget _settingsBody() => SafeArea(
    child: ListView(
      children: [
        _settingsTile(
          'Integrations',
          'Manage MCP servers and credentials',
          Icons.hub_outlined,
          'integrations',
        ),
        _settingsTile(
          'Approvals',
          'Review actions Jarvis needs permission to run',
          Icons.gpp_maybe_outlined,
          'approvals',
        ),
        _settingsTile(
          'Reminders and notifications',
          'View scheduled reminders and alerts',
          Icons.notifications_none_outlined,
          'reminders',
        ),
        _settingsTile(
          'Condition watches',
          'Manage threshold alerts',
          Icons.monitor_heart_outlined,
          'watches',
        ),
        _settingsTile(
          'Morning briefing',
          'Choose your daily briefing schedule',
          Icons.wb_sunny_outlined,
          'briefing',
        ),
        _settingsTile(
          'Files',
          'Browse uploaded documents',
          Icons.folder_open_outlined,
          'files',
        ),
        _settingsTile(
          'Audit log',
          'Review Jarvis activity',
          Icons.fact_check_outlined,
          'audit',
        ),
        if (_auth.enabled)
          ListTile(
            leading: const Icon(Icons.logout),
            title: const Text('Sign out'),
            onTap: () => unawaited(_signOut()),
          ),
      ],
    ),
  );

  Widget _settingsTile(
    String title,
    String subtitle,
    IconData icon,
    String destination,
  ) => ListTile(
    leading: Icon(icon),
    title: Text(title),
    subtitle: Text(subtitle),
    trailing: const Icon(Icons.chevron_right),
    onTap: () => _openUtility(destination),
  );

  Widget _welcome() => HomeOverview(
    http: _http,
    mark: const _JarvisMark(size: 76),
    ready: _conversationId != null,
    voiceStarting: _voiceStarting,
    onTalk: _conversationId == null || _voiceStarting || _sending
        ? null
        : () => unawaited(_toggleVoice()),
    onOpenTasks: () => _selectDestination(1),
    refreshRevision: _homeRevision,
    onContinueConversation: _messages.isEmpty
        ? null
        : () => setState(() => _showHome = false),
  );
}

class _MessageBubble extends StatelessWidget {
  const _MessageBubble({required this.message});
  final _Message message;

  @override
  Widget build(BuildContext context) {
    final isUser = message.role == 'user';
    return Align(
      alignment: isUser ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        constraints: const BoxConstraints(maxWidth: 680),
        margin: const EdgeInsets.only(bottom: 14),
        padding: EdgeInsets.symmetric(
          horizontal: isUser ? 16 : 3,
          vertical: 12,
        ),
        decoration: isUser
            ? BoxDecoration(
                color: const Color(0xff282638),
                borderRadius: BorderRadius.circular(20),
              )
            : null,
        child: Text(
          message.content.isEmpty && message.pending
              ? 'Thinking…'
              : message.content,
          style: const TextStyle(fontSize: 16, height: 1.55),
        ),
      ),
    );
  }
}

class _JarvisMark extends StatelessWidget {
  const _JarvisMark({required this.size});
  final double size;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      gradient: const RadialGradient(
        colors: [Color(0xffd6ceff), Color(0xff9585ef), Color(0xff554d85)],
      ),
      boxShadow: [
        BoxShadow(
          color: const Color(0xff9585ef).withValues(alpha: .28),
          blurRadius: size * .45,
        ),
      ],
    ),
    child: Icon(
      Icons.blur_on_rounded,
      size: size * .63,
      color: const Color(0xff1b1830),
    ),
  );
}
