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
import 'features/chat/chat_entries.dart';
import 'features/chat/chat_widgets.dart';
import 'features/home/home_overview.dart';
import 'theme.dart';

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
    theme: buildJarvisTheme(),
    home: const ChatScreen(),
  );
}

class ChatScreen extends StatefulWidget {
  const ChatScreen({super.key});

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  static const _wideLayoutWidth = 840.0;

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
  final _entries = <ChatEntry>[];
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
  String? _pushToken;
  StreamSubscription<String>? _pushTokenSubscription;
  StreamSubscription<RemoteMessage>? _pushOpenedSubscription;
  StreamSubscription<RemoteMessage>? _pushForegroundSubscription;
  final Set<String> _shownPushNotifications = {};

  bool get _hasMessages => _entries.any((entry) => entry is MessageEntry);

  bool get _busy =>
      _sending ||
      _entries.any(
        (entry) =>
            entry is ApprovalEntry && entry.status == ApprovalStatus.submitting,
      );

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
          data: const {'title': 'New conversation'},
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
        _entries.clear();
        _error = null;
      });
    }
    final details = await _http.get<Map<String, dynamic>>(
      '/api/v1/conversations/$conversationId',
    );
    final records = details.data?['messages'] as List<dynamic>? ?? [];
    final approvals = await _loadConversationApprovals(conversationId);
    if (mounted && _conversationId == conversationId) {
      setState(() {
        _entries.addAll(
          records.map((item) {
            final message = item as Map<String, dynamic>;
            return MessageEntry(
              role: message['role'] as String,
              content: message['content'] as String,
            );
          }),
        );
        _entries.addAll(approvals);
      });
      _scrollToBottom(jump: true);
    }
    await _connectRealtime();
  }

  Future<List<ApprovalEntry>> _loadConversationApprovals(
    String conversationId,
  ) async {
    try {
      final response = await _http.get<List<dynamic>>('/api/v1/approvals');
      return (response.data ?? [])
          .whereType<Map<String, dynamic>>()
          .where((item) => item['conversationId'] == conversationId)
          .map(ApprovalEntry.fromJson)
          .whereType<ApprovalEntry>()
          .toList();
    } on DioException {
      return const [];
    }
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

  Map<Object?, Object?>? _payload(List<Object?>? arguments) {
    if (arguments == null || arguments.isEmpty) return null;
    final value = arguments.first;
    return value is Map<Object?, Object?> ? value : null;
  }

  /// Index of the trailing "thinking" placeholder, if one is showing.
  int get _placeholderIndex {
    if (_entries.isEmpty) return -1;
    final last = _entries.last;
    return last is MessageEntry &&
            !last.isUser &&
            last.pending &&
            last.content.isEmpty
        ? _entries.length - 1
        : -1;
  }

  void _removePlaceholder() {
    final index = _placeholderIndex;
    if (index >= 0) _entries.removeAt(index);
  }

  void _appendDelta(String delta) {
    final last = _entries.isEmpty ? null : _entries.last;
    if (last is MessageEntry && !last.isUser && last.pending) {
      _entries[_entries.length - 1] = last.copyWith(
        content: '${last.content}$delta',
      );
    } else {
      _entries.add(
        MessageEntry(role: 'assistant', content: delta, pending: true),
      );
    }
  }

  void _completeAssistant(String content) {
    final index = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
    );
    final message = MessageEntry(role: 'assistant', content: content);
    if (index >= 0) {
      _entries[index] = message;
    } else if (content.isNotEmpty) {
      _entries.add(message);
    }
    _settleToolRuns();
  }

  void _toolEvent(String tool, {bool? success}) {
    final runIndex = _entries.lastIndexWhere((entry) => entry is ToolRunEntry);
    final lastUser = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && entry.isUser,
    );
    final lastApproval = _entries.lastIndexWhere(
      (entry) => entry is ApprovalEntry,
    );
    final current =
        runIndex > lastUser && runIndex > lastApproval && runIndex >= 0
        ? _entries[runIndex] as ToolRunEntry
        : null;
    final updated = success == null
        ? (current ?? const ToolRunEntry([])).started(tool)
        : (current ?? const ToolRunEntry([])).finished(tool, success: success);
    if (current != null) {
      _entries[runIndex] = updated;
    } else {
      final placeholder = _placeholderIndex;
      if (placeholder >= 0) {
        _entries.insert(placeholder, updated);
      } else {
        _entries.add(updated);
      }
    }
  }

  void _settleToolRuns() {
    for (var i = 0; i < _entries.length; i++) {
      final entry = _entries[i];
      if (entry is ToolRunEntry && entry.running) _entries[i] = entry.settle();
    }
  }

  void _addApprovals(Iterable<ApprovalEntry> approvals) {
    _removePlaceholder();
    _settleToolRuns();
    for (final approval in approvals) {
      final existing = _entries.indexWhere(
        (entry) => entry is ApprovalEntry && entry.id == approval.id,
      );
      if (existing >= 0) {
        final current = _entries[existing] as ApprovalEntry;
        if (current.status == ApprovalStatus.submitting) continue;
        _entries[existing] = approval;
      } else {
        _entries.add(approval);
      }
    }
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
      final event = _payload(arguments);
      final delta = event?['delta'] as String? ?? '';
      if (delta.isEmpty || !mounted) return;
      setState(() {
        _showHome = false;
        _appendDelta(delta);
      });
      _scrollToBottom();
    });
    hub.on('tool.started', (arguments) {
      final tool = _payload(arguments)?['tool'] as String?;
      if (tool == null || !mounted) return;
      setState(() => _toolEvent(tool));
      _scrollToBottom();
    });
    hub.on('tool.completed', (arguments) {
      final tool = _payload(arguments)?['tool'] as String?;
      if (tool == null || !mounted) return;
      setState(() => _toolEvent(tool, success: true));
    });
    hub.on('tool.failed', (arguments) {
      final tool = _payload(arguments)?['tool'] as String?;
      if (tool == null || !mounted) return;
      setState(() => _toolEvent(tool, success: false));
    });
    hub.on('tool.approval_required', (arguments) {
      final approval = ApprovalEntry.fromJson(_payload(arguments));
      if (approval == null || !mounted) return;
      setState(() {
        _showHome = false;
        _addApprovals([approval]);
      });
      _scrollToBottom();
    });
    hub.on('notification.created', (arguments) {
      final event = _payload(arguments);
      if (event == null || !mounted) return;
      if (event['type'] == 'task.completed' ||
          event['type'] == 'approval.required') {
        setState(() => _homeRevision++);
      }
      if (event['type'] != 'approval.required') return;
      final notificationId = event['notificationId'] as String?;
      if (notificationId != null) _shownPushNotifications.add(notificationId);
      final approvalId = event['sourceId'] as String?;
      final inline = _entries.any(
        (entry) => entry is ApprovalEntry && entry.id == approvalId,
      );
      if (inline && _selectedDestination == 0 && !_showHome) return;
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
    hub.on('agent.completed', (_) {
      if (mounted) setState(_settleToolRuns);
    });
    hub.on('agent.failed', (arguments) {
      if (!mounted) return;
      final event = _payload(arguments);
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        _error =
            event?['message'] as String? ??
            'Jarvis could not complete this response.';
      });
    });
    hub.on('voice.transcript', (arguments) {
      final transcript = _payload(arguments)?['text'] as String? ?? '';
      if (transcript.isEmpty || !mounted) return;
      setState(() {
        _showHome = false;
        _entries.add(MessageEntry(role: 'user', content: transcript));
      });
      _scrollToBottom();
    });
    hub.on('voice.failed', (arguments) {
      if (!mounted) return;
      final event = _payload(arguments);
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
          _settleToolRuns();
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
        _entries.clear();
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

  Future<void> _send([String? text]) async {
    final content = (text ?? _input.text).trim();
    final conversationId = _conversationId;
    if (content.isEmpty ||
        conversationId == null ||
        _busy ||
        _voiceActive ||
        _voiceStarting) {
      return;
    }
    if (text == null) _input.clear();
    final userMessage = MessageEntry(role: 'user', content: content);
    setState(() {
      _sending = true;
      _showHome = false;
      _selectedDestination = 0;
      _error = null;
      _entries.add(userMessage);
      _entries.add(
        const MessageEntry(role: 'assistant', content: '', pending: true),
      );
    });
    _scrollToBottom();
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/conversations/$conversationId/messages',
        data: {'content': content},
      );
      if (!mounted || _conversationId != conversationId) return;
      setState(() => _applyRunResult(response));
    } on DioException catch (error) {
      if (mounted && _conversationId == conversationId) {
        setState(() {
          _removePlaceholder();
          _settleToolRuns();
          final index = _entries.lastIndexOf(userMessage);
          if (index >= 0) _entries[index] = userMessage.copyWith(failed: true);
          _error = _describeError(error);
        });
      }
    } finally {
      if (mounted) setState(() => _sending = false);
      _scrollToBottom();
    }
  }

  void _applyRunResult(Response<dynamic> response) {
    final data = response.data;
    if (response.statusCode == 202 && data is List) {
      _addApprovals(
        data.map(ApprovalEntry.fromJson).whereType<ApprovalEntry>(),
      );
      return;
    }
    final content = data is Map ? data['content'] as String? ?? '' : '';
    _completeAssistant(content);
  }

  Future<void> _retry(MessageEntry message) async {
    if (_busy) return;
    setState(() => _entries.remove(message));
    await _send(message.content);
  }

  Future<void> _decide(ApprovalEntry approval, bool approved) async {
    final conversationId = _conversationId;
    if (conversationId == null || _busy) return;
    void replace(ApprovalEntry Function(ApprovalEntry current) update) {
      final index = _entries.indexWhere(
        (entry) => entry is ApprovalEntry && entry.id == approval.id,
      );
      if (index >= 0) {
        _entries[index] = update(_entries[index] as ApprovalEntry);
      }
    }

    setState(() {
      _error = null;
      replace(
        (current) => current.copyWith(
          status: ApprovalStatus.submitting,
          decision: approved,
          clearError: true,
        ),
      );
      _entries.add(
        const MessageEntry(role: 'assistant', content: '', pending: true),
      );
    });
    _scrollToBottom();
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/approvals/${approval.id}/decision',
        data: {'approved': approved},
      );
      if (!mounted || _conversationId != conversationId) return;
      setState(() {
        replace(
          (current) => current.copyWith(
            status: approved ? ApprovalStatus.approved : ApprovalStatus.denied,
          ),
        );
        _applyRunResult(response);
      });
      setState(() => _homeRevision++);
    } on DioException catch (error) {
      if (!mounted || _conversationId != conversationId) return;
      final status = error.response?.statusCode;
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        replace(
          (current) => status == 404
              ? current.copyWith(
                  status: ApprovalStatus.denied,
                  error: 'This approval is no longer pending.',
                )
              : current.copyWith(
                  status: ApprovalStatus.failed,
                  error: status == 409
                      ? 'This approval was already handled elsewhere.'
                      : 'Jarvis could not finish this step. You can retry.',
                ),
        );
      });
    } finally {
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
    if (status == 503) {
      return 'A Jarvis service is temporarily unavailable. Please try again.';
    }
    if (status == 409) {
      final data = error.response?.data;
      final message = data is Map ? data['message'] as String? : null;
      return message ?? 'Jarvis is busy with this conversation.';
    }
    if (status != null) return 'Jarvis returned HTTP $status.';
    return 'Could not reach the Jarvis API at $_apiBaseUrl.';
  }

  void _scrollToBottom({bool jump = false}) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_scroll.hasClients) return;
      final target = _scroll.position.maxScrollExtent;
      if (jump) {
        _scroll.jumpTo(target);
      } else {
        _scroll.animateTo(
          target,
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOut,
        );
      }
    });
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

  static const _destinations = [
    (Icons.chat_bubble_outline, Icons.chat_bubble, 'Chat'),
    (Icons.checklist_outlined, Icons.checklist, 'Tasks'),
    (Icons.mic_none_rounded, Icons.graphic_eq_rounded, 'Voice'),
    (Icons.psychology_outlined, Icons.psychology, 'Memory'),
    (Icons.settings_outlined, Icons.settings, 'Settings'),
  ];

  @override
  Widget build(BuildContext context) {
    if (_signedOut) return _signInScreen();

    final destination = _selectedDestination;
    final showIndependentScaffold = destination == 1 || destination == 3;
    return LayoutBuilder(
      builder: (context, constraints) {
        final wide = constraints.maxWidth >= _wideLayoutWidth;
        final scaffold = Scaffold(
          appBar: showIndependentScaffold ? null : _appBar(destination),
          body: switch (destination) {
            0 => _chatBody(),
            1 => TasksScreen(http: _http),
            2 => _voiceBody(),
            3 => MemoryScreen(http: _http),
            _ => _settingsBody(),
          },
          bottomNavigationBar: wide
              ? null
              : NavigationBar(
                  selectedIndex: destination,
                  onDestinationSelected: _selectDestination,
                  destinations: [
                    for (final (icon, selected, label) in _destinations)
                      NavigationDestination(
                        icon: Icon(icon),
                        selectedIcon: Icon(selected),
                        label: label,
                      ),
                  ],
                ),
        );
        if (!wide) return scaffold;
        return Scaffold(
          body: Row(
            children: [
              NavigationRail(
                selectedIndex: destination,
                onDestinationSelected: _selectDestination,
                labelType: NavigationRailLabelType.all,
                groupAlignment: -.85,
                leading: Padding(
                  padding: const EdgeInsets.only(top: 18, bottom: 22),
                  child: Tooltip(
                    message: 'New chat',
                    child: InkWell(
                      customBorder: const CircleBorder(),
                      onTap: _busy ? null : _startNewChat,
                      child: const JarvisAvatar(size: 40),
                    ),
                  ),
                ),
                destinations: [
                  for (final (icon, selected, label) in _destinations)
                    NavigationRailDestination(
                      icon: Icon(icon),
                      selectedIcon: Icon(selected),
                      label: Text(label),
                    ),
                ],
              ),
              const VerticalDivider(width: 1),
              Expanded(child: scaffold),
            ],
          ),
        );
      },
    );
  }

  void _startNewChat() {
    if (_selectedDestination != 0) setState(() => _selectedDestination = 0);
    if (!_hasMessages && _conversationId != null) {
      setState(() => _showHome = true);
      return;
    }
    unawaited(_createAndOpenConversation());
  }

  PreferredSizeWidget _appBar(int destination) => AppBar(
    titleSpacing: 20,
    title: Text(switch (destination) {
      4 => 'Settings',
      2 => 'Voice',
      _ => 'Jarvis',
    }),
    actions: [
      if (destination == 0 && _hasMessages)
        IconButton(
          tooltip: _showHome ? 'Continue conversation' : 'Home',
          onPressed: _busy || _voiceStarting
              ? null
              : () => setState(() => _showHome = !_showHome),
          icon: Icon(
            _showHome ? Icons.chat_bubble_outline : Icons.home_outlined,
            size: 21,
          ),
        ),
      if (destination == 0)
        IconButton(
          tooltip: 'New chat',
          onPressed: _busy ? null : _startNewChat,
          icon: const Icon(Icons.edit_square, size: 20),
        ),
      if (destination == 0)
        IconButton(
          tooltip: 'Conversations',
          onPressed: _busy ? null : _chooseConversation,
          icon: const Icon(Icons.forum_outlined, size: 21),
        ),
      Padding(
        padding: const EdgeInsets.only(left: 4, right: 20),
        child: Tooltip(
          message: _connected ? 'Live updates connected' : 'Offline',
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 300),
            width: 8,
            height: 8,
            decoration: BoxDecoration(
              color: _connected
                  ? JarvisColors.success
                  : const Color(0xff686a77),
              shape: BoxShape.circle,
              boxShadow: _connected
                  ? [
                      BoxShadow(
                        color: JarvisColors.success.withValues(alpha: .5),
                        blurRadius: 8,
                      ),
                    ]
                  : null,
            ),
          ),
        ),
      ),
    ],
  );

  Widget _signInScreen() => Scaffold(
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
            Padding(padding: const EdgeInsets.all(20), child: Text(_error!)),
        ],
      ),
    ),
  );

  Widget _chatBody() => SafeArea(
    child: Column(
      children: [
        if (_error != null)
          MaterialBanner(
            backgroundColor: JarvisColors.surfaceRaised,
            content: Text(_error!),
            leading: const Icon(
              Icons.info_outline,
              color: JarvisColors.warning,
            ),
            actions: [
              TextButton(
                onPressed: () => setState(() => _error = null),
                child: const Text('Dismiss'),
              ),
              if (_conversationId == null || !_connected)
                TextButton(onPressed: _initialize, child: const Text('Retry')),
            ],
          ),
        Expanded(
          child: _showHome || _entries.isEmpty
              ? _welcome()
              : ListView.builder(
                  controller: _scroll,
                  padding: const EdgeInsets.fromLTRB(18, 20, 18, 24),
                  itemCount: _entries.length,
                  itemBuilder: (context, index) => Align(
                    alignment: Alignment.topCenter,
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 780),
                      child: SizedBox(
                        width: double.infinity,
                        child: _entryView(_entries[index]),
                      ),
                    ),
                  ),
                ),
        ),
        Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 808),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(14, 4, 14, 14),
              child: ChatComposer(
                controller: _input,
                onSend: () => unawaited(_send()),
                onVoice: _conversationId == null
                    ? null
                    : () => unawaited(_toggleVoice()),
                sending: _busy,
                voiceActive: _voiceActive,
                voiceStarting: _voiceStarting,
              ),
            ),
          ),
        ),
      ],
    ),
  );

  Widget _entryView(ChatEntry entry) => switch (entry) {
    MessageEntry() => MessageBubble(
      key: ObjectKey(entry),
      message: entry,
      onRetry: entry.failed ? () => unawaited(_retry(entry)) : null,
    ),
    ToolRunEntry() => ToolRunView(run: entry),
    ApprovalEntry() => ApprovalCard(
      approval: entry,
      onDecide: (approved) => unawaited(_decide(entry, approved)),
    ),
  };

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
    child: Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 720),
        child: ListView(
          padding: const EdgeInsets.symmetric(vertical: 8),
          children: [
            _settingsSection('Assistant'),
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
              'Morning briefing',
              'Choose your daily briefing schedule and time zone',
              Icons.wb_sunny_outlined,
              'briefing',
            ),
            _settingsSection('Automations'),
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
            _settingsSection('Data'),
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
      ),
    ),
  );

  Widget _settingsSection(String title) => Padding(
    padding: const EdgeInsets.fromLTRB(20, 18, 20, 6),
    child: Text(
      title.toUpperCase(),
      style: const TextStyle(
        fontSize: 11.5,
        letterSpacing: 1.1,
        fontWeight: FontWeight.w600,
        color: JarvisColors.muted,
      ),
    ),
  );

  Widget _settingsTile(
    String title,
    String subtitle,
    IconData icon,
    String destination,
  ) => ListTile(
    leading: Container(
      width: 38,
      height: 38,
      decoration: BoxDecoration(
        color: JarvisColors.accent.withValues(alpha: .12),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Icon(icon, size: 20, color: JarvisColors.accent),
    ),
    title: Text(title),
    subtitle: Text(subtitle, style: const TextStyle(color: JarvisColors.muted)),
    trailing: const Icon(Icons.chevron_right, color: JarvisColors.muted),
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
    onContinueConversation: _hasMessages
        ? () => setState(() => _showHome = false)
        : null,
    onSuggestion: _conversationId == null || _busy
        ? null
        : (text) => unawaited(_send(text)),
  );
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
