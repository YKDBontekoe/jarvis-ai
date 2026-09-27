import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';
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
import 'task_details_screen.dart';
import 'notification_details_screen.dart';
import 'notification_routing.dart';
import 'json_maps.dart';
import 'condition_watches_screen.dart';
import 'daily_briefing_screen.dart';
import 'integrations_screen.dart';
import 'features/chat/chat_entries.dart';
import 'features/chat/chat_widgets.dart';
import 'features/home/home_overview.dart';
import 'features/shell/sidebar.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

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
  Future<String?>? _accessTokenInFlight;
  var _generation = 0;
  Future<void> _sessionLock = Future.value();

  Future<T> _serialized<T>(Future<T> Function() action) {
    final previous = _sessionLock;
    final released = Completer<void>();
    _sessionLock = released.future;
    return previous
        .catchError((_) {})
        .then((_) => action())
        .whenComplete(released.complete);
  }

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

  Future<String?> accessToken({bool forceRefresh = false}) {
    final existing = _accessTokenInFlight;
    if (existing != null && !forceRefresh) return existing;
    late final Future<String?> pending;
    pending = _serialized(
      () => _readOrRefreshAccessToken(forceRefresh: forceRefresh),
    ).whenComplete(() {
      if (identical(_accessTokenInFlight, pending)) {
        _accessTokenInFlight = null;
      }
    });
    _accessTokenInFlight = pending;
    return pending;
  }

  Future<String?> _readOrRefreshAccessToken({bool forceRefresh = false}) async {
    final generation = _generation;
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
      if (generation != _generation) return null;
    }
    final expiration = int.tryParse(
      await _storage.read(key: 'token_expiration') ?? '',
    );
    final accessToken = await _storage.read(key: 'access_token');
    if (generation != _generation) return null;
    if (!forceRefresh &&
        accessToken != null &&
        expiration != null &&
        expiration > DateTime.now().millisecondsSinceEpoch + 30000) {
      return accessToken;
    }

    final refreshToken = await _storage.read(key: 'refresh_token');
    if (refreshToken == null) return null;
    try {
      if (kIsWeb) {
        final response = await web_oidc.refreshAuthorizationTokens(
          _oidcIssuer,
          _oidcClientId,
          refreshToken,
        );
        if (generation != _generation) return null;
        await _saveWebResponse(response, refreshToken);
        if (generation != _generation) return null;
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
      if (generation != _generation) return null;
      await _save(
        response.accessToken,
        response.refreshToken ?? refreshToken,
        response.accessTokenExpirationDateTime,
      );
      if (generation != _generation) return null;
      return response.accessToken;
    } catch (error) {
      if (_isInvalidGrantError(error)) {
        _generation++;
        _accessTokenInFlight = null;
        await _storage.deleteAll();
        return null;
      }
      rethrow;
    }
  }

  static bool _isInvalidGrantError(Object error) {
    if (error is PlatformException) {
      return _isInvalidGrant(error.code) || _isInvalidGrant(error.message);
    }
    if (error is DioException) {
      final data = error.response?.data;
      if (data is Map && _isInvalidGrant(data['error']?.toString())) {
        return true;
      }
      return _isInvalidGrant(error.message);
    }
    return _isInvalidGrant(error.toString());
  }

  static bool _isInvalidGrant(String? value) =>
      value != null && value.toLowerCase().contains('invalid_grant');

  Future<void> signIn() async {
    await _serialized(() async {
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
      _generation++;
      _accessTokenInFlight = null;
      await _save(
        response.accessToken,
        response.refreshToken,
        response.accessTokenExpirationDateTime,
      );
    });
  }

  Future<void> signOut() async {
    await _serialized(() async {
      _generation++;
      _accessTokenInFlight = null;
      await _storage.deleteAll();
    });
  }

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
        asJsonString(response['refresh_token']) ?? previousRefreshToken;
    await _save(
      asJsonString(response['access_token']),
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
      receiveTimeout: const Duration(minutes: 20),
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
  bool _signingOut = false;
  bool _authBusy = false;
  bool _voiceActive = false;
  bool _voiceStarting = false;
  int _voiceGeneration = 0;
  CancelToken? _runCancel;
  int _selectedDestination = 0;
  int _homeRevision = 0;
  bool _showHome = true;
  int _realtimeGeneration = 0;
  int _openGeneration = 0;
  int _recentRevision = 0;
  int _initGeneration = 0;
  EventsListener<RoomEvent>? _voiceEvents;
  final _scaffoldKey = GlobalKey<ScaffoldState>();
  List<Map<String, dynamic>> _recent = [];
  String? _pushToken;
  StreamSubscription<String>? _pushTokenSubscription;
  StreamSubscription<RemoteMessage>? _pushOpenedSubscription;
  StreamSubscription<RemoteMessage>? _pushForegroundSubscription;
  final Set<String> _shownPushNotifications = {};

  bool get _hasMessages => _entries.any((entry) => entry is MessageEntry);

  bool get _hasPendingApproval => _entries.any(
    (entry) =>
        entry is ApprovalEntry &&
        (entry.status == ApprovalStatus.pending ||
            entry.status == ApprovalStatus.failed),
  );

  bool get _busy =>
      _sending ||
      _entries.any(
        (entry) =>
            entry is ApprovalEntry && entry.status == ApprovalStatus.submitting,
      );

  @override
  void initState() {
    super.initState();
    _attachPushListeners();
    _http.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          try {
            final token = await _auth.accessToken();
            if (_auth.enabled && (token == null || token.isEmpty)) {
              if (!_signedOut && !_signingOut) unawaited(_signOut());
              handler.reject(
                DioException(
                  requestOptions: options,
                  type: DioExceptionType.cancel,
                  error: StateError('Missing access token.'),
                ),
              );
              return;
            }
            if (token != null) {
              options.headers['Authorization'] = 'Bearer $token';
            }
            handler.next(options);
          } catch (error) {
            handler.reject(DioException(requestOptions: options, error: error));
          }
        },
        onError: (error, handler) async {
          if (_signedOut ||
              _signingOut ||
              !_isAuthExpired(error) ||
              error.requestOptions.extra['jarvisRetriedAuth'] == true) {
            if (!_signedOut && !_signingOut && _isAuthExpired(error)) {
              unawaited(_signOut());
            }
            handler.next(error);
            return;
          }
          try {
            final token = await _auth.accessToken(forceRefresh: true);
            if (token == null || token.isEmpty) {
              if (!_signedOut && !_signingOut) unawaited(_signOut());
              handler.next(error);
              return;
            }
            final request = error.requestOptions;
            request.headers['Authorization'] = 'Bearer $token';
            request.extra['jarvisRetriedAuth'] = true;
            handler.resolve(await _http.fetch(request));
          } on DioException catch (retryError) {
            handler.next(retryError);
          } catch (_) {
            handler.next(error);
          }
        },
      ),
    );
    unawaited(_initialize());
  }

  Future<void> _initialize() async {
    final generation = ++_initGeneration;
    bool stale() =>
        !mounted ||
        generation != _initGeneration ||
        _signedOut ||
        _signingOut;
    try {
      if (stale()) return;
      if (_auth.enabled && await _auth.accessToken() == null) {
        if (mounted && generation == _initGeneration) {
          setState(() => _signedOut = true);
        }
        return;
      }
      if (stale()) return;
      await _enablePush();
      if (stale()) return;
      final list = await _http.get<List<dynamic>>('/api/v1/conversations');
      if (stale()) return;
      final items = jsonMaps(list.data);
      String? conversationId;
      if (items.isNotEmpty && items.first['id'] is String) {
        conversationId = items.first['id'] as String;
      } else {
        final created = await _http.post<Map<String, dynamic>>(
          '/api/v1/conversations',
          data: const {'title': 'New conversation'},
        );
        final id = created.data?['id'];
        conversationId = id is String ? id : null;
      }
      if (conversationId == null || conversationId.isEmpty) {
        throw const FormatException('Missing conversation ID.');
      }
      if (stale()) return;
      await _openConversation(conversationId, showHome: true);
      if (stale()) return;
      if (Firebase.apps.isNotEmpty) {
        final initialPush = await FirebaseMessaging.instance
            .getInitialMessage();
        if (initialPush != null && !stale()) {
          _handlePushPayload(initialPush.data);
        }
      }
      if (mounted && generation == _initGeneration) setState(() => _error = null);
    } on DioException catch (error) {
      if (mounted && generation == _initGeneration) {
        setState(() {
          _error = _describeError(error);
          if (_isAuthExpired(error)) _signedOut = true;
        });
      }
    } on FormatException {
      if (mounted && generation == _initGeneration) {
        setState(() => _error = 'Jarvis returned an invalid conversation.');
      }
    } catch (error) {
      if (mounted && generation == _initGeneration) {
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
    final openGeneration = ++_openGeneration;
    bool isLatestOpen() =>
        mounted &&
        openGeneration == _openGeneration &&
        !_signedOut &&
        !_signingOut;
    final details = await _http.get<Map<String, dynamic>>(
      '/api/v1/conversations/$conversationId',
    );
    if (!isLatestOpen()) return;
    final records = jsonMaps(details.data?['messages']);
    final approvals = await _loadConversationApprovals(conversationId);
    if (!isLatestOpen()) return;

    _runCancel?.cancel();
    final generation = ++_realtimeGeneration;
    bool isCurrent() =>
        mounted &&
        generation == _realtimeGeneration &&
        !_signedOut &&
        !_signingOut;
    await _stopVoice();
    if (!isCurrent() || !isLatestOpen()) return;
    final previous = _hub;
    _hub = null;
    await previous?.stop();
    if (!isCurrent() || !isLatestOpen()) return;
    setState(() {
      _conversationId = conversationId;
      _connected = false;
      _sending = false;
      _selectedDestination = 0;
      _showHome = showHome &&
          !approvals.any(
            (entry) =>
                entry.status == ApprovalStatus.pending ||
                entry.status == ApprovalStatus.failed,
          );
      _entries
        ..clear()
        ..addAll(
          records
              .where(
                (message) =>
                    message['role'] is String && message['content'] is String,
              )
              .map(
                (message) => MessageEntry(
                  role: message['role'] as String,
                  content: message['content'] as String,
                ),
              ),
        )
        ..addAll(approvals);
      _error = null;
    });
    _scrollToBottom(jump: true);
    unawaited(_loadRecent());
    if (!isCurrent() || !isLatestOpen() || _conversationId != conversationId) {
      return;
    }
    try {
      await _connectRealtime(generation);
    } catch (error) {
      if (!isCurrent() || !isLatestOpen() || _conversationId != conversationId) {
        return;
      }
      setState(() {
        _connected = false;
        _error = error is DioException
            ? _describeError(error)
            : 'Could not connect realtime updates.';
      });
    }
  }

  Future<List<ApprovalEntry>> _loadConversationApprovals(
    String conversationId,
  ) async {
    try {
      final response = await _http.get<List<dynamic>>('/api/v1/approvals');
      return jsonMaps(response.data)
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
    if (!mounted || _signedOut || _signingOut) return;
    unawaited(_loadRecent());
    if (selection?.deletedCurrent == true) {
      if (!mounted || _signedOut || _signingOut) return;
      await _clearCurrentConversation();
      if (!mounted || _signedOut || _signingOut) return;
      await _createAndOpenConversation();
    } else if (selection?.conversationId != null &&
        selection!.conversationId != _conversationId) {
      if (!mounted || _signedOut || _signingOut) return;
      try {
        await _openConversation(selection.conversationId!);
      } on DioException catch (error) {
        if (mounted) setState(() => _error = _describeError(error));
      }
    }
  }

  Future<void> _clearCurrentConversation() async {
    _runCancel?.cancel();
    _openGeneration++;
    _realtimeGeneration++;
    await _stopVoice();
    if (!mounted || _signedOut || _signingOut) return;
    final previous = _hub;
    _hub = null;
    await previous?.stop();
    if (!mounted || _signedOut || _signingOut) return;
    setState(() {
      _conversationId = null;
      _connected = false;
      _sending = false;
      _entries.clear();
      _error = null;
    });
  }

  Future<void> _createAndOpenConversation() async {
    try {
      final response = await _http.post<Map<String, dynamic>>(
        '/api/v1/conversations',
        data: const {'title': 'New conversation'},
      );
      final id = response.data?['id'];
      if (id is! String || id.isEmpty) {
        throw const FormatException('Missing conversation ID.');
      }
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
    if (_signedOut || _signingOut) return;
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
      unawaited(_openUtilityPage(destination, page));
    }
  }

  Future<void> _openUtilityPage(String destination, Widget page) async {
    await Navigator.of(
      context,
    ).push<void>(MaterialPageRoute<void>(builder: (_) => page));
    if (!mounted || _signedOut || _signingOut) return;
    if (destination == 'approvals') {
      await _syncConversationApprovals();
    }
    if (!mounted || _signedOut || _signingOut) return;
    if (destination == 'tasks' ||
        destination == 'approvals' ||
        destination == 'watches' ||
        destination == 'reminders') {
      setState(() => _homeRevision++);
    }
  }

  void _selectDestination(int index) {
    if (index == 2) {
      if (_busy ||
          _hasPendingApproval ||
          _signedOut ||
          _signingOut ||
          !_connected ||
          _conversationId == null) {
        if (!_connected || _conversationId == null) {
          setState(() => _error = 'Connect to Jarvis before starting voice.');
        }
        return;
      }
      unawaited(_toggleVoice());
      return;
    }
    if (_voiceActive || _voiceStarting) unawaited(_stopVoice());
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
    final index = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
    );
    if (index < 0) return;
    final message = _entries[index] as MessageEntry;
    if (message.content.isEmpty) {
      _entries.removeAt(index);
    } else {
      _entries[index] = message.copyWith(pending: false);
    }
  }

  void _appendDelta(String delta) {
    final last = _entries.isEmpty ? null : _entries.last;
    if (last is MessageEntry && !last.isUser && last.pending) {
      _entries[_entries.length - 1] = last.copyWith(
        content: '${last.content}$delta',
      );
    } else if (last is MessageEntry && !last.isUser && !last.pending) {
      return;
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
      final last = _entries.isEmpty ? null : _entries.last;
      if (last is MessageEntry && !last.isUser && last.content == content) {
        _settleToolRuns();
        return;
      }
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
        if (current.status != ApprovalStatus.pending &&
            approval.status == ApprovalStatus.pending &&
            !approval.retry) {
          continue;
        }
        _entries[existing] = approval;
      } else {
        _entries.add(approval);
      }
    }
  }

  Future<void> _syncConversationApprovals() async {
    final conversationId = _conversationId;
    if (conversationId == null) return;
    final approvals = await _loadConversationApprovals(conversationId);
    if (!mounted ||
        _signedOut ||
        _signingOut ||
        _conversationId != conversationId) {
      return;
    }
    setState(() {
      _entries.removeWhere((entry) {
        if (entry is! ApprovalEntry) return false;
        if (entry.status != ApprovalStatus.pending &&
            entry.status != ApprovalStatus.failed) {
          return false;
        }
        return !approvals.any((approval) => approval.id == entry.id);
      });
      _addApprovals(approvals);
    });
  }

  bool _hubIsCurrent(HubConnection hub, String conversationId, int generation) =>
      mounted &&
      !_signedOut &&
      identical(_hub, hub) &&
      _conversationId == conversationId &&
      _realtimeGeneration == generation;

  Future<void> _connectRealtime([int? generation]) async {
    final expectedGeneration = generation ?? _realtimeGeneration;
    final conversationId = _conversationId;
    if (conversationId == null) return;
    final hub = HubConnectionBuilder()
        .withUrl(
          '$_apiBaseUrl/hubs/events',
          options: HttpConnectionOptions(
            accessTokenFactory: () async {
              final token = await _auth.accessToken();
              if (_auth.enabled && (token == null || token.isEmpty)) {
                throw StateError('Missing access token for realtime updates.');
              }
              return token ?? '';
            },
          ),
        )
        .withAutomaticReconnect()
        .build();
    hub.on('message.delta', (arguments) {
      final event = _payload(arguments);
      final delta = asJsonString(event?['delta']) ?? '';
      if (delta.isEmpty || !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _showHome = false;
        _appendDelta(delta);
      });
      _scrollToBottom();
    });
    hub.on('message.completed', (arguments) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      final content = asJsonString(_payload(arguments)?['content']) ?? '';
      setState(() => _completeAssistant(content));
      _scrollToBottom();
    });
    hub.on('tool.started', (arguments) {
      final tool = asJsonString(_payload(arguments)?['tool']);
      if (tool == null || !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() => _toolEvent(tool));
      _scrollToBottom();
    });
    hub.on('tool.completed', (arguments) {
      final tool = asJsonString(_payload(arguments)?['tool']);
      if (tool == null || !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() => _toolEvent(tool, success: true));
    });
    hub.on('tool.failed', (arguments) {
      final tool = asJsonString(_payload(arguments)?['tool']);
      if (tool == null || !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() => _toolEvent(tool, success: false));
    });
    hub.on('tool.approval_required', (arguments) {
      final approval = ApprovalEntry.fromJson(_payload(arguments));
      if (approval == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _showHome = false;
        _addApprovals([approval]);
      });
      _scrollToBottom();
    });
    hub.on('notification.created', (arguments) {
      final event = _payload(arguments);
      if (event == null ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      if (event['type'] == 'task.completed' ||
          event['type'] == 'task.failed' ||
          event['type'] == 'approval.required') {
        setState(() => _homeRevision++);
      }
      if (event['type'] != 'approval.required') return;
      final notificationId = asJsonString(event['notificationId']);
      final approvalId = asJsonString(event['sourceId']);
      final inline = _entries.any(
        (entry) => entry is ApprovalEntry && entry.id == approvalId,
      );
      if (inline && _selectedDestination == 0 && !_showHome) return;
      if (notificationId != null &&
          !_shownPushNotifications.add(notificationId)) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            asJsonString(event['body']) ?? 'Jarvis needs your approval.',
          ),
          action: SnackBarAction(
            label: 'Review',
            onPressed: () => _openUtility('approvals'),
          ),
        ),
      );
    });
    hub.on('agent.completed', (_) {
      if (_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        setState(_settleToolRuns);
      }
    });
    hub.on('agent.failed', (arguments) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      final event = _payload(arguments);
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        _error =
            asJsonString(event?['message']) ??
            'Jarvis could not complete this response.';
      });
      if (_voiceActive || _voiceStarting) unawaited(_stopVoice());
    });
    hub.on('voice.transcript', (arguments) {
      final transcript = asJsonString(_payload(arguments)?['text']) ?? '';
      if (transcript.isEmpty ||
          !_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        return;
      }
      setState(() {
        _showHome = false;
        final last = _entries.isEmpty ? null : _entries.last;
        final duplicate =
            last is MessageEntry && last.isUser && last.content == transcript;
        if (!duplicate) {
          _entries.add(MessageEntry(role: 'user', content: transcript));
        }
      });
      _scrollToBottom();
    });
    hub.on('voice.failed', (arguments) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      final event = _payload(arguments);
      setState(
        () => _error = asJsonString(event?['message']) ?? 'Voice session failed.',
      );
      unawaited(_stopVoice());
    });
    hub.onreconnecting(({error}) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      unawaited(_stopVoice());
      setState(() {
        _connected = false;
        _selectedDestination = _selectedDestination == 2 ? 0 : _selectedDestination;
        _removePlaceholder();
        _settleToolRuns();
      });
    });
    hub.onreconnected(({connectionId}) {
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) return;
      unawaited(_restoreRealtime(hub, conversationId));
    });
    hub.onclose(({error}) {
      if (!identical(_hub, hub)) return;
      unawaited(_stopVoice());
      if (mounted) {
        setState(() {
          _connected = false;
          _selectedDestination = _selectedDestination == 2 ? 0 : _selectedDestination;
          _removePlaceholder();
          _settleToolRuns();
          _error ??= 'Realtime updates disconnected. Retry the connection.';
        });
      }
    });
    if (_conversationId != conversationId ||
        _realtimeGeneration != expectedGeneration ||
        _signedOut) {
      await hub.stop();
      return;
    }
    _hub = hub;
    try {
      await hub.start();
      if (!_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        if (identical(_hub, hub)) _hub = null;
        await hub.stop();
        return;
      }
      await hub.invoke('JoinConversation', args: [conversationId]);
      if (_hubIsCurrent(hub, conversationId, expectedGeneration)) {
        setState(() => _connected = true);
      }
    } catch (_) {
      if (identical(_hub, hub)) _hub = null;
      await hub.stop();
      rethrow;
    }
  }

  Future<void> _restoreRealtime(
    HubConnection hub,
    String conversationId,
  ) async {
    final generation = _realtimeGeneration;
    try {
      await hub.invoke('JoinConversation', args: [conversationId]);
      if (mounted &&
          _hub == hub &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(() {
          _connected = true;
          _homeRevision++;
        });
        await _reloadConversationEntries(conversationId, generation);
      }
    } catch (_) {
      if (mounted &&
          _hub == hub &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(() {
          _connected = false;
          _error = 'Could not restore realtime updates. Retry the connection.';
        });
      }
    }
  }

  Future<void> _reloadConversationEntries(
    String conversationId, [
    int? generation,
  ]) async {
    final expectedGeneration = generation ?? _realtimeGeneration;
    try {
      final details = await _http.get<Map<String, dynamic>>(
        '/api/v1/conversations/$conversationId',
      );
      final approvals = await _loadConversationApprovals(conversationId);
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != expectedGeneration ||
          _signedOut ||
          _signingOut) {
        return;
      }
      setState(() {
        _entries
          ..clear()
          ..addAll(
            jsonMaps(details.data?['messages'])
                .where(
                  (message) =>
                      message['role'] is String && message['content'] is String,
                )
                .map(
                  (message) => MessageEntry(
                    role: message['role'] as String,
                    content: message['content'] as String,
                  ),
                ),
          );
        _addApprovals(approvals);
      });
    } on DioException {
      // Keep the current transcript if history cannot be refreshed.
    }
  }

  Future<void> _signIn() async {
    if (_signingOut) return;
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
    if (_signingOut) return;
    _signingOut = true;
    _signedOut = true;
    _initGeneration++;
    _realtimeGeneration++;
    _runCancel?.cancel();
    if (mounted) {
      ScaffoldMessenger.of(context).clearSnackBars();
      Navigator.of(context).popUntil((route) => route.isFirst);
      setState(() {
        _signedOut = true;
        _connected = false;
        _sending = false;
        _conversationId = null;
        _error = null;
        _entries.clear();
        _recent = [];
      });
    }
    try {
    await _stopVoice();
    final hub = _hub;
    _hub = null;
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
    await _pushOpenedSubscription?.cancel();
    _pushOpenedSubscription = null;
    await _pushForegroundSubscription?.cancel();
    _pushForegroundSubscription = null;
    if (Firebase.apps.isNotEmpty) {
      try {
        await FirebaseMessaging.instance.deleteToken();
      } on FirebaseException {
        // The server registration is also removed above when the API is reachable.
      }
    }
    await _auth.signOut();
    await hub?.stop();
    } finally {
      _signingOut = false;
      if (mounted) setState(() {});
    }
  }

  void _attachPushListeners() {
    if (Firebase.apps.isEmpty) return;
    _pushOpenedSubscription ??= FirebaseMessaging.onMessageOpenedApp.listen(
      (message) => _handlePushPayload(message.data),
    );
    _pushForegroundSubscription ??= FirebaseMessaging.onMessage.listen(
      _onForegroundPush,
    );
  }

  Future<void> _enablePush() async {
    if (_signedOut || _signingOut || Firebase.apps.isEmpty || kIsWeb) return;
    try {
      final messaging = FirebaseMessaging.instance;
      final settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );
      if (settings.authorizationStatus == AuthorizationStatus.denied) return;
      if (_signedOut || _signingOut) return;
      _attachPushListeners();
      _pushTokenSubscription ??= messaging.onTokenRefresh.listen((token) {
        unawaited(_registerPushToken(token).catchError((_) {}));
      });
      final token = await messaging.getToken();
      if (token != null && !_signedOut && !_signingOut) {
        await _registerPushToken(token);
      }
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
    if (_signedOut || _signingOut) return;
    final platform = defaultTargetPlatform == TargetPlatform.iOS
        ? 'ios'
        : 'android';
    final previous = _pushToken;
    await _http.put<void>(
      '/api/v1/push-devices',
      data: {'token': token, 'platform': platform},
    );
    if (_signedOut || _signingOut) {
      try {
        await _http.delete<void>(
          '/api/v1/push-devices',
          data: {'token': token},
        );
      } on DioException {
        // Sign-out already dropped local state; stale server rows expire at Firebase.
      }
      return;
    }
    if (previous != null && previous != token) {
      try {
        await _http.delete<void>(
          '/api/v1/push-devices',
          data: {'token': previous},
        );
      } on DioException {
        // The new token is registered; Firebase expires the previous one.
      }
    }
    _pushToken = token;
  }

  void _onForegroundPush(RemoteMessage message) {
    if (!mounted || _signedOut) return;
    if (message.data['type'] == 'task.completed' ||
        message.data['type'] == 'task.failed' ||
        message.data['type'] == 'approval.required') {
      setState(() => _homeRevision++);
    }
    final notificationId = asJsonString(message.data['notificationId']);
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
    if (!mounted || _signedOut) return;
    unawaited(_markPushNotificationRead(asJsonString(data['notificationId'])));
    final type = asJsonString(data['type']);
    final sourceId = asJsonString(data['sourceId']);
    if (opensApprovalScreen(type)) {
      _openUtility('approvals');
      return;
    }
    if (opensDailyBriefing(type)) {
      _openUtility('briefing');
      return;
    }
    if (opensTaskDetails(type) && sourceId != null) {
      unawaited(_openPushedDetail(
        TaskDetailsScreen(http: _http, taskId: sourceId),
      ));
      return;
    }
    if (sourceId == null || !opensNotificationDetails(type)) {
      _openUtility('reminders');
      return;
    }
    unawaited(
      _openPushedDetail(
        NotificationDetailsScreen(
          http: _http,
          notificationType: type!,
          sourceId: sourceId,
        ),
      ),
    );
  }

  Future<void> _markPushNotificationRead(String? notificationId) async {
    if (notificationId == null || notificationId.isEmpty) return;
    try {
      await _http.post('/api/v1/notifications/$notificationId/read');
    } on DioException {
      // The unread badge refreshes the next time Reminders is opened.
    }
  }

  Future<void> _openPushedDetail(Widget page) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(builder: (_) => page),
    );
    if (mounted && !_signedOut && !_signingOut) {
      setState(() => _homeRevision++);
    }
  }

  Future<bool> _send([String? text]) async {
    final content = (text ?? _input.text).trim();
    final conversationId = _conversationId;
    final generation = _realtimeGeneration;
    if (content.isEmpty ||
        conversationId == null ||
        _busy ||
        _hasPendingApproval ||
        _voiceActive ||
        _voiceStarting ||
        _signedOut ||
        _signingOut) {
      return false;
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
    final run = CancelToken();
    _runCancel = run;
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/conversations/$conversationId/messages',
        data: {'content': content},
        cancelToken: run,
      );
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return true;
      }
      setState(() => _applyRunResult(response));
      unawaited(_loadRecent());
      return true;
    } on DioException catch (error) {
      if (mounted &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(() {
          _removePlaceholder();
          _settleToolRuns();
          final index = _entries.lastIndexOf(userMessage);
          if (index >= 0) _entries[index] = userMessage.copyWith(failed: true);
          _error = error.type == DioExceptionType.cancel
              ? null
              : _describeError(error);
        });
      }
      return true;
    } finally {
      if (identical(_runCancel, run)) _runCancel = null;
      if (mounted &&
          _conversationId == conversationId &&
          _realtimeGeneration == generation) {
        setState(() => _sending = false);
      }
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
    final content = data is Map ? asJsonString(data['content']) ?? '' : '';
    _completeAssistant(content);
  }

  Future<void> _retry(MessageEntry message) async {
    if (_busy ||
        _hasPendingApproval ||
        _voiceActive ||
        _voiceStarting ||
        _signedOut ||
        _signingOut) {
      return;
    }
    setState(() => _entries.remove(message));
    final sent = await _send(message.content);
    if (!sent && mounted && !_entries.contains(message)) {
      setState(() => _entries.add(message));
    }
  }

  Future<void> _decide(ApprovalEntry approval, bool approved) async {
    final conversationId = _conversationId;
    if (conversationId == null ||
        _busy ||
        _voiceActive ||
        _voiceStarting ||
        _signedOut ||
        _signingOut) {
      return;
    }
    final generation = _realtimeGeneration;
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
    final run = CancelToken();
    _runCancel = run;
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/approvals/${approval.id}/decision',
        data: {'approved': approved},
        cancelToken: run,
      );
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return;
      }
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
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return;
      }
      if (error.type == DioExceptionType.cancel) {
        setState(() {
          _removePlaceholder();
          _settleToolRuns();
          replace(
            (current) => current.copyWith(
              status: ApprovalStatus.pending,
              clearError: true,
            ),
          );
        });
        return;
      }
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
              : status == 409
              ? current.copyWith(
                  status: ApprovalStatus.pending,
                  error:
                      'Decide the earlier pending tool call first, or this one was already handled.',
                )
              : current.copyWith(
                  status: ApprovalStatus.failed,
                  error: 'Jarvis could not finish this step. You can retry.',
                ),
        );
      });
      if (status == 409) await _syncConversationApprovals();
    } catch (_) {
      if (!mounted ||
          _conversationId != conversationId ||
          _realtimeGeneration != generation) {
        return;
      }
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        replace(
          (current) => current.copyWith(
            status: ApprovalStatus.failed,
            error: 'Jarvis could not finish this step. You can retry.',
          ),
        );
      });
    } finally {
      if (identical(_runCancel, run)) _runCancel = null;
      _scrollToBottom();
    }
  }

  Future<void> _toggleVoice() async {
    if (_voiceActive || _voiceStarting) {
      await _stopVoice();
      return;
    }
    if (_sending || _busy || _hasPendingApproval || _signedOut || _signingOut) {
      return;
    }
    final conversationId = _conversationId;
    if (conversationId == null || !_connected) {
      setState(() {
        _error = 'Connect to Jarvis before starting voice.';
        if (_selectedDestination == 2) _selectedDestination = 0;
      });
      return;
    }

    final voiceGeneration = ++_voiceGeneration;
    setState(() {
      _voiceStarting = true;
      _selectedDestination = 2;
      _error = null;
    });
    Room? room;
    final generation = _realtimeGeneration;
    bool isCurrent() =>
        mounted &&
        voiceGeneration == _voiceGeneration &&
        !_signedOut &&
        !_signingOut &&
        _conversationId == conversationId &&
        _realtimeGeneration == generation;
    try {
      final sessionResponse = await _http.post<Map<String, dynamic>>(
        '/api/v1/voice/session',
        data: {'conversationId': conversationId},
      );
      if (!isCurrent()) return;
      final session = sessionResponse.data;
      if (session == null) {
        throw StateError('Jarvis returned no voice session.');
      }
      final serverUrl = asJsonString(session['serverUrl']);
      final token = asJsonString(session['token']);
      if (serverUrl == null || token == null) {
        throw StateError('Jarvis returned incomplete LiveKit credentials.');
      }

      await AudioManager.instance.setSpeakerOutputPreferred(true);
      if (!isCurrent()) return;
      room = Room();
      _voiceRoom = room;
      _listenToVoiceRoom(room);
      await room.connect(serverUrl, token).timeout(const Duration(seconds: 20));
      if (!isCurrent() || _voiceRoom != room) {
        await _abandonVoiceRoom(room);
        return;
      }
      await room.localParticipant?.setMicrophoneEnabled(true);
      if (!isCurrent() || _voiceRoom != room) {
        await _abandonVoiceRoom(room);
        return;
      }
      setState(() {
        _voiceActive = true;
        _selectedDestination = 2;
      });
    } on DioException catch (error) {
      if (isCurrent()) setState(() => _error = _describeError(error));
      await _abandonVoiceRoom(room);
    } catch (error) {
      await _abandonVoiceRoom(room);
      if (isCurrent()) setState(() => _error = 'Could not start voice: $error');
    } finally {
      if (mounted && voiceGeneration == _voiceGeneration) {
        setState(() {
          _voiceStarting = false;
          if (!_voiceActive && _selectedDestination == 2) {
            _selectedDestination = 0;
          }
        });
      }
    }
  }

  Future<void> _abandonVoiceRoom(Room? room) async {
    if (room == null) return;
    final owned = identical(_voiceRoom, room);
    if (owned) {
      _voiceEvents?.dispose();
      _voiceEvents = null;
      _voiceRoom = null;
    } else {
      return;
    }
    try {
      await room.disconnect();
      await room.dispose();
    } catch (_) {}
    try {
      await AudioManager.instance.setSpeakerOutputPreferred(false);
    } catch (_) {}
  }

  void _listenToVoiceRoom(Room room) {
    _voiceEvents?.dispose();
    final listener = room.createListener();
    _voiceEvents = listener;
    listener.on<RoomDisconnectedEvent>((event) {
      if (!identical(_voiceRoom, room)) return;
      unawaited(_stopVoice());
      if (mounted) {
        setState(() {
          if (_selectedDestination == 2) _selectedDestination = 0;
          _error ??= 'The voice session ended.';
        });
      }
    });
  }

  Future<void> _stopVoice() async {
    _voiceGeneration++;
    _voiceEvents?.dispose();
    _voiceEvents = null;
    final room = _voiceRoom;
    _voiceRoom = null;
    try {
      await room?.localParticipant?.setMicrophoneEnabled(false);
      await room?.disconnect();
      await room?.dispose();
    } catch (_) {}
    try {
      await AudioManager.instance.setSpeakerOutputPreferred(false);
    } catch (_) {}
    if (mounted && (_voiceActive || _voiceStarting || _selectedDestination == 2)) {
      setState(() {
        _voiceActive = false;
        _voiceStarting = false;
        if (_selectedDestination == 2) _selectedDestination = 0;
      });
    }
  }

  String _describeError(DioException error) {
    final status = error.response?.statusCode;
    if (status == 401) {
      return 'Your sign-in has expired. Sign in again to continue.';
    }
    if (status == 502) {
      return 'Jarvis could not complete this request. Please try again.';
    }
    if (status == 503) {
      return 'A Jarvis service is temporarily unavailable. Please try again.';
    }
    if (status == 409) {
      final data = error.response?.data;
      final message = data is Map ? asJsonString(data['message']) : null;
      return message ?? 'Jarvis is busy with this conversation.';
    }
    if (status != null) return 'Jarvis returned HTTP $status.';
    return 'Could not reach the Jarvis API at $_apiBaseUrl.';
  }

  bool _isAuthExpired(DioException error) =>
      _auth.enabled && error.response?.statusCode == 401;

  Future<void> _retryConnection() async {
    final conversationId = _conversationId;
    if (conversationId != null) {
      try {
        await _openConversation(conversationId, showHome: _showHome);
        if (mounted &&
            !_signedOut &&
            !_signingOut &&
            _conversationId == conversationId) {
          setState(() => _error = null);
        }
      } on DioException catch (error) {
        if (mounted &&
            !_signedOut &&
            !_signingOut &&
            _conversationId == conversationId) {
          setState(() {
            _error = _describeError(error);
            if (_isAuthExpired(error)) _signedOut = true;
          });
        }
      } catch (error) {
        if (mounted &&
            !_signedOut &&
            !_signingOut &&
            _conversationId == conversationId) {
          setState(() => _error = 'Could not connect to Jarvis: $error');
        }
      }
      return;
    }
    await _initialize();
  }

  void _scrollToBottom({bool jump = false}) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_scroll.hasClients) return;
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
    _realtimeGeneration++;
    unawaited(_stopVoice());
    final hub = _hub;
    _hub = null;
    unawaited(hub?.stop());
    unawaited(_pushTokenSubscription?.cancel());
    unawaited(_pushOpenedSubscription?.cancel());
    unawaited(_pushForegroundSubscription?.cancel());
    _input.dispose();
    _scroll.dispose();
    _http.close();
    super.dispose();
  }

  /// Outgoing content fades out before incoming content fades in, so the two
  /// never overlap mid-transition.
  static const _fadeThrough = Interval(.5, 1, curve: Curves.easeOutCubic);

  @override
  Widget build(BuildContext context) {
    if (_signedOut) return _signInScreen();

    return LayoutBuilder(
      builder: (context, constraints) {
        final wide = constraints.maxWidth >= _wideLayoutWidth;
        final voice = _selectedDestination == 2;
        final content = Scaffold(
          extendBodyBehindAppBar: voice,
          appBar: _topBar(wide: wide, voice: voice),
          body: AnimatedSwitcher(
            duration: const Duration(milliseconds: 260),
            switchInCurve: _fadeThrough,
            switchOutCurve: _fadeThrough,
            child: KeyedSubtree(
              key: ValueKey(voice),
              child: voice ? _voiceBody() : _chatBody(),
            ),
          ),
        );
        final sidebar = _sidebar(wide: wide);
        return Scaffold(
          key: _scaffoldKey,
          drawer: wide
              ? null
              : Drawer(
                  width: math.min(330, constraints.maxWidth * .86),
                  backgroundColor: JarvisColors.canvas,
                  surfaceTintColor: Colors.transparent,
                  shape: const RoundedRectangleBorder(
                    borderRadius: BorderRadius.horizontal(
                      right: Radius.circular(28),
                    ),
                  ),
                  child: sidebar,
                ),
          onDrawerChanged: (open) {
            if (open) unawaited(_loadRecent());
          },
          body: wide
              ? Row(
                  children: [
                    SizedBox(width: 292, child: sidebar),
                    const VerticalDivider(width: 1),
                    Expanded(child: content),
                  ],
                )
              : content,
        );
      },
    );
  }

  Widget _sidebar({required bool wide}) => JarvisSidebar(
    conversations: _recent,
    selectedConversationId: _showHome ? null : _conversationId,
    homeSelected: _showHome && _selectedDestination == 0,
    connected: _connected,
    onHome: () => _fromSidebar(() {
      if (_hasPendingApproval) {
        if (_selectedDestination != 0) _selectDestination(0);
        setState(() => _showHome = false);
        return;
      }
      if (_selectedDestination != 0) _selectDestination(0);
      setState(() => _showHome = true);
    }),
    onNewChat: () => _fromSidebar(_startNewChat),
    onVoice: () => _fromSidebar(() => _selectDestination(2)),
    onConversation: (id) => _fromSidebar(() async {
      if (_selectedDestination != 0) _selectDestination(0);
      if (id == _conversationId) {
        setState(() => _showHome = false);
        return;
      }
      try {
        await _openConversation(id);
      } on DioException catch (error) {
        if (mounted) setState(() => _error = _describeError(error));
      }
    }),
    onSeeAll: () => _fromSidebar(() => unawaited(_chooseConversation())),
    onUtility: (destination) => _fromSidebar(() => _openUtility(destination)),
    onSettings: () => _fromSidebar(_openSettings),
  );

  void _fromSidebar(VoidCallback action) {
    final scaffold = _scaffoldKey.currentState;
    if (scaffold?.isDrawerOpen ?? false) scaffold!.closeDrawer();
    action();
  }

  Future<void> _loadRecent() async {
    if (_signedOut || _signingOut) return;
    final revision = ++_recentRevision;
    try {
      final response = await _http.get<List<dynamic>>('/api/v1/conversations');
      if (!mounted ||
          _signedOut ||
          _signingOut ||
          revision != _recentRevision) {
        return;
      }
      setState(
        () => _recent = jsonMaps(response.data)
            .where((item) => item['id'] is String)
            .toList(),
      );
    } on DioException {
      // The sidebar keeps its last known list while the API is unreachable.
    }
  }

  void _openSettings() => unawaited(
    Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => Scaffold(
          appBar: AppBar(title: const Text('Settings')),
          body: _settingsBody(),
        ),
      ),
    ),
  );

  void _showQuickActions() {
    Widget action(String title, String subtitle, IconData icon, String to) =>
        ListTile(
          leading: IconBadge(icon: icon),
          title: Text(title),
          subtitle: Text(subtitle),
          onTap: () {
            Navigator.pop(context);
            _openUtility(to);
          },
        );
    unawaited(
      showModalBottomSheet<void>(
        context: context,
        builder: (context) => SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(8, 0, 8, 12),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                action(
                  'Upload a file',
                  'PDFs and text become searchable',
                  PhosphorIconsRegular.paperclip,
                  'files',
                ),
                action(
                  'Start a background task',
                  'Jarvis works on it and reports back',
                  PhosphorIconsRegular.listChecks,
                  'tasks',
                ),
                action(
                  'Set a reminder',
                  'Pick a date and time',
                  PhosphorIconsRegular.bell,
                  'reminders',
                ),
                action(
                  'Add a memory',
                  'Tell Jarvis something to remember',
                  PhosphorIconsRegular.notebook,
                  'memory',
                ),
              ],
            ),
          ),
        ),
      ),
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

  PreferredSizeWidget _topBar({required bool wide, required bool voice}) =>
      AppBar(
        toolbarHeight: 64,
        backgroundColor: voice ? Colors.transparent : null,
        automaticallyImplyLeading: false,
        leadingWidth: 64,
        leading: voice
            ? Center(
                child: CircleIconButton(
                  icon: PhosphorIconsRegular.x,
                  tooltip: 'Close voice',
                  onPressed: () => _selectDestination(0),
                ),
              )
            : wide
            ? null
            : Center(
                child: CircleIconButton(
                  icon: PhosphorIconsRegular.list,
                  tooltip: 'Menu',
                  onPressed: () => _scaffoldKey.currentState?.openDrawer(),
                ),
              ),
        centerTitle: true,
        title: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              voice ? 'Voice' : 'Jarvis',
              style: const TextStyle(
                fontSize: 17,
                fontWeight: FontWeight.w600,
                letterSpacing: -.3,
              ),
            ),
            const SizedBox(width: 7),
            _ConnectionDot(connected: _connected),
          ],
        ),
        actions: [
          if (!voice)
            CircleIconButton(
              icon: PhosphorIconsRegular.notePencil,
              tooltip: 'New chat',
              onPressed: _busy ? null : _startNewChat,
            ),
          const SizedBox(width: 12),
        ],
      );

  Widget _signInScreen() => Scaffold(
    body: Stack(
      children: [
        const Positioned.fill(child: _AmbientBackdrop()),
        SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(28),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 400),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const JarvisOrb(size: 96, semanticLabel: 'Jarvis'),
                    const SizedBox(height: 32),
                    Text(
                      'Sign in to Jarvis',
                      textAlign: TextAlign.center,
                      style: JarvisType.serif.copyWith(fontSize: 42),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      'Your private assistant for conversations, tasks, memory, and voice.',
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                        color: JarvisColors.inkSoft,
                      ),
                    ),
                    const SizedBox(height: 32),
                    SizedBox(
                      width: double.infinity,
                      child: FilledButton.icon(
                        onPressed: _authBusy || _signingOut ? null : _signIn,
                        style: FilledButton.styleFrom(
                          backgroundColor: JarvisColors.ink,
                          minimumSize: const Size.fromHeight(54),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(40),
                          ),
                        ),
                        icon: _authBusy
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(PhosphorIconsRegular.signIn),
                        label: const Text(
                          'Continue with your identity provider',
                        ),
                      ),
                    ),
                    const SizedBox(height: 16),
                    const Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(
                          PhosphorIconsRegular.lockSimple,
                          size: 14,
                          color: JarvisColors.muted,
                        ),
                        SizedBox(width: 6),
                        Text(
                          'Secured with OpenID Connect + PKCE',
                          style: TextStyle(
                            fontSize: 12.5,
                            color: JarvisColors.muted,
                          ),
                        ),
                      ],
                    ),
                    if (_error != null)
                      InlineNotice(
                        message: _error!,
                        tone: NoticeTone.danger,
                        margin: const EdgeInsets.only(top: 24),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ],
    ),
  );

  Widget _chatBody() => SafeArea(
    child: Column(
      children: [
        if (_error != null)
          ContentWidth(
            maxWidth: 808,
            child: InlineNotice(
              message: _error!,
              margin: const EdgeInsets.fromLTRB(14, 4, 14, 8),
              actions: [
                if (_conversationId == null || !_connected)
                  TextButton(
                    onPressed: _retryConnection,
                    child: const Text('Retry'),
                  ),
                TextButton(
                  onPressed: () => setState(() => _error = null),
                  style: TextButton.styleFrom(
                    foregroundColor: JarvisColors.inkSoft,
                  ),
                  child: const Text('Dismiss'),
                ),
              ],
            ),
          )
        else if (!_connected && _conversationId != null)
          ContentWidth(
            maxWidth: 808,
            child: InlineNotice(
              message: 'Realtime updates are offline.',
              margin: const EdgeInsets.fromLTRB(14, 4, 14, 8),
              actions: [
                TextButton(
                  onPressed: _retryConnection,
                  child: const Text('Retry'),
                ),
              ],
            ),
          ),
        Expanded(
          child: (_showHome && !_hasPendingApproval) || _entries.isEmpty
              ? _welcome()
              : ListView.builder(
                  controller: _scroll,
                  padding: const EdgeInsets.fromLTRB(18, 16, 18, 24),
                  itemCount: _entries.length,
                  itemBuilder: (context, index) => Align(
                    alignment: Alignment.topCenter,
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 760),
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
            constraints: const BoxConstraints(maxWidth: 788),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(14, 4, 14, 14),
              child: ChatComposer(
                controller: _input,
                onSend: () => unawaited(_send()),
                onCancel: _busy ? () => _runCancel?.cancel() : null,
                onVoice: _conversationId == null ||
                        (_busy && !_voiceActive && !_voiceStarting)
                    ? null
                    : () => _selectDestination(2),
                onAttach: _showQuickActions,
                sending: _busy,
                awaitingApproval: _hasPendingApproval,
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

  Widget _voiceBody() {
    final theme = Theme.of(context);
    return Stack(
      children: [
        const Positioned.fill(child: _AmbientBackdrop()),
        SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(32),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 420),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    SizedBox.square(
                      dimension: 240,
                      child: Center(
                        child: JarvisOrb(
                          size: 128,
                          animate: _voiceStarting,
                          listening: _voiceActive,
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    AnimatedSwitcher(
                      duration: const Duration(milliseconds: 240),
                      switchInCurve: _fadeThrough,
                      switchOutCurve: _fadeThrough,
                      child: Text(
                        _voiceStarting
                            ? 'Connecting to Jarvis…'
                            : _voiceActive
                            ? 'Listening'
                            : 'Talk to Jarvis',
                        key: ValueKey('$_voiceStarting$_voiceActive'),
                        style: JarvisType.serif.copyWith(fontSize: 40),
                      ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      _voiceActive
                          ? 'Speak naturally. You can interrupt at any time.'
                          : 'Voice continues this conversation and its memory.',
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodyLarge?.copyWith(
                        color: JarvisColors.inkSoft,
                      ),
                    ),
                    if (_error != null)
                      InlineNotice(
                        message: _error!,
                        tone: NoticeTone.danger,
                        margin: const EdgeInsets.only(top: 20),
                      ),
                    const SizedBox(height: 32),
                    FilledButton.icon(
                      onPressed: _voiceActive || _voiceStarting
                          ? _toggleVoice
                          : (_busy || _hasPendingApproval ? null : _toggleVoice),
                      style: FilledButton.styleFrom(
                        backgroundColor: _voiceActive || _voiceStarting
                            ? JarvisColors.danger
                            : JarvisColors.ink,
                        minimumSize: const Size(220, 56),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(40),
                        ),
                      ),
                      icon: _voiceStarting
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : Icon(
                              _voiceActive
                                  ? PhosphorIconsRegular.stop
                                  : PhosphorIconsRegular.microphone,
                            ),
                      label: Text(
                        _voiceActive || _voiceStarting
                            ? 'End voice chat'
                            : 'Start voice chat',
                      ),
                    ),
                    const SizedBox(height: 24),
                    const Wrap(
                      alignment: WrapAlignment.center,
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        _VoiceHint(
                          icon: PhosphorIconsRegular.waveform,
                          label: 'Interrupt anytime',
                        ),
                        _VoiceHint(
                          icon: PhosphorIconsRegular.brain,
                          label: 'Uses your memory',
                        ),
                        _VoiceHint(
                          icon: PhosphorIconsRegular.shieldCheck,
                          label: 'Asks before acting',
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _settingsBody() => SafeArea(
    child: ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
      children: [
        ContentWidth(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SurfaceCard(
                child: Row(
                  children: [
                    const JarvisOrb(size: 44, glow: false),
                    const SizedBox(width: 16),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Your assistant',
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            _connected
                                ? 'Live updates connected'
                                : 'Offline — live updates paused',
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
              _settingsGroup('Assistant', [
                _settingsTile(
                  'Integrations',
                  'Manage MCP servers and credentials',
                  PhosphorIconsRegular.plugsConnected,
                  'integrations',
                ),
                _settingsTile(
                  'Approvals',
                  'Review actions Jarvis needs permission to run',
                  PhosphorIconsRegular.shieldCheck,
                  'approvals',
                ),
                _settingsTile(
                  'Morning briefing',
                  'Choose your daily briefing schedule and time zone',
                  PhosphorIconsRegular.sunHorizon,
                  'briefing',
                ),
              ]),
              _settingsGroup('Automations', [
                _settingsTile(
                  'Reminders and notifications',
                  'View scheduled reminders and alerts',
                  PhosphorIconsRegular.bell,
                  'reminders',
                ),
                _settingsTile(
                  'Condition watches',
                  'Manage threshold alerts',
                  PhosphorIconsRegular.pulse,
                  'watches',
                ),
              ]),
              _settingsGroup('Data', [
                _settingsTile(
                  'Files',
                  'Browse uploaded documents',
                  PhosphorIconsRegular.folderOpen,
                  'files',
                ),
                _settingsTile(
                  'Audit log',
                  'Review Jarvis activity',
                  PhosphorIconsRegular.listChecks,
                  'audit',
                ),
              ]),
              if (_auth.enabled)
                _settingsGroup('Account', [
                  ListTile(
                    leading: const IconBadge(
                      icon: PhosphorIconsRegular.signOut,
                      color: JarvisColors.danger,
                      size: 34,
                    ),
                    title: const Text(
                      'Sign out',
                      style: TextStyle(color: JarvisColors.danger),
                    ),
                    onTap: () => unawaited(_signOut()),
                  ),
                ]),
            ],
          ),
        ),
      ],
    ),
  );

  Widget _settingsGroup(String title, List<Widget> tiles) => Padding(
    padding: const EdgeInsets.only(top: 24),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(6, 0, 6, 8),
          child: Text(
            title.toUpperCase(),
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: JarvisColors.muted,
              letterSpacing: .8,
            ),
          ),
        ),
        SurfaceCard(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Column(
            children: [
              for (final (index, tile) in tiles.indexed) ...[
                if (index > 0) const Divider(indent: 64),
                tile,
              ],
            ],
          ),
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
    leading: IconBadge(icon: icon, size: 34),
    title: Text(title),
    subtitle: Text(subtitle),
    trailing: const Icon(
      PhosphorIconsRegular.caretRight,
      size: 16,
      color: JarvisColors.muted,
    ),
    onTap: () => _openUtility(destination),
  );

  Widget _welcome() => HomeOverview(
    http: _http,
    mark: const JarvisOrb(size: 56),
    ready: _conversationId != null,
    voiceStarting: _voiceStarting,
    onTalk: _conversationId == null ||
            _busy ||
            _hasPendingApproval ||
            !_connected
        ? null
        : () => _selectDestination(2),
    onOpenTasks: () => _openUtility('tasks'),
    refreshRevision: _homeRevision,
    onContinueConversation: _hasMessages
        ? () => setState(() => _showHome = false)
        : null,
    onSuggestion: _conversationId == null || _busy || _hasPendingApproval
        ? null
        : (text) => unawaited(_send(text)),
  );
}

class _ConnectionDot extends StatelessWidget {
  const _ConnectionDot({required this.connected});

  final bool connected;

  @override
  Widget build(BuildContext context) => Tooltip(
    message: connected ? 'Live updates connected' : 'Offline',
    child: AnimatedContainer(
      duration: const Duration(milliseconds: 300),
      width: 7,
      height: 7,
      decoration: BoxDecoration(
        color: connected ? JarvisColors.success : JarvisColors.outlineStrong,
        shape: BoxShape.circle,
      ),
    ),
  );
}

/// Floating frosted capsule for the main sections, with a detached voice
/// button; the voice destination starts a session rather than just
/// switching screens, so it is kept visually separate.
class _VoiceHint extends StatelessWidget {
  const _VoiceHint({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
    decoration: BoxDecoration(
      color: JarvisColors.surface.withValues(alpha: .8),
      borderRadius: BorderRadius.circular(40),
      border: Border.all(color: JarvisColors.outline),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 15, color: JarvisColors.inkSoft),
        const SizedBox(width: 6),
        Text(
          label,
          style: const TextStyle(
            fontSize: 12.5,
            fontWeight: FontWeight.w500,
            color: JarvisColors.inkSoft,
          ),
        ),
      ],
    ),
  );
}

/// Soft, blurred colour fields behind full-screen moments (sign-in, voice).
class _AmbientBackdrop extends StatelessWidget {
  const _AmbientBackdrop();

  @override
  Widget build(BuildContext context) => const IgnorePointer(
    child: Stack(
      children: [
        Positioned(
          top: -120,
          left: -80,
          child: _Blob(size: 360, color: Color(0x1c7c6cff)),
        ),
        Positioned(
          bottom: -140,
          right: -100,
          child: _Blob(size: 420, color: Color(0x1638bdf8)),
        ),
        Positioned(
          top: 180,
          right: -60,
          child: _Blob(size: 220, color: Color(0x12f472b6)),
        ),
      ],
    ),
  );
}

class _Blob extends StatelessWidget {
  const _Blob({required this.size, required this.color});

  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      gradient: RadialGradient(colors: [color, color.withValues(alpha: 0)]),
    ),
  );
}
