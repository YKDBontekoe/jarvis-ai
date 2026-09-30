import 'dart:async';
import 'dart:math' as math;

import 'package:dio/dio.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:livekit_client/livekit_client.dart';
import 'package:signalr_netcore/iretry_policy.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../../api/api_config.dart';
import '../../api/api_errors.dart';
import '../../api/jarvis_http.dart';
import '../../auth/auth_session.dart';
import '../../auth/auth_validation.dart';
import '../../conversations_screen.dart';
import '../../json_maps.dart';
import '../../notification_details_screen.dart';
import '../coding/coding_run_detail_screen.dart';
import '../../notification_routing.dart';
import '../../file_download_stub.dart'
    if (dart.library.io) '../../file_download_io.dart'
    if (dart.library.js_interop) '../../file_download_web.dart'
    as file_download;
import '../../task_details_screen.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../devices/device_invoke.dart';
import '../devices/device_telemetry.dart';
import '../home/home_overview.dart';
import '../settings/settings_view.dart';
import '../search/command_palette.dart';
import '../search/recent_searches_store.dart';
import '../search/search_navigation.dart';
import '../search/search_screen.dart';
import '../shell/sidebar.dart';
import '../shell/utility_pages.dart';
import '../voice/chat_gpt_voices.dart';
import '../voice/voice_stage.dart';
import 'chat_entries.dart';
import 'chat_widgets.dart';
import 'generative_ui.dart';
import 'mcp_setup.dart';
import 'remote_query.dart';

part 'chat_screen_controller.dart';
part 'chat_screen_session.dart';
part 'chat_screen_nav.dart';
part 'chat_screen_realtime.dart';
part 'chat_screen_push.dart';
part 'chat_screen_send.dart';
part 'chat_screen_catchup.dart';
part 'chat_screen_transcript.dart';
part 'chat_screen_voice.dart';
part 'chat_screen_ui.dart';
part 'chat_screen_auth.dart';
part 'chat_screen_sources.dart';
part 'chat_screen_search.dart';

const _voiceCapture = AudioCaptureOptions(
  echoCancellation: true,
  noiseSuppression: true,
  autoGainControl: true,
);

const _wideLayoutWidth = 840.0;

class ChatScreen extends StatefulWidget {
  const ChatScreen({this.skipAuthentication = false, super.key});

  final bool skipAuthentication;

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends _ChatScreenController
    with
        _ChatScreenSession,
        _ChatScreenNav,
        _ChatScreenRealtime,
        _ChatScreenPush,
        _ChatScreenSend,
        _ChatScreenCatchUp,
        _ChatScreenTranscript,
        _ChatScreenVoice,
        _ChatScreenUi,
        _ChatScreenAuth,
        _ChatScreenSources,
        _ChatScreenSearch {
  /// Outgoing content fades out before incoming content fades in, so the two
  /// never overlap mid-transition.
  static const _fadeThrough = Interval(.5, 1, curve: Curves.easeOutCubic);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _scroll.addListener(_handleTranscriptScroll);
    if (widget.skipAuthentication) _restoringSession = false;
    _attachPushListeners();
    attachJarvisAuthInterceptor(
      http: _http,
      auth: _auth,
      isSessionInactive: () => _signedOut || _signingOut,
      onAuthLost: () => unawaited(_signOut()),
    );
    unawaited(_initialize());
  }

  @override
  Widget build(BuildContext context) {
    if (_restoringSession) {
      return const Scaffold(
        body: Stack(
          children: [
            Positioned.fill(child: _AmbientBackdrop()),
            Center(child: JarvisOrb(size: 96, semanticLabel: 'Jarvis')),
          ],
        ),
      );
    }
    if (_signedOut) return _signInScreen();

    return LayoutBuilder(
      builder: (context, constraints) {
        final wide = constraints.maxWidth >= _wideLayoutWidth;
        _isWide = wide;
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
        final shell = Scaffold(
          key: _scaffoldKey,
          drawer: wide
              ? null
              : Drawer(
                  width: math.min(330, constraints.maxWidth * .86),
                  backgroundColor: JarvisColors.of(context).canvas,
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
                    Expanded(
                      child: _utilityPane == null
                          ? content
                          : KeyedSubtree(
                              key: ValueKey(_paneRevision),
                              child: Navigator(
                                onGenerateRoute: (_) => MaterialPageRoute<void>(
                                  builder: (routeContext) {
                                    _paneContext = routeContext;
                                    return _utilityPane!;
                                  },
                                ),
                              ),
                            ),
                    ),
                  ],
                )
              : content,
        );
        return Shortcuts(
          shortcuts: {
            LogicalKeySet(LogicalKeyboardKey.control, LogicalKeyboardKey.keyK):
                OpenSearchIntent(),
            LogicalKeySet(LogicalKeyboardKey.meta, LogicalKeyboardKey.keyK):
                OpenSearchIntent(),
          },
          child: Actions(
            actions: {
              OpenSearchIntent: CallbackAction<OpenSearchIntent>(
                onInvoke: (_) {
                  unawaited(_openSearch(context));
                  return null;
                },
              ),
            },
            child: shell,
          ),
        );
      },
    );
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final backgrounded =
        state == AppLifecycleState.inactive ||
        state == AppLifecycleState.hidden ||
        state == AppLifecycleState.paused ||
        state == AppLifecycleState.detached;
    if (backgrounded) {
      if (_sending || _busy) _remoteQuery = true;
      return;
    }
    if (state != AppLifecycleState.resumed || _conversationId == null) return;
    if (_remoteQuery) unawaited(_catchUpRemoteQuery(_conversationId!));
    final hub = _hub;
    if (!_signedOut &&
        !_signingOut &&
        (hub == null || hub.state == HubConnectionState.Disconnected)) {
      unawaited(_retryConnection());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _catchUpTimer?.cancel();
    _runCancel?.cancel();
    _catchUpGeneration++;
    _realtimeGeneration++;
    unawaited(_stopVoice());
    final hub = _hub;
    _hub = null;
    unawaited(hub?.stop());
    unawaited(_pushTokenSubscription?.cancel());
    unawaited(_pushOpenedSubscription?.cancel());
    unawaited(_pushForegroundSubscription?.cancel());
    _input.dispose();
    _email.dispose();
    _password.dispose();
    _scroll.dispose();
    _http.close();
    super.dispose();
  }

  void _handleTranscriptScroll() {
    if (_scroll.hasClients && _scroll.position.pixels <= 160) {
      unawaited(_loadOlderMessages());
    }
  }
}
