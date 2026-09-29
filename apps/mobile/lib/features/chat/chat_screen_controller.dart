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
  List<ConversationSourceChip> _attachedSources = [];

  bool get _hasMessages => _entries.any((entry) => entry is MessageEntry);

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
  Future<void> _openConversation(String conversationId, {bool showHome = false});
  Future<List<ApprovalEntry>?> _loadConversationApprovals(
    String conversationId,
  );
  Future<void> _chooseConversation();
  Future<void> _createAndOpenConversation();
  Future<void> _switchConversationProfile();
  void _openUtility(String destination);
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
  Future<void> _retryConnection();
  Future<void> _loadRecent();
  void _openSettings();
  void _startNewChat();

  Future<void> _connectRealtime([int? generation]);

  Future<void> _enablePush();
  void _handlePushPayload(Map<String, dynamic> data);
  Future<void> _openSearch(BuildContext context);
  Future<void> _openSearchRouteFromNotification(Map<String, dynamic> data);

  Future<bool> _send([String? text]);
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
  void _completeAssistant(String content, {String? id, List<MessageCitation>? citations});
  void _toolEvent(String tool, {bool? success});
  void _settleSubmittingApprovals({ApprovalStatus? fallback});
  void _addApprovals(Iterable<ApprovalEntry> approvals);
  Future<void> _syncConversationApprovals();

  Future<void> _toggleVoice();
  Future<void> _toggleVoiceMute();
  Future<void> _stopVoice();

  void _scrollToBottom({bool jump = false});
  void _dismissKeyboard();
  Widget _settingsBody();

  List<ConversationSourceChip> get _sourceChips;
  Future<void> _loadConversationSources(String conversationId);
  Future<void> _detachSource(ConversationSourceChip source);
  Future<void> _pickConversationSources();
  Future<void> _openCitation(MessageCitation citation);
}
