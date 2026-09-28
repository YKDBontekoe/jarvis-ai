part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenUi on _ChatScreenController {
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
  );

  void _dismissKeyboard() => FocusManager.instance.primaryFocus?.unfocus();

  void _fromSidebar(VoidCallback action) {
    _dismissKeyboard();
    final scaffold = _scaffoldKey.currentState;
    if (scaffold?.isDrawerOpen ?? false) scaffold!.closeDrawer();
    action();
  }

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
                  onPressed: () {
                    _dismissKeyboard();
                    _scaffoldKey.currentState?.openDrawer();
                  },
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

  Widget _signInScreen() {
    final busy = _authBusy || _signingOut;
    return Scaffold(
      body: Stack(
        children: [
          const Positioned.fill(child: _AmbientBackdrop()),
          SafeArea(
            child: Center(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(28),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 400),
                  child: AutofillGroup(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const JarvisOrb(size: 96, semanticLabel: 'Jarvis'),
                        const SizedBox(height: 32),
                        Text(
                          _creatingAccount
                              ? 'Create your account'
                              : 'Sign in to Jarvis',
                          textAlign: TextAlign.center,
                          style: JarvisType.serif.copyWith(fontSize: 42),
                        ),
                        const SizedBox(height: 10),
                        Text(
                          'Your private assistant for conversations, tasks, memory, and voice.',
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodyLarge
                              ?.copyWith(color: JarvisColors.inkSoft),
                        ),
                        const SizedBox(height: 32),
                        TextField(
                          controller: _email,
                          enabled: !busy,
                          keyboardType: TextInputType.emailAddress,
                          textInputAction: TextInputAction.next,
                          autofillHints: const [AutofillHints.email],
                          autocorrect: false,
                          enableSuggestions: false,
                          decoration: const InputDecoration(labelText: 'Email'),
                        ),
                        const SizedBox(height: 12),
                        TextField(
                          controller: _password,
                          enabled: !busy,
                          obscureText: _obscurePassword,
                          textInputAction: TextInputAction.done,
                          autofillHints: [
                            _creatingAccount
                                ? AutofillHints.newPassword
                                : AutofillHints.password,
                          ],
                          onSubmitted: busy
                              ? null
                              : (_) => unawaited(_signIn()),
                          decoration: InputDecoration(
                            labelText: 'Password',
                            suffixIcon: IconButton(
                              tooltip: _obscurePassword
                                  ? 'Show password'
                                  : 'Hide password',
                              onPressed: () => setState(
                                () => _obscurePassword = !_obscurePassword,
                              ),
                              icon: Icon(
                                _obscurePassword
                                    ? PhosphorIconsRegular.eye
                                    : PhosphorIconsRegular.eyeSlash,
                              ),
                            ),
                          ),
                        ),
                        const SizedBox(height: 8),
                        const Align(
                          alignment: Alignment.centerLeft,
                          child: Text(
                            'Use 8 or more characters with upper and lower case letters and a number.',
                            style: TextStyle(
                              fontSize: 12.5,
                              color: JarvisColors.muted,
                            ),
                          ),
                        ),
                        const SizedBox(height: 20),
                        SizedBox(
                          width: double.infinity,
                          child: FilledButton.icon(
                            onPressed: busy ? null : () => unawaited(_signIn()),
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
                                : Icon(
                                    _creatingAccount
                                        ? PhosphorIconsRegular.user
                                        : PhosphorIconsRegular.signIn,
                                  ),
                            label: Text(
                              _creatingAccount ? 'Create account' : 'Sign in',
                            ),
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextButton(
                          onPressed: busy
                              ? null
                              : () => setState(() {
                                  _creatingAccount = !_creatingAccount;
                                  _error = null;
                                }),
                          child: Text(
                            _creatingAccount
                                ? 'Already have an account? Sign in'
                                : 'Need an account? Create one',
                          ),
                        ),
                        const SizedBox(height: 8),
                        const Text(
                          'Your password is checked by Jarvis.',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            fontSize: 12.5,
                            color: JarvisColors.muted,
                          ),
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
          ),
        ],
      ),
    );
  }

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
        if (_liveSurface case final live? when surfaceAwaitsReply(live))
          LayoutBuilder(
            builder: (context, constraints) {
              final height = constraints.maxHeight.isFinite
                  ? constraints.maxHeight
                  : 640.0;
              return ConstrainedBox(
                constraints: BoxConstraints(
                  maxWidth: 788,
                  maxHeight: math.min(360, height * 0.42),
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
              );
            },
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
      onRate: entry.id == null || _conversationId == null
          ? null
          : (rating) => unawaited(_rate(entry, rating)),
    ),
    ToolRunEntry() => ToolRunView(run: entry),
    ApprovalEntry() => ApprovalCard(
      approval: entry,
      onDecide: (approved) => unawaited(_decide(entry, approved)),
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
    onTalk:
        _conversationId == null || _busy || _hasPendingApproval || !_connected
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

/// Soft, blurred colour fields behind full-screen moments (sign-in).
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
