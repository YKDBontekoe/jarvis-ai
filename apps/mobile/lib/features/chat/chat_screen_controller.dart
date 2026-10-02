part of 'chat_screen.dart';

/// Holds ChatScreen fields so feature mixins can share state without forming
/// a circular `mixin on _ChatScreenState` relationship.
abstract class _ChatScreenController extends State<ChatScreen>
    with WidgetsBindingObserver {
  late final _auth = AuthSession(enabled: !widget.skipAuthentication);
  final _http = createJarvisHttp();
  final _input = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _scroll = ScrollController();
  final _entries = <ChatEntry>[];
  String? _messageCursor;
  bool _hasOlderMessages = false;
  bool _loadingOlderMessages = false;
  HubConnection? _hub;
  Room? _voiceRoom;
  String? _conversationId;
  String? _profileId;
  String? _profileName;

  /// The project of the open conversation, when it is in one.
  String? _projectId;
  bool _profileDeleted = false;
  String? _preferredProfileId;
  String? _error;
  String? _surfaceError;
  String? _surfaceErrorFor;
  bool _connected = false;
  bool _sending = false;
  bool _signedOut = false;
  bool _restoringSession = true;
  bool _signingOut = false;
  bool _authBusy = false;
  bool _creatingAccount = false;
  bool _obscurePassword = true;
  bool _voiceActive = false;
  bool _voiceStarting = false;
  bool _voiceHandsFree = true;
  bool _voiceCaptions = true;
  String? _voiceCaption;
  String? _voiceCaptionRole;
  String _voicePhase = 'idle';
  String _voiceName = '';
  bool _voiceMuted = false;
  bool _voiceReconnecting = false;
  bool _voiceUserStop = false;
  int _voiceGeneration = 0;
  CancelToken? _runCancel;
  Timer? _catchUpTimer;
  int _catchUpGeneration = 0;
  bool _remoteQuery = false;
  bool _stopRequested = false;
  String? _pendingQueryText;
  int _selectedDestination = 0;
  int _homeRevision = 0;
  int _unreadNotifications = 0;

  // On wide screens utility pages (tasks, memory, settings…) open inside the
  // content area so the sidebar stays put; narrow screens push full-screen routes.
  bool _isWide = false;
  Widget? _utilityPane;
  String? _paneDestination;
  int _paneRevision = 0;
  BuildContext? _paneContext;
  bool _openingFromSidebar = false;
  bool _showHome = true;
  int _realtimeGeneration = 0;
  final _deltaBuffer = StringBuffer();
  Timer? _deltaTimer;

  /// Bumped when streamed text changes only the transcript, so the list
  /// rebuilds without rebuilding the whole screen.
  final _transcriptTick = ValueNotifier<int>(0);
  bool _nearBottom = true;
  int _openGeneration = 0;
  int _recentRevision = 0;
  int _initGeneration = 0;
  EventsListener<RoomEvent>? _voiceEvents;
  final _scaffoldKey = GlobalKey<ScaffoldState>();
  List<Map<String, dynamic>> _recent = [];
  List<Map<String, dynamic>> _projects = [];
  String? _pushToken;
  StreamSubscription<String>? _pushTokenSubscription;
  StreamSubscription<RemoteMessage>? _pushOpenedSubscription;
  StreamSubscription<RemoteMessage>? _pushForegroundSubscription;
  final Set<String> _shownPushNotifications = {};
  final Set<String> _handledPushActions = {};
  List<ConversationSourceChip> _attachedSources = [];

  /// Unsent text per conversation; starts in memory until device storage opens.
  ComposerDrafts _drafts = ComposerDrafts.memory();
  bool _applyingDraft = false;

  /// Photos picked for the next message, and bytes of photos already shown.
  List<PendingPhoto> _pendingPhotos = [];
  final Map<String, Future<Uint8List?>> _photoBytes = {};

  /// Messages waiting for a connection, and whether the last send joined them.
  OutboxStore _outbox = OutboxStore.memory();
  Timer? _outboxTimer;
  bool _flushingOutbox = false;
  bool _lastSendQueued = false;

  /// Bumped to move keyboard focus into the composer.
  final _composerFocus = ValueNotifier<int>(0);

  Future<void> _openDrafts() async {
    final stored = await ComposerDrafts.open();
    if (!mounted || _signedOut) return;
    _drafts = stored;
    final id = _conversationId;
    if (id != null && _input.text.isEmpty) {
      _replaceComposerText(_drafts.read(id));
    }
  }

  void _rememberDraft() {
    if (_applyingDraft) return;
    final id = _conversationId;
    if (id != null) _drafts.write(id, _input.text);
  }

  /// Sets the composer without saving it as a draft of the open conversation.
  void _replaceComposerText(String text) {
    _applyingDraft = true;
    try {
      _input.value = TextEditingValue(
        text: text,
        selection: TextSelection.collapsed(offset: text.length),
      );
    } finally {
      _applyingDraft = false;
    }
  }

  /// Shows the draft of [conversationId] after switching to it. Text typed
  /// before any conversation was open moves along into the new one.
  void _restoreDraft(String? previousId, String conversationId) {
    if (previousId == conversationId) return;
    if (previousId != null) _pendingPhotos = [];
    final draft = _drafts.read(conversationId);
    if (previousId == null && draft.isEmpty && _input.text.trim().isNotEmpty) {
      _drafts.write(conversationId, _input.text);
      return;
    }
    _replaceComposerText(draft);
  }

  bool get _hasMessages => _entries.any((entry) => entry is MessageEntry);

  /// True once streamed text for the pending reply has arrived.
  bool get _hasStreamedReply => _entries.any(
    (entry) =>
        entry is MessageEntry &&
        !entry.isUser &&
        entry.pending &&
        entry.content.isNotEmpty,
  );

  MessageEntry? get _lastMessageEntry {
    for (var index = _entries.length - 1; index >= 0; index--) {
      if (_entries[index] case final MessageEntry entry) return entry;
    }
    return null;
  }

  UiSurfaceEntry? get _liveSurface => liveSurface(_entries);

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

  Future<void> _initialize();
  Future<void> _openConversation(
    String conversationId, {
    bool showHome = false,
  });
  Future<List<ApprovalEntry>?> _loadConversationApprovals(
    String conversationId,
  );
  Future<void> _chooseConversation();
  Future<void> _createAndOpenConversation();
  Future<void> _switchConversationProfile();
  void _openUtility(String destination);
  Future<void> _refreshUnreadNotifications();
  void _closeUtilityPane();
  void _selectDestination(int index);
  Future<void> _reloadConversationEntries(
    String conversationId, [
    int? generation,
  ]);
  // Declared for part implementations; invoked from chat_screen.dart.
  // ignore: unused_element
  Future<void> _loadOlderMessages();
  Future<void> _signIn();
  Future<void> _signOut();
  Future<void> _editAsNewMessage(String text);
  Future<void> _regenerate(MessageEntry reply);
  List<MessageEntry> _queuedEntries(String conversationId);
  Future<void> _queueMessage(MessageEntry message, String conversationId);
  Future<void> _flushOutbox();
  Future<void> _cancelQueued(MessageEntry entry);
  void _showPhotoSources();
  bool get _canSummarize;
  void _showConversationSummary();
  void _removePendingPhoto(PendingPhoto photo);
  Future<Uint8List?> _loadPhoto(String fileId);
  Future<void> _retryConnection();
  Future<void> _loadRecent();
  Future<void> _createProject();
  void _openSettings();
  void _startNewChat();

  Future<void> _connectRealtime([int? generation]);

  Future<void> _enablePush();
  void _onPushOpened(RemoteMessage message);
  Future<void> _openSearch(BuildContext context);
  Future<void> _openSearchRouteFromNotification(Map<String, dynamic> data);

  Future<bool> _send([String? text, List<MessagePhoto>? photos]);
  void _upsertSurface(UiSurfaceEntry surface);
  void _upsertBrowserSession(BrowserSessionEntry session);
  void _appendBrowserStep(String sessionId, BrowserStepItem step);
  Future<void> _submitSurface(
    UiSurfaceEntry surface,
    String action,
    Map<String, String> values,
  );
  Future<void> _loadConversationSurfaces(String conversationId);
  Future<void> _rate(MessageEntry message, String rating);
  Future<void> _retry(MessageEntry message);
  Future<void> _decide(ApprovalEntry approval, bool approved);
  void _finishRemoteQuery();
  void _ensurePlaceholder();
  void _removePlaceholder();
  void _settleToolRuns();
  Future<void> _catchUpRemoteQuery(String conversationId);
  void _replaceTranscript(
    Map<String, dynamic>? details,
    List<ApprovalEntry>? approvals,
  );
  Future<void> _cancelServerRun(String conversationId);
  Future<void> _cancelActiveRun();
  Future<void> _handleDeviceInvoke(
    String invokeId,
    String capability,
    Map<Object?, Object?>? event,
  );
  void _appendDelta(String delta);
  void _completeAssistant(
    String content, {
    String? id,
    List<MessageCitation>? citations,
  });
  void _toolEvent(String tool, {bool? success});
  void _settleSubmittingApprovals({ApprovalStatus? fallback});
  void _addApprovals(Iterable<ApprovalEntry> approvals);
  Future<void> _syncConversationApprovals();

  Future<void> _toggleVoice();
  Future<void> _toggleVoiceMute();
  Future<void> _stopVoice();

  void _scrollToBottom({bool jump = false, bool force = false});
  void _showTranscript();
  // Declared for part implementations; invoked via the hub event wrapper.
  // ignore: unused_element
  void _flushDeltas();
  void _discardDeltas();
  void _dismissKeyboard();
  Widget _settingsBody();

  List<ConversationSourceChip> get _sourceChips;
  Future<void> _loadConversationSources(String conversationId);
  Future<void> _pickConversationSources();
  Future<void> _detachSource(ConversationSourceChip source);
  Future<void> _openCitation(MessageCitation citation);
}
