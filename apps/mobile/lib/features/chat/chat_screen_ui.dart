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

  /// Leaves the home view for the transcript, landing on the latest message.
  void _showTranscript() {
    setState(() => _showHome = false);
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

  Widget _sidebar({required bool wide}) => JarvisSidebar(
    conversations: _recent,
    selectedConversationId: _showHome ? null : _conversationId,
    homeSelected:
        _showHome && _selectedDestination == 0 && _utilityPane == null,
    connected: _connected,
    onHome: () => _fromSidebar(() {
      if (_hasPendingApproval) {
        if (_selectedDestination != 0) _selectDestination(0);
        _showTranscript();
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
        _showTranscript();
        return;
      }
      try {
        await _openConversation(id);
      } on DioException catch (error) {
        if (mounted) setState(() => _error = describeApiError(error));
      } catch (_) {
        if (mounted) {
          setState(() => _error = 'Could not open that conversation.');
        }
      }
    }),
    onSeeAll: () => _fromSidebar(() => unawaited(_chooseConversation())),
    onUtility: (destination) => _fromSidebar(() => _openUtility(destination)),
    onSettings: () => _fromSidebar(_openSettings),
    onJarvisSearch: () => _fromSidebar(() => unawaited(_openSearch(context))),
  );

  void _dismissKeyboard() => FocusManager.instance.primaryFocus?.unfocus();

  void _fromSidebar(VoidCallback action) {
    _dismissKeyboard();
    final scaffold = _scaffoldKey.currentState;
    if (scaffold?.isDrawerOpen ?? false) scaffold!.closeDrawer();
    // Anything chosen in the sidebar replaces whatever the content area shows.
    _closeUtilityPane();
    _openingFromSidebar = true;
    try {
      action();
    } finally {
      _openingFromSidebar = false;
    }
  }

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
        builder: (context) => SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(8, 0, 8, 12),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
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

  PreferredSizeWidget _topBar({
    required bool wide,
    required bool voice,
  }) => AppBar(
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
              onPressed: () {
                _dismissKeyboard();
                _scaffoldKey.currentState?.openDrawer();
              },
            ),
          ),
    centerTitle: true,
    title: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Flexible(
              child: Text(
                voice ? 'Voice' : 'Jarvis',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
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
        _NotificationBell(
          unread: _unreadNotifications,
          onPressed: _signedOut ? null : () => _openUtility('notifications'),
        ),
      if (!voice)
        CircleIconButton(
          icon: PhosphorIconsRegular.magnifyingGlass,
          tooltip: 'Search',
          onPressed: _signedOut ? null : () => unawaited(_openSearch(context)),
        ),
      if (!voice)
        CircleIconButton(
          icon: PhosphorIconsRegular.notePencil,
          tooltip: 'New chat',
          onPressed: _busy ? null : _startNewChat,
        ),
      const SizedBox(width: 12),
    ],
  );

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
                : Stack(
                    children: [
                      Positioned.fill(
                        child: ValueListenableBuilder<int>(
                          valueListenable: _transcriptTick,
                          builder: (context, _, _) => ListView.builder(
                            controller: _scroll,
                            keyboardDismissBehavior:
                                ScrollViewKeyboardDismissBehavior.onDrag,
                            padding: const EdgeInsets.fromLTRB(18, 16, 18, 24),
                            itemCount: _entries.length,
                            itemBuilder: (context, index) => Align(
                              alignment: Alignment.topCenter,
                              child: ConstrainedBox(
                                constraints: const BoxConstraints(
                                  maxWidth: 760,
                                ),
                                child: SizedBox(
                                  width: double.infinity,
                                  child: _entryView(_entries[index]),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ),
                      if (!_nearBottom)
                        Positioned(
                          right: 16,
                          bottom: 8,
                          child: CircleIconButton(
                            icon: PhosphorIconsRegular.caretDown,
                            tooltip: 'Jump to latest',
                            size: 44,
                            onPressed: () => _scrollToBottom(force: true),
                          ),
                        ),
                    ],
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
                ),
              ),
            ),
          ),
        ],
      ),
    ),
  );

  Widget _entryView(ChatEntry entry) => switch (entry) {
    MessageEntry() => MessageBubble(
      key: ObjectKey(entry),
      message: entry,
      onRetry: entry.failed ? () => unawaited(_retry(entry)) : null,
      onRate: entry.id == null || _conversationId == null
          ? null
          : (rating) => unawaited(_rate(entry, rating)),
      onCitationTap: (citation) => unawaited(_openCitation(citation)),
      thinkingLabel: entry.pending && entry.content.isEmpty
          ? thinkingLabel(_entries)
          : 'Thinking',
    ),
    ToolRunEntry() => ToolRunView(
      run: entry,
      onOpenTasks: () => _openUtility('tasks'),
    ),
    ApprovalEntry() => ApprovalCard(
      approval: entry,
      onDecide: (approved) => unawaited(_decide(entry, approved)),
      loadMemoryText: _loadMemoryText,
    ),
    UiSurfaceEntry() => _surfaceView(entry),
    BrowserSessionEntry() => BrowserTimelineView(session: entry),
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
  );

  Widget _settingsBody() => SettingsView(
    connected: _connected,
    onOpen: _openUtility,
    onSignOut: _auth.enabled ? () => unawaited(_signOut()) : null,
  );

  Widget _welcome() => HomeOverview(
    http: _http,
    mark: const JarvisOrb(size: 56),
    ready: _conversationId != null,
    voiceStarting: _voiceStarting,
    onTalk: _conversationId == null || _busy || _hasPendingApproval
        ? null
        : () => _selectDestination(2),
    onOpenTasks: () => _openUtility('tasks'),
    onOpenUsage: () => _openUtility('usage'),
    onOpenApprovals: () => _openUtility('approvals'),
    onOpenReminders: () => _openUtility('reminders'),
    onOpenIntegrations: () => _openUtility('integrations'),
    onOpenCoding: () => _openUtility('coding'),
    onOpenLists: () => _openUtility('lists'),
    refreshRevision: _homeRevision,
    onContinueConversation: _hasMessages ? _showTranscript : null,
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
          icon: unread > 0
              ? PhosphorIconsRegular.bellRinging
              : PhosphorIconsRegular.bell,
          tooltip: label,
          onPressed: onPressed,
        ),
        if (unread > 0)
          Positioned(
            top: -2,
            right: -2,
            child: IgnorePointer(
              child: AnimatedScale(
                scale: 1,
                duration: const Duration(milliseconds: 180),
                curve: Curves.easeOutBack,
                child: Container(
                  constraints: const BoxConstraints(minWidth: 18),
                  padding: const EdgeInsets.symmetric(
                    horizontal: 5,
                    vertical: 1,
                  ),
                  decoration: BoxDecoration(
                    color: colors.danger,
                    borderRadius: BorderRadius.circular(20),
                    border: Border.all(color: colors.canvas, width: 2),
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
