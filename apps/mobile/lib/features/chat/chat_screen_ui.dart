part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenUi on _ChatScreenController {
  Future<String?> _loadMemoryText(String memoryId) async {
    try {
      final response = await _http.get<dynamic>('/api/v1/memory/$memoryId');
      return asJsonString(jsonObject(response.data)?['content']);
    } on DioException {
      return null;
    }
  }

  /// Opens the conversation over the tab pages, landing on the latest message.
  void _showTranscript() {
    setState(() {
      _showHome = false;
      _inChat = true;
    });
    _scrollToBottom(jump: true, force: true);
  }

  void _scrollToBottom({bool jump = false, bool force = false}) =>
      _scrollToBottomStep(jump: jump, force: force, settle: 3);

  void _scrollToBottomStep({
    required bool jump,
    required bool force,
    required int settle,
  }) {
    // A reader who scrolled up keeps their place while a reply streams in.
    if (!force && !_nearBottom) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      if (!_scroll.hasClients) {
        // The transcript list is not built yet (it mounts after the home view).
        if (jump && settle > 0) {
          _scrollToBottomStep(jump: true, force: true, settle: settle - 1);
        }
        return;
      }
      final target = _scroll.position.maxScrollExtent;
      if (jump) {
        _scroll.jumpTo(target);
        // Lazily built items change the extent once they are measured, so
        // settle on the real bottom over the next frames.
        if (settle > 0) {
          _scrollToBottomStep(jump: true, force: true, settle: settle - 1);
        }
      } else {
        _scroll.animateTo(
          target,
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOut,
        );
      }
    });
  }

  void _dismissKeyboard() => FocusManager.instance.primaryFocus?.unfocus();

  /// Runs a move made from the tab bar, the rail or a tile: pages it opens
  /// replace whatever the content area shows instead of stacking on it.
  void _fromShell(VoidCallback action) {
    _dismissKeyboard();
    _closeUtilityPane();
    _openingFromShell = true;
    try {
      action();
    } finally {
      _openingFromShell = false;
    }
  }

  void _selectTab(JarvisTab tab) => _fromShell(() {
    if (_selectedDestination != 0) _selectDestination(0);
    setState(() {
      _tab = tab;
      _inChat = false;
    });
    if (tab == JarvisTab.chats) unawaited(_refreshChats());
  });

  Future<void> _refreshChats() async {
    if (_signedOut || _signingOut) return;
    await Future.wait([_loadRecent(), _chatList.loadWhatsApp(_http)]);
  }

  void _presentChat() {
    if (!_inChat && mounted) setState(() => _inChat = true);
  }

  Future<void> _presentConversation(String conversationId) {
    _presentChat();
    return _openConversation(conversationId);
  }

  /// The orb: back to the conversation that is open, or a fresh one.
  void _openJarvis() {
    final source = _tabOrbRect();
    _fromShell(() {
      if (_selectedDestination != 0) _selectDestination(0);
      if (_conversationId == null) {
        setState(() => _inChat = true);
        unawaited(_createAndOpenConversation());
      } else if (_hasMessages) {
        _showTranscript();
      } else {
        setState(() => _inChat = true);
      }
    });
    if (source != null && _inChat && !JarvisMotion.reduced(context)) {
      setState(() => _orbFlying = true);
      WidgetsBinding.instance.addPostFrameCallback((_) async {
        if (!mounted) return;
        await flyOrb(context: context, from: source, to: _titleOrbKey);
        if (mounted) setState(() => _orbFlying = false);
      });
    }
  }

  /// Where the tab bar's orb is on screen, or null when it is not showing.
  Rect? _tabOrbRect() {
    final box = _tabOrbKey.currentContext?.findRenderObject();
    if (box is! RenderBox || !box.attached || !box.hasSize) return null;
    final center = box.localToGlobal(box.size.center(Offset.zero));
    return Rect.fromCenter(center: center, width: 40, height: 40);
  }

  void _leaveChat() {
    _dismissKeyboard();
    setState(() => _inChat = false);
  }

  Future<void> _openJarvisChat(String id) async {
    if (_selectedDestination != 0) _selectDestination(0);
    if (id == _conversationId) {
      _showTranscript();
      return;
    }
    setState(() => _inChat = true);
    try {
      await _openConversation(id);
    } on DioException catch (error) {
      if (mounted) setState(() => _error = describeApiError(error));
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not open that conversation.');
    }
  }

  void _openChatItem(ChatListItem item) => _fromShell(() {
    if (item.isJarvis) {
      final id = item.conversationId;
      if (id != null) unawaited(_openJarvisChat(id));
      return;
    }
    final chat = item.whatsApp;
    final channelId = item.channelId;
    if (chat == null || channelId == null) return;
    final page = WhatsAppChatScreen(
      http: _http,
      channelId: channelId,
      chat: chat,
      account: item.account,
    );
    if (_isWide) {
      _showInPane('whatsapp-chat', page);
    } else {
      unawaited(_openUtilityPage('whatsapp-chat', page));
    }
  });

  void _openChatByKey(String key) {
    for (final item in _chatList.all) {
      if (item.key == key) {
        _openChatItem(item);
        return;
      }
    }
  }

  /// Opens what a tile or a settings row points at.
  void _openTile(String destination) {
    switch (destination) {
      case 'chats':
        _selectTab(JarvisTab.chats);
      case 'settings':
        _selectTab(JarvisTab.you);
      case 'voice':
        _fromShell(() => _selectDestination(2));
      default:
        _fromShell(() => _openUtility(destination));
    }
  }

  /// Connection and sign-in problems, above whichever tab is showing.
  Widget _shellNotice() {
    final message = _error;
    if (message == null) return const SizedBox.shrink();
    return ContentWidth(
      maxWidth: 808,
      child: InlineNotice(
        message: message,
        margin: const EdgeInsets.fromLTRB(16, 4, 16, 4),
        actions: [
          if (_conversationId == null || !_connected)
            TextButton(onPressed: _retryConnection, child: const Text('Retry')),
          TextButton(
            onPressed: () => setState(() => _error = null),
            style: TextButton.styleFrom(
              foregroundColor: JarvisColors.of(context).inkSoft,
            ),
            child: const Text('Dismiss'),
          ),
        ],
      ),
    );
  }

  Widget _tabPage() => switch (_tab) {
    JarvisTab.home => JarvisHome(
      source: _tileSource,
      layout: _tiles,
      chats: _chatList,
      ready: _conversationId != null,
      refreshRevision: _homeRevision,
      jarvisBusy: _busy,
      onOpen: _openTile,
      onOpenChat: _openChatByKey,
      onAddTile: () => _selectTab(JarvisTab.everything),
      onSuggestion: _conversationId == null || _busy || _hasPendingApproval
          ? null
          : (text) {
              _presentChat();
              unawaited(_send(text));
            },
    ),
    JarvisTab.chats => ChatsScreen(
      chats: _chatList,
      http: _http,
      onOpen: _openChatItem,
      onNewChat: _busy ? null : _startNewChat,
      onSearch: () => unawaited(_openSearch(context)),
      onManage: () => _fromShell(() => unawaited(_chooseConversation())),
      onRefresh: _refreshChats,
      onConnectWhatsApp: () => _openTile('whatsapp'),
    ),
    JarvisTab.everything => EverythingScreen(
      source: _tileSource,
      layout: _tiles,
      onOpen: _openTile,
    ),
    JarvisTab.you => Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(22, 20, 22, 0),
          child: Text(
            'Settings',
            style: JarvisType.displayOf(context).copyWith(fontSize: 28),
          ),
        ),
        Expanded(child: _settingsBody()),
      ],
    ),
  };

  void _showQuickActions() {
    Widget action(String title, String subtitle, IconData icon, String to) =>
        ListTile(
          leading: IconBadge(icon: icon),
          title: Text(title),
          subtitle: Text(subtitle),
          onTap: () {
            Navigator.pop(context);
            if (to == 'attach-sources') {
              unawaited(_pickConversationSources());
              return;
            }
            _openUtility(to);
          },
        );
    unawaited(
      showModalBottomSheet<void>(
        context: context,
        // Seven actions do not fit the default half-height sheet on a phone.
        isScrollControlled: true,
        builder: (context) => SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.fromLTRB(8, 0, 8, 12),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (_canSummarize)
                  ListTile(
                    leading: const IconBadge(
                      icon: PhosphorIconsRegular.sparkle,
                    ),
                    title: const Text('Summarize this chat'),
                    subtitle: const Text('Key points and what is left to do'),
                    onTap: () {
                      Navigator.pop(context);
                      _showConversationSummary();
                    },
                  ),
                action(
                  'Attach chat sources',
                  'Limit file search to selected documents',
                  PhosphorIconsRegular.folders,
                  'attach-sources',
                ),
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
                  'tasks$createDestinationSuffix',
                ),
                action(
                  'Set a reminder',
                  'Pick a date and time',
                  PhosphorIconsRegular.bell,
                  'reminders$createDestinationSuffix',
                ),
                action(
                  'Add a memory',
                  'Tell Jarvis something to remember',
                  PhosphorIconsRegular.notebook,
                  'memory$createDestinationSuffix',
                ),
                ListTile(
                  leading: const IconBadge(
                    icon: PhosphorIconsRegular.plugsConnected,
                  ),
                  title: const Text('Connect an app'),
                  subtitle: const Text('Calendar, mail, GitHub, and more'),
                  onTap: () {
                    Navigator.pop(context);
                    unawaited(_send(mcpSetupPrompt));
                  },
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  PreferredSizeWidget _topBar({required bool wide, required bool voice}) =>
      AppBar(
        toolbarHeight: 64,
        backgroundColor: voice ? Colors.transparent : null,
        automaticallyImplyLeading: false,
        leadingWidth: 64,
        leading: Center(
          child: voice
              ? CircleIconButton(
                  icon: PhosphorIconsRegular.x,
                  tooltip: 'Close voice',
                  onPressed: () => _selectDestination(0),
                )
              : CircleIconButton(
                  icon: PhosphorIconsRegular.arrowLeft,
                  tooltip: 'Back',
                  onPressed: _leaveChat,
                ),
        ),
        centerTitle: true,
        title: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            GestureDetector(
              onTap: voice || _openProject == null
                  ? null
                  : () => _openUtility('$projectDestinationPrefix$_projectId'),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (!voice && _openProject != null) ...[
                    ProjectBadge(
                      color: asJsonString(_openProject!['color']),
                      size: 20,
                    ),
                    const SizedBox(width: 7),
                  ] else if (!voice) ...[
                    // Where the tab bar's orb lands when it opens chat.
                    KeyedSubtree(
                      key: _titleOrbKey,
                      child: Opacity(
                        opacity: _orbFlying ? 0 : 1,
                        child: JarvisOrb(
                          size: 22,
                          glow: false,
                          thinking: _busy,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                  ],
                  Flexible(
                    child: Text(
                      voice
                          ? 'Voice'
                          : asJsonString(_openProject?['name']) ?? 'Jarvis',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontFamily: 'Geist',
                        fontSize: 17,
                        fontWeight: FontWeight.w600,
                        letterSpacing: -.3,
                      ),
                    ),
                  ),
                  const SizedBox(width: 7),
                  _ConnectionDot(connected: _connected),
                ],
              ),
            ),
            if (!voice && (_profileName != null || _profileDeleted))
              GestureDetector(
                onTap: _busy ? null : _switchConversationProfile,
                child: Padding(
                  padding: const EdgeInsets.only(top: 2),
                  child: Text(
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    _profileDeleted
                        ? '${_profileName ?? 'Profile'} (deleted)'
                        : _profileName ?? 'Profile',
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w500,
                      color: JarvisColors.of(context).inkSoft,
                    ),
                  ),
                ),
              ),
          ],
        ),
        actions: [
          if (!voice)
            ToolbarCapsule(
              children: [
                _NotificationBell(
                  unread: _unreadNotifications,
                  onPressed: _signedOut
                      ? null
                      : () => _openUtility('notifications'),
                ),
                CircleIconButton(
                  bare: true,
                  icon: PhosphorIconsRegular.magnifyingGlass,
                  tooltip: 'Search',
                  onPressed: _signedOut
                      ? null
                      : () => unawaited(_openSearch(context)),
                ),
                CircleIconButton(
                  bare: true,
                  icon: PhosphorIconsRegular.notePencil,
                  tooltip: 'New chat',
                  onPressed: _busy ? null : _startNewChat,
                ),
              ],
            ),
          const SizedBox(width: 14),
        ],
      );

  /// Shown while an approval waits and the transcript is scrolled away from it.
  bool get _showApprovalDock =>
      _hasPendingApproval && !_nearBottom && _entries.isNotEmpty;

  /// The sidebar's copy of the open conversation's project, if any.
  Map<String, dynamic>? get _openProject {
    final id = _projectId;
    if (id == null || _showHome) return null;
    for (final project in _projects) {
      if (project['id'] == id) return project;
    }
    return null;
  }

  Widget _chatBody() => SafeArea(
    child: LayoutBuilder(
      builder: (context, bodyConstraints) => Column(
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
                      foregroundColor: JarvisColors.of(context).inkSoft,
                    ),
                    child: const Text('Dismiss'),
                  ),
                ],
              ),
            )
          else if (!_connected && _conversationId != null)
            Padding(
              padding: const EdgeInsets.only(top: 2, bottom: 6),
              child: StatusChip(
                label: 'Realtime updates are offline',
                color: JarvisColors.of(context).warning,
                actionLabel: 'Retry',
                onAction: _retryConnection,
              ),
            ),
          Expanded(
            // Home and the transcript fade through each other instead of
            // swapping in one frame.
            child: EdgeFade(
              child: AnimatedSwitcher(
                duration: JarvisMotion.of(context, JarvisMotion.base),
                switchInCurve: JarvisMotion.standard,
                switchOutCurve: JarvisMotion.exit,
                transitionBuilder: JarvisMotion.fadeRise,
                child: (_showHome && !_hasPendingApproval) || _entries.isEmpty
                    ? KeyedSubtree(
                        key: const ValueKey('home'),
                        child: _welcome(),
                      )
                    : KeyedSubtree(
                        key: const ValueKey('transcript'),
                        child: Stack(
                          children: [
                            Positioned.fill(
                              child: ValueListenableBuilder<int>(
                                valueListenable: _transcriptTick,
                                builder: (context, _, _) => ListView.builder(
                                  controller: _scroll,
                                  keyboardDismissBehavior:
                                      ScrollViewKeyboardDismissBehavior.onDrag,
                                  padding: const EdgeInsets.fromLTRB(
                                    18,
                                    16,
                                    18,
                                    24,
                                  ),
                                  itemCount: _entries.length,
                                  itemBuilder: (context, index) => Align(
                                    alignment: Alignment.topCenter,
                                    child: ConstrainedBox(
                                      constraints: const BoxConstraints(
                                        maxWidth: 760,
                                      ),
                                      child: SizedBox(
                                        width: double.infinity,
                                        child: FadeSlideIn(
                                          animate: index >= _settledEntries,
                                          offset: 14,
                                          scale: .94,
                                          alignment: _entryOrigin(
                                            _entries[index],
                                          ),
                                          child: _entryView(_entries[index]),
                                        ),
                                      ),
                                    ),
                                  ),
                                ),
                              ),
                            ),
                            Positioned(
                              right: 16,
                              bottom: 8,
                              child: IgnorePointer(
                                ignoring: _nearBottom || _showApprovalDock,
                                child: AnimatedScale(
                                  scale: _nearBottom || _showApprovalDock
                                      ? .4
                                      : 1,
                                  duration: JarvisMotion.of(
                                    context,
                                    const Duration(milliseconds: 420),
                                  ),
                                  curve: _nearBottom || _showApprovalDock
                                      ? JarvisMotion.exit
                                      : JarvisSprings.pop,
                                  child: AnimatedOpacity(
                                    opacity: _nearBottom || _showApprovalDock
                                        ? 0
                                        : 1,
                                    duration: JarvisMotion.of(
                                      context,
                                      JarvisMotion.base,
                                    ),
                                    curve: JarvisMotion.standard,
                                    child: ExcludeSemantics(
                                      excluding:
                                          _nearBottom || _showApprovalDock,
                                      child: CircleIconButton(
                                        icon: PhosphorIconsRegular.caretDown,
                                        tooltip: 'Jump to latest',
                                        size: 44,
                                        onPressed: () =>
                                            _scrollToBottom(force: true),
                                      ),
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
              ),
            ),
          ),
          if (_liveSurface case final live? when surfaceAwaitsReply(live))
            ConstrainedBox(
              constraints: BoxConstraints(
                maxWidth: 788,
                maxHeight: pinnedSurfaceMaxHeight(bodyConstraints.maxHeight),
              ),
              child: ListView(
                shrinkWrap: true,
                padding: const EdgeInsets.fromLTRB(14, 0, 14, 2),
                children: [
                  UiSurfaceCard(
                    key: ValueKey('live-${live.id}'),
                    surface: live,
                    pinned: true,
                    errorText: _surfaceErrorFor == live.id
                        ? _surfaceError
                        : null,
                    onAction: (action, values) =>
                        _submitSurface(live, action, values),
                  ),
                ],
              ),
            ),
          // A waiting approval locks the composer; when its card has
          // scrolled away, this keeps the way back one tap away.
          AnimatedSwitcher(
            duration: JarvisMotion.of(context, JarvisMotion.base),
            switchInCurve: JarvisMotion.standard,
            switchOutCurve: JarvisMotion.exit,
            transitionBuilder: JarvisMotion.fadeRise,
            child: _showApprovalDock
                ? Padding(
                    key: const ValueKey('approval-dock'),
                    padding: const EdgeInsets.fromLTRB(14, 2, 14, 0),
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 760),
                      child: _ApprovalDock(
                        onReview: () => _scrollToBottom(force: true),
                      ),
                    ),
                  )
                : const SizedBox(width: double.infinity),
          ),
          Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 788),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(14, 4, 14, 14),
                child: ChatComposer(
                  controller: _input,
                  onSend: () => unawaited(_send()),
                  onCancel: _busy ? () => unawaited(_cancelActiveRun()) : null,
                  onVoice:
                      _conversationId == null ||
                          (_busy && !_voiceActive && !_voiceStarting)
                      ? null
                      : () => _selectDestination(2),
                  onAttach: _showQuickActions,
                  sources: _sourceChips,
                  onRemoveSource: _conversationId == null
                      ? null
                      : (source) => unawaited(_detachSource(source)),
                  sending: _busy,
                  awaitingApproval: _hasPendingApproval,
                  voiceActive: _voiceActive,
                  voiceStarting: _voiceStarting,
                  focusRequests: _composerFocus,
                  photos: _pendingPhotos,
                  onRemovePhoto: _removePendingPhoto,
                  onPhoto: _conversationId == null ? null : _showPhotoSources,
                  onImages: _conversationId == null
                      ? null
                      : (images) => unawaited(_enqueueIncomingPhotos(images)),
                ),
              ),
            ),
          ),
        ],
      ),
    ),
  );

  /// New entries grow out of the side they sit on: yours from the right,
  /// Jarvis's from the left.
  Alignment _entryOrigin(ChatEntry entry) =>
      entry is MessageEntry && entry.isUser
      ? Alignment.bottomRight
      : Alignment.bottomLeft;

  Widget _entryView(ChatEntry entry) => switch (entry) {
    MessageEntry() => MessageBubble(
      key: ObjectKey(entry),
      message: entry,
      onRetry: entry.failed ? () => unawaited(_retry(entry)) : null,
      onRate: entry.id == null || _conversationId == null
          ? null
          : (rating) => unawaited(_rate(entry, rating)),
      onCitationTap: (citation) => unawaited(_openCitation(citation)),
      photoLoader: _loadPhoto,
      onCancelQueued: entry.queued
          ? () => unawaited(_cancelQueued(entry))
          : null,
      onRegenerate:
          !entry.isUser &&
              !entry.pending &&
              entry.id != null &&
              identical(entry, _lastMessageEntry) &&
              !_busy &&
              !_hasPendingApproval &&
              !_voiceActive &&
              !_voiceStarting
          ? () => unawaited(_regenerate(entry))
          : null,
      onEdit: entry.isUser && !_voiceActive && !_voiceStarting
          ? (text) => unawaited(_editAsNewMessage(text))
          : null,
      thinkingLabel: entry.pending && entry.content.isEmpty
          ? thinkingLabel(_entries)
          : 'Thinking',
    ),
    ToolRunEntry() => ToolRunView(
      run: entry,
      onOpenTasks: () => _openUtility('tasks'),
      onOpenEntity: (ref) => ref.type == 'conversation'
          ? unawaited(_presentConversation(ref.id))
          : _openUtility(ref.destination),
    ),
    ApprovalEntry() => ApprovalCard(
      approval: entry,
      onDecide: (approved) => unawaited(_decide(entry, approved)),
      onAlwaysAllow: entry.canRememberCategory
          ? () => unawaited(_decide(entry, true, rememberCategory: true))
          : null,
      loadMemoryText: _loadMemoryText,
    ),
    UiSurfaceEntry() => _surfaceView(entry),
    BrowserSessionEntry() => BrowserTimelineView(
      session: entry,
      loadScreenshot: _loadComputerScreenshot,
      onWatch: () => unawaited(_watchComputer(entry)),
      onTakeOver: () => unawaited(_watchComputer(entry, takeOver: true)),
      onHandBack: () => unawaited(_handBackComputer(entry)),
    ),
  };

  Widget _surfaceView(UiSurfaceEntry entry) {
    final live = _liveSurface;
    if (live != null && entry.id == live.id && surfaceAwaitsReply(live)) {
      return const SizedBox.shrink();
    }
    final current =
        live != null && entry.id == live.id && entry.status == 'open';
    return UiSurfaceCard(
      key: ValueKey(entry.id),
      surface: entry,
      onAction: current
          ? (action, values) => _submitSurface(entry, action, values)
          : null,
    );
  }

  String get _shownVoicePhase {
    if (_voiceStarting) return 'connecting';
    if (!_voiceActive) return 'idle';
    if (_voiceReconnecting) return 'reconnecting';
    return _voicePhase;
  }

  Widget _voiceBody() => VoiceStage(
    phase: _shownVoicePhase,
    voiceName: voiceLabel(_voiceName),
    handsFree: _voiceHandsFree,
    captions: _voiceCaptions,
    caption: _voiceCaption,
    captionRole: _voiceCaptionRole,
    muted: _voiceMuted,
    error: _error,
    canStart: !_busy && !_hasPendingApproval && _conversationId != null,
    onPrimary: () => unawaited(_toggleVoice()),
    onToggleMute: _voiceActive ? () => unawaited(_toggleVoiceMute()) : null,
    level: _voiceActive ? _voiceLevel : null,
  );

  Widget _settingsBody() => SettingsView(
    connected: _connected,
    onOpen: (destination) => _fromShell(() => _openUtility(destination)),
    onSignOut: _auth.enabled ? () => unawaited(_signOut()) : null,
  );

  Widget _welcome() => _NewChatWelcome(
    onSuggestion: _conversationId == null || _busy || _hasPendingApproval
        ? null
        : (text) => unawaited(_send(text)),
  );
}

/// A conversation with nothing in it yet: the mark, a question, and a few
/// things to try.
class _NewChatWelcome extends StatelessWidget {
  const _NewChatWelcome({required this.onSuggestion});

  final ValueChanged<String>? onSuggestion;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, box) => ListView(
      key: const Key('new-chat-welcome'),
      padding: EdgeInsets.fromLTRB(20, box.maxHeight > 600 ? 48 : 16, 20, 24),
      children: [
        Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 560),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // The orb lands with a ripple, the question sharpens into
                // focus, then the suggestions rise in.
                const Center(child: _WelcomeOrb()),
                const SizedBox(height: 22),
                BlurIn(
                  delay: const Duration(milliseconds: 180),
                  child: Text(
                    'What do you need?',
                    textAlign: TextAlign.center,
                    style: JarvisType.displayOf(context).copyWith(fontSize: 34),
                  ),
                ),
                const SizedBox(height: 22),
                if (onSuggestion != null)
                  FadeSlideIn(
                    index: 8,
                    offset: 16,
                    child: SuggestionChips(onSelected: onSuggestion),
                  ),
              ],
            ),
          ),
        ),
      ],
    ),
  );
}

class _WelcomeOrb extends StatefulWidget {
  const _WelcomeOrb();

  @override
  State<_WelcomeOrb> createState() => _WelcomeOrbState();
}

class _WelcomeOrbState extends State<_WelcomeOrb> {
  int _ripple = 0;

  @override
  void initState() {
    super.initState();
    // Ripple once the orb has landed.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) setState(() => _ripple++);
    });
  }

  @override
  Widget build(BuildContext context) => Shockwave(
    trigger: _ripple,
    size: 56,
    child: const PopIn(from: .3, child: JarvisOrb(size: 56)),
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
        color: connected
            ? JarvisColors.of(context).success
            : JarvisColors.of(context).outlineStrong,
        shape: BoxShape.circle,
      ),
    ),
  );
}

/// Top-bar bell that opens notifications and shows how many are unread.
class _NotificationBell extends StatelessWidget {
  const _NotificationBell({required this.unread, required this.onPressed});

  final int unread;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final label = unread > 0
        ? 'Notifications, $unread unread'
        : 'Notifications';
    return Stack(
      clipBehavior: Clip.none,
      children: [
        CircleIconButton(
          bare: true,
          icon: unread > 0
              ? PhosphorIconsRegular.bellRinging
              : PhosphorIconsRegular.bell,
          tooltip: label,
          onPressed: onPressed,
        ),
        if (unread > 0)
          Positioned(
            top: 3,
            right: 2,
            child: IgnorePointer(
              // The badge pops again each time the count changes.
              child: PopIn(
                key: ValueKey(unread),
                from: .5,
                child: Container(
                  constraints: const BoxConstraints(minWidth: 18),
                  padding: const EdgeInsets.symmetric(
                    horizontal: 5,
                    vertical: 1,
                  ),
                  decoration: BoxDecoration(
                    color: colors.danger,
                    borderRadius: BorderRadius.circular(20),
                    border: Border.all(color: colors.surface, width: 2),
                  ),
                  child: Text(
                    unread > 99 ? '99+' : '$unread',
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 10.5,
                      height: 1.3,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
              ),
            ),
          ),
      ],
    );
  }
}

/// "Jarvis needs your approval · Review" above the composer. It only points
/// to the card; the decision itself stays on the card, next to its details.
class _ApprovalDock extends StatelessWidget {
  const _ApprovalDock({required this.onReview});

  final VoidCallback onReview;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Material(
      color: colors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: BorderSide(color: colors.warning.withValues(alpha: .35)),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(16),
        onTap: onReview,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(14, 10, 8, 10),
          child: Row(
            children: [
              Icon(
                PhosphorIconsRegular.shieldCheck,
                size: 18,
                color: colors.warning,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  'Jarvis needs your approval',
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: colors.ink,
                  ),
                ),
              ),
              TextButton.icon(
                onPressed: onReview,
                iconAlignment: IconAlignment.end,
                icon: const Icon(PhosphorIconsRegular.caretDown, size: 15),
                label: const Text('Review'),
                style: TextButton.styleFrom(
                  foregroundColor: colors.accent,
                  visualDensity: VisualDensity.compact,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
