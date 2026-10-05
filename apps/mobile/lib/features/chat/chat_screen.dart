import 'dart:async';

import 'package:dio/dio.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';
import 'package:livekit_client/livekit_client.dart';
import 'package:signalr_netcore/iretry_policy.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../../api/api_config.dart';
import '../../api/api_errors.dart';
import '../../api/jarvis_http.dart';
import '../../auth/auth_session.dart';
import '../../auth/auth_validation.dart';
import '../conversations/conversations_screen.dart';
import '../../error_reporting.dart';
import '../../json_maps.dart';
import '../notifications/notification_details_screen.dart';
import '../coding/coding_run_detail_screen.dart';
import '../notifications/notification_routing.dart';
import '../../push/notification_actions.dart';
import '../../schedule_format.dart';
import '../files/file_download_stub.dart'
    if (dart.library.io) '../files/file_download_io.dart'
    if (dart.library.js_interop) '../files/file_download_web.dart'
    as file_download;
import '../tasks/task_details_screen.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../devices/device_invoke.dart';
import '../devices/device_telemetry.dart';
import '../devices/place_reminder_tracker.dart';
import '../chats/chat_list.dart';
import '../chats/chats_screen.dart';
import '../everything/everything_screen.dart';
import '../home/jarvis_home.dart';
import '../tiles/tile_controller.dart';
import '../people/people_screen.dart';
import '../settings/settings_view.dart';
import '../search/command_palette.dart';
import '../search/recent_searches_store.dart';
import '../search/search_navigation.dart';
import '../search/search_screen.dart';
import '../projects/project_style.dart';
import '../shell/jarvis_tab_bar.dart';
import '../shell/utility_pages.dart';
import '../whatsapp/whatsapp_chat_screen.dart';
import '../voice/chat_gpt_voices.dart';
import '../voice/voice_errors.dart';
import '../voice/voice_stage.dart';
import 'chat_entries.dart';
import 'chat_widgets.dart';
import 'composer_drafts.dart';
import 'conversation_summary.dart';
import 'image_paste_stub.dart'
    if (dart.library.js_interop) 'image_paste_web.dart';
import 'incoming_photos.dart';
import 'outbox_store.dart';
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
part 'chat_screen_photos.dart';
part 'chat_screen_outbox.dart';
part 'chat_screen_summary.dart';
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
        _ChatScreenSearch,
        _ChatScreenPhotos,
        _ChatScreenOutbox,
        _ChatScreenSummary {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _scroll.addListener(_handleTranscriptScroll);
    _input.addListener(_rememberDraft);
    unawaited(_openDrafts());
    unawaited(_openOutbox());
    unawaited(_tiles.open());
    _chatsTimer = Timer.periodic(const Duration(seconds: 60), (_) {
      if (!_signedOut && !_signingOut && _chatList.whatsAppLinked) {
        unawaited(_chatList.loadWhatsApp(_http));
      }
    });
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
        final showTabs = !_inChat && !voice;
        final chat = Scaffold(
          extendBodyBehindAppBar: voice,
          appBar: _topBar(wide: wide, voice: voice),
          body: MotionSwitcher(
            child: KeyedSubtree(
              key: ValueKey(voice),
              child: voice ? _voiceBody() : _chatBody(),
            ),
          ),
        );
        final content = MotionSwitcher(
          child: showTabs
              ? KeyedSubtree(
                  key: ValueKey('page-${_tab.name}'),
                  child: Scaffold(
                    body: SafeArea(
                      bottom: false,
                      child: Column(
                        children: [
                          _shellNotice(),
                          Expanded(child: _tabPage()),
                        ],
                      ),
                    ),
                  ),
                )
              : KeyedSubtree(key: const ValueKey('chat'), child: chat),
        );
        final chatsAttention = _chatList.unreadCount > 0;
        final shell = Scaffold(
          bottomNavigationBar: !wide && showTabs
              ? JarvisTabBar(
                  selected: _tab,
                  onSelect: _selectTab,
                  onJarvis: _openJarvis,
                  chatsAttention: chatsAttention,
                  jarvisBusy: _busy,
                )
              : null,
          body: wide
              ? Row(
                  children: [
                    JarvisNavRail(
                      selected: _utilityPane == null && showTabs ? _tab : null,
                      onSelect: _selectTab,
                      onJarvis: _openJarvis,
                      chatsAttention: chatsAttention,
                      jarvisBusy: _busy,
                    ),
                    Expanded(
                      child: MotionSwitcher(
                        child: _utilityPane == null
                            ? KeyedSubtree(
                                key: const ValueKey('content'),
                                child: content,
                              )
                            : _paneNavigator(),
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

  Widget _paneNavigator() {
    final revision = _paneRevision;
    final page = _utilityPane!;
    return KeyedSubtree(
      key: ValueKey(revision),
      child: Navigator(
        onGenerateRoute: (_) => MaterialPageRoute<void>(
          builder: (routeContext) {
            // A pane that is fading out must not claim the live context.
            if (revision == _paneRevision) _paneContext = routeContext;
            return page;
          },
        ),
      ),
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
      unawaited(_drafts.flush());
      if (_sending || _busy) _remoteQuery = true;
      return;
    }
    if (state == AppLifecycleState.resumed &&
        !_signedOut &&
        !_signingOut &&
        !_restoringSession) {
      unawaited(PlaceReminderTracker.instance.refresh(_http));
    }
    if (state != AppLifecycleState.resumed || _conversationId == null) return;
    unawaited(_flushOutbox());
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
    unawaited(PlaceReminderTracker.instance.detach());
    _catchUpTimer?.cancel();
    _chatsTimer?.cancel();
    _chatList.dispose();
    _tiles.dispose();
    _deltaTimer?.cancel();
    _transcriptTick.dispose();
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
    unawaited(_drafts.flush());
    _outboxTimer?.cancel();
    _composerFocus.dispose();
    _input.dispose();
    _email.dispose();
    _password.dispose();
    _scroll.dispose();
    _http.close();
    super.dispose();
  }

  void _handleTranscriptScroll() {
    if (!_scroll.hasClients) return;
    final position = _scroll.position;
    if (position.pixels <= 160) unawaited(_loadOlderMessages());
    final near = isNearTranscriptBottom(
      position.pixels,
      position.maxScrollExtent,
    );
    if (near != _nearBottom) setState(() => _nearBottom = near);
  }
}
