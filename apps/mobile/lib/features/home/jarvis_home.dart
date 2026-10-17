import 'dart:async';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../../json_maps.dart';
import '../chat/mcp_setup.dart';
import '../chats/chat_list.dart';
import '../entities/entity_ref.dart' show entityDestinationPrefix;
import '../settings/codex_sign_in_card.dart';
import '../tiles/tile_actions.dart';
import '../tiles/tile_card.dart' show tileRadius;
import '../tiles/app_tiles.dart';
import '../tiles/tile_controller.dart';
import '../tiles/tile_grid.dart';
import '../tiles/tile_models.dart';
import '../tiles/tile_registry.dart';
import '../whatsapp/catch_up_card.dart';
import '../whatsapp/whatsapp_models.dart';
import 'ask_bar.dart';
import 'brief_card.dart';
import 'clock_header.dart';
import 'day_one_card.dart';
import 'evening_card.dart';
import 'home_context.dart';
import 'noticed_card.dart';
import 'right_now_card.dart';
import 'home_insights.dart';
import 'home_widget_sync.dart';
import 'needs_you_card.dart';
import 'get_started_card.dart';
import 'next_up.dart' hide greetingFor;
import '../../l10n/l10n.dart';

/// Replaces Home's wall clock when no [JarvisHome.clock] is given
/// (screenshots of the evening).
@visibleForTesting
DateTime Function()? debugHomeClock;

/// Home: the next thing on your day, then the content and shortcuts you chose.
class JarvisHome extends StatefulWidget {
  const JarvisHome({
    required this.source,
    required this.layout,
    required this.chats,
    required this.ready,
    required this.refreshRevision,
    required this.onOpen,
    required this.onOpenChat,
    required this.onAddTile,
    this.onSuggestion,
    this.onReplyWith,
    this.jarvisBusy = false,
    this.clock,
    super.key,
  });

  final TileDataSource source;
  final TileLayoutController layout;
  final ChatList chats;

  /// False while there is no connection to Jarvis; nothing is requested then.
  final bool ready;
  final int refreshRevision;

  /// Opens a destination: a page name such as `tasks`, `chats`, `voice` or `settings`.
  final ValueChanged<String> onOpen;

  /// Opens a chat from the Chats tile, by its [ChatListItem.key].
  final ValueChanged<String> onOpenChat;
  final VoidCallback onAddTile;
  final ValueChanged<String>? onSuggestion;

  /// Opens a WhatsApp chat, by its key, with a quick reply in the composer.
  final void Function(String key, String draft)? onReplyWith;

  /// Jarvis is writing a reply, possibly in a conversation that is not open.
  final bool jarvisBusy;

  /// Replaces the wall clock in tests.
  final DateTime Function()? clock;

  @override
  State<JarvisHome> createState() => _JarvisHomeState();
}

class _JarvisHomeState extends State<JarvisHome> with WidgetsBindingObserver {
  late final Map<String, TileData?> _data = Map.of(widget.source.cache);
  late Map<String, dynamic>? _briefing = widget.source.lastBriefing;
  late String? _preferredName = widget.source.lastPreferredName;
  late HomeContext? _context = widget.source.lastContext;

  /// Cards put away here, hidden before the server confirms it.
  final Set<String> _gone = {};
  bool _editing = false;
  Timer? _tick;
  int _ticks = 0;
  int _generation = 0;

  /// Time since Home last fetched everything. Coming back to the app sooner
  /// than [_resumeRefreshGap] keeps what is shown instead of fetching again:
  /// flicking between apps used to stack full reloads.
  final _sinceRefresh = Stopwatch();
  static const _resumeRefreshGap = Duration(seconds: 30);

  /// Items an action is running on, so their rows can show it.
  final Set<String> _pending = {};

  /// Needs you rows whose server call failed, so the card brings them back.
  final Set<String> _failed = {};

  /// Drives the parallax glow, the receding greeting and the compact header.
  final _scroll = ScrollController();

  /// The Ask bar's text, so a follow-up on the brief can start it.
  final _ask = TextEditingController();

  /// The brief dismissed here, hidden before the server confirms it.
  String? _briefDismissed;

  DateTime get _now =>
      widget.clock?.call() ?? debugHomeClock?.call() ?? DateTime.now();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.layout.addListener(_layoutChanged);
    widget.chats.addListener(_rebuild);
    _tick = Timer.periodic(const Duration(seconds: 30), (_) {
      _ticks++;
      // The clock and countdowns follow every tick; the data every other one.
      if (_ticks.isEven && widget.ready && !_editing && _pending.isEmpty) {
        unawaited(_refresh());
      } else {
        _rebuild();
      }
    });
    if (widget.ready) unawaited(_refresh());
  }

  @override
  void didUpdateWidget(JarvisHome oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.layout != widget.layout) {
      oldWidget.layout.removeListener(_layoutChanged);
      widget.layout.addListener(_layoutChanged);
    }
    if (oldWidget.chats != widget.chats) {
      oldWidget.chats.removeListener(_rebuild);
      widget.chats.addListener(_rebuild);
    }
    if (!widget.ready) {
      _generation++;
      setState(() {
        _data.clear();
        _briefing = null;
        _preferredName = null;
        _context = null;
      });
    } else if (!oldWidget.ready ||
        oldWidget.refreshRevision != widget.refreshRevision) {
      unawaited(_refresh());
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed || !widget.ready) return;
    if (_sinceRefresh.isRunning && _sinceRefresh.elapsed < _resumeRefreshGap) {
      _rebuild();
    } else {
      unawaited(_refresh());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    widget.layout.removeListener(_layoutChanged);
    widget.chats.removeListener(_rebuild);
    _tick?.cancel();
    _generation++;
    _scroll.dispose();
    _ask.dispose();
    super.dispose();
  }

  void _rebuild() {
    if (mounted) setState(() {});
  }

  void _layoutChanged() {
    if (!mounted) return;
    setState(() {});
    // A tile that was just added loads its data now.
    if (widget.ready) {
      for (final item in widget.layout.layout) {
        final spec = tileSpecFor(item.id);
        if (spec?.load != null && !_data.containsKey(item.id)) {
          unawaited(_loadTile(spec!, _generation));
        }
      }
    }
  }

  Future<void> _loadTile(TileSpec spec, int generation) async {
    final data = await widget.source.load(spec);
    if (!mounted || generation != _generation) return;
    setState(() => _data[spec.id] = data);
  }

  Future<void> _refresh() async {
    if (!widget.ready) return;
    final generation = ++_generation;
    _sinceRefresh
      ..reset()
      ..start();
    widget.source.invalidate();
    unawaited(_loadPreferredName(generation));
    unawaited(_loadContext(generation));
    final briefing = await widget.source.briefing();
    if (!mounted || generation != _generation) return;
    setState(() => _briefing = briefing);
    _syncWidget();
    await Future.wait([
      for (final item in widget.layout.layout)
        if (tileSpecFor(item.id) case final spec? when spec.load != null)
          _loadTile(spec, generation),
    ]);
  }

  Future<void> _loadContext(int generation) async {
    final context = await widget.source.context();
    if (!mounted || generation != _generation || context == null) return;
    setState(() => _context = context);
    widget.chats.setPeople(context.people);
    _syncWidget();
  }

  /// Hands the iOS widget what Home now shows.
  void _syncWidget() {
    final now = _now;
    unawaited(
      HomeWidgetSync.instance.push(
        HomeWidgetPayload.build(
          now: now,
          context: _context,
          needsYou: needsYouItems(
            now: now,
            briefing: _briefing,
            chats: widget.chats,
          ),
        ),
      ),
    );
  }

  /// Fetches the context again after something on it changed.
  Future<void> _reloadContext() async {
    widget.source.invalidate();
    await _loadContext(_generation);
  }

  HomeHero? get _hero {
    final hero = _context?.hero;
    return hero == null || _gone.contains(hero.id) ? null : hero;
  }

  void _dismissCard(String id, String kind, {bool mute = false}) {
    setState(() => _gone.add(id));
    unawaited(widget.source.dismiss(id, kind, mute: mute));
  }

  Future<void> _heroAction(HomeHero hero, HeroAction action) async {
    final suggest = widget.onSuggestion;
    switch (action) {
      case HeroAction.done || HeroAction.snooze:
        final id = hero.reminderId;
        if (id == null) return;
        setState(() => _gone.add(hero.id));
        await _remind(id, action == HeroAction.done ? 'done' : 'snooze');
        await _reloadContext();
      case HeroAction.open:
        widget.onOpen('today');
      case HeroAction.brief:
        await _readBrief();
      case HeroAction.plan:
        suggest?.call(switch (hero.kind) {
          'brief' => 'About my morning brief: what should I focus on first?',
          'wind_down' => 'Help me plan tomorrow.',
          _ => 'Help me prepare for ${hero.title}.',
        });
      case HeroAction.ok:
        _dismissCard(hero.id, hero.kind);
    }
  }

  /// Opens today's brief in full and marks it read.
  Future<void> _readBrief() async {
    final note = _briefNote ?? jsonObject(_briefing?['latestBriefing']);
    if (note == null) return;
    final id = asJsonString(note['id']);
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      useSafeArea: true,
      isScrollControlled: true,
      builder: (sheet) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: .6,
        maxChildSize: .92,
        builder: (sheet, scroll) => ListView(
          key: const Key('home-brief-sheet'),
          controller: scroll,
          padding: const EdgeInsets.fromLTRB(22, 0, 22, 28),
          children: [
            Text(
              asJsonString(note['title']) ?? 'Morning brief',
              style: JarvisType.displayOf(sheet)
                  .copyWith(fontSize: 26, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 14),
            Text(
              plainBriefing(asJsonString(note['body']) ?? ''),
              style: TextStyle(
                fontSize: 16,
                height: 1.55,
                color: JarvisColors.of(sheet).ink,
              ),
            ),
          ],
        ),
      ),
    );
    if (id != null && mounted) {
      await _dismissBrief(id);
      await _reloadContext();
    }
  }

  Future<void> _noticed(HomeInsight insight) async {
    final suggest = widget.onSuggestion;
    switch (insight.kind) {
      case 'routine':
        final id = insight.sourceId;
        if (id == null) return;
        setState(() => _gone.add(insight.id));
        String message;
        try {
          await widget.source.http.post<dynamic>(
            '/api/v1/routines/suggestions/$id/accept',
          );
          message = 'Saved as a draft routine. Turn it on in Routines.';
        } catch (_) {
          if (mounted) setState(() => _gone.remove(insight.id));
          message = 'Could not set up that routine';
        }
        if (!mounted) return;
        ScaffoldMessenger.maybeOf(context)
          ?..hideCurrentSnackBar()
          ..showSnackBar(SnackBar(content: Text(message)));
      case 'drift':
        for (final item in widget.chats.whatsApp) {
          if (widget.chats.personFor(item)?.personId == insight.personId) {
            widget.onOpenChat(item.key);
            return;
          }
        }
        suggest?.call(
          'Help me write a short message to check in: ${insight.title}.',
        );
      default:
        suggest?.call('Help me write a birthday message: ${insight.title}.');
    }
  }

  /// What Jarvis can learn from in the first days, and what is connected.
  List<DayOneStep> _dayOneSteps() {
    final calendar = jsonObject(_briefing?['calendar']);
    final suggest = widget.onSuggestion;
    return [
      DayOneStep(
        id: 'calendar',
        label: 'Connect your calendar',
        unlocks: 'Right now, countdowns and your day line',
        icon: PhosphorIconsRegular.calendarBlank,
        done: asJsonBool(calendar?['connected']),
        onTap: () => suggest?.call(mcpCalendarPrompt),
      ),
      DayOneStep(
        id: 'whatsapp',
        label: 'Read along with WhatsApp',
        unlocks: 'Who is waiting on you, and quick replies',
        icon: PhosphorIconsRegular.whatsappLogo,
        done: widget.chats.whatsAppLinked,
        onTap: () => widget.onOpen('whatsapp'),
      ),
      DayOneStep(
        id: 'people',
        label: 'Tell Jarvis who matters',
        unlocks: 'Birthdays, and a nudge when you drift apart',
        icon: PhosphorIconsRegular.users,
        done: _context?.people.isNotEmpty ?? false,
        onTap: () => widget.onOpen('people'),
      ),
    ];
  }

  Future<void> _loadPreferredName(int generation) async {
    final name = await widget.source.preferredName();
    if (!mounted || generation != _generation) return;
    setState(() => _preferredName = name);
  }

  /// What the Chats tile shows, straight from the shared chat list.
  TileData _chatsData() {
    final chats = widget.chats;
    final items = chats.all.take(8).toList();
    final unread = chats.unreadCount;
    if (items.isEmpty) return const TileData(subtitle: 'No conversations yet');
    (String, ChatSource, String?) identity(ChatListItem item) =>
        (item.title, item.source, item.isJarvis ? item.profile : item.account);
    final identities = <(String, ChatSource, String?), int>{};
    for (final item in items) {
      identities.update(
        identity(item),
        (count) => count + 1,
        ifAbsent: () => 1,
      );
    }
    String contextLine(ChatListItem item) {
      final profile = item.profile?.trim();
      return [
        item.isJarvis ? 'Jarvis' : 'WhatsApp',
        if (item.isJarvis && profile?.isNotEmpty == true && profile != 'Jarvis')
          profile!,
        if (!item.isJarvis && item.account?.isNotEmpty == true) item.account!,
        if (identities[identity(item)]! > 1 &&
            item.isJarvis &&
            item.startedAt != null)
          '${item.startedAt!.day}/${item.startedAt!.month} · ${clockTime(item.startedAt!)}',
      ].join(' · ');
    }

    return TileData(
      stat: unread > 0 ? '$unread' : null,
      unit: unread > 0 ? 'unread' : null,
      subtitle: widget.jarvisBusy
          ? 'Jarvis is replying…'
          : unread > 0
          ? [
              for (final item in chats.whatsApp)
                if (item.unread > 0) item.title,
            ].take(3).join(', ')
          : items.first.title,
      visual: widget.jarvisBusy ? TileVisual.waveform : TileVisual.none,
      attention: unread > 0 || widget.jarvisBusy,
      rows: [
        for (final item in items)
          TileRow(
            item.title,
            meta: whatsAppListTime(item.time, now: _now),
            attention: item.unread > 0,
            target: 'chat:${item.key}',
          ),
      ],
      chats: [
        for (final item in items)
          TileChat(
            title: item.title,
            context: contextLine(item),
            preview: item.preview,
            time: whatsAppListTime(item.time, now: _now),
            unread: item.unread,
            isJarvis: item.isJarvis,
            group: item.group,
            target: 'chat:${item.key}',
          ),
      ],
    );
  }

  Map<String, TileData?> get _tileData => {..._data, 'chats': _chatsData()};

  /// Today's brief, unless it was read or dismissed.
  Map<String, dynamic>? get _briefNote {
    final note = jsonObject(_briefing?['latestBriefing']);
    if (!BriefCard.shows(note)) return null;
    if (asJsonString(note!['id']) == _briefDismissed) return null;
    return note;
  }

  bool _isLoading(String id) =>
      widget.ready && tileSpecFor(id)?.load != null && !_data.containsKey(id);

  /// Runs a quick action from a tile: the tile answers at once, the server
  /// confirms, and the real data replaces the guess.
  Future<void> _runAction(
    TileSpec spec,
    TileAction action,
    String? itemId,
  ) async {
    if (action.id == 'review') {
      widget.onOpen(spec.destination);
      return;
    }
    if (itemId == null || _pending.contains(itemId)) return;
    unawaited(HapticFeedback.selectionClick());
    final before = _data[spec.id];
    setState(() {
      _pending.add(itemId);
      if (before != null) {
        _data[spec.id] = applyTileAction(spec.id, before, action.id, itemId);
      }
    });
    final outcome = await widget.source.act(spec.id, action.id, itemId);
    if (!mounted) return;
    setState(() => _pending.remove(itemId));
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(outcome.message)));
    if (!outcome.ok && before != null) {
      setState(() => _data[spec.id] = before);
    }
    // What the briefing says (the clock, the approvals banner, Today) changed
    // too, so fetch it again along with the tile.
    widget.source.invalidate();
    final briefing = await widget.source.briefing();
    if (!mounted) return;
    setState(() => _briefing = briefing);
    final generation = _generation;
    for (final id in {spec.id, 'today'}) {
      final other = tileSpecFor(id);
      if (other?.load != null && widget.layout.contains(id)) {
        unawaited(_loadTile(other!, generation));
      }
    }
  }

  /// A button on a Needs you row: reminders are handled right here, the rest
  /// open where they live or go to Jarvis.
  Future<void> _needsYou(NeedsYouItem item, NeedsYouAction action) async {
    switch ((item.kind, action)) {
      case (NeedsYouKind.approval, _):
        widget.onOpen('approvals');
      case (NeedsYouKind.task, _):
        widget.onOpen('tasks');
      case (NeedsYouKind.catchUp, _):
        await showCatchUpSheet(context, [
          for (final chat in item.chats)
            CatchUpDigestEntry(
              id: chat.key,
              title: chat.title,
              catchUp: chat.whatsApp!.catchUp!,
              onOpen: () => widget.onOpenChat(chat.key),
            ),
        ]);
      case (NeedsYouKind.person, NeedsYouAction.draft):
        widget.onSuggestion?.call(
          'Draft a short reply to ${item.title} on WhatsApp, in the language of our chat.',
        );
      case (NeedsYouKind.person, _):
        widget.onOpenChat(item.id);
      case (NeedsYouKind.reminder, NeedsYouAction.done) ||
          (NeedsYouKind.reminder, NeedsYouAction.snooze):
        await _remind(
          item.id,
          action == NeedsYouAction.done ? 'done' : 'snooze',
        );
      case (NeedsYouKind.reminder, _):
        widget.onOpen('reminders');
      case (NeedsYouKind.commitment, NeedsYouAction.done):
        await _fulfil(item.id);
      case (NeedsYouKind.commitment, _):
        widget.onOpen('inbox');
      case (NeedsYouKind.decision, _):
        widget.onOpen('decisions');
    }
  }

  /// Marks an accepted promise done. A failure brings its row back.
  Future<void> _fulfil(String id) async {
    if (_pending.contains(id)) return;
    setState(() {
      _pending.add(id);
      _failed.remove(id);
    });
    var ok = true;
    try {
      await widget.source.http.patch<dynamic>(
        '/api/v1/commitments/$id',
        data: {'status': 'done'},
      );
    } catch (_) {
      ok = false;
    }
    if (!mounted) return;
    setState(() {
      _pending.remove(id);
      if (ok) {
        _failed.remove(id);
      } else {
        _failed.add(id);
      }
    });
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(ok ? 'Marked as done' : 'Could not mark it as done'),
        ),
      );
    widget.source.invalidate();
    final briefing = await widget.source.briefing();
    if (mounted) setState(() => _briefing = briefing);
  }

  Future<void> _remind(String id, String action) async {
    if (_pending.contains(id)) return;
    setState(() {
      _pending.add(id);
      _failed.remove(id);
    });
    final outcome = await widget.source.act('reminders', action, id);
    if (!mounted) return;
    setState(() {
      _pending.remove(id);
      if (outcome.ok) {
        _failed.remove(id);
      } else {
        _failed.add(id);
      }
    });
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(outcome.message)));
    widget.source.invalidate();
    final briefing = await widget.source.briefing();
    if (mounted) setState(() => _briefing = briefing);
  }

  Future<void> _dismissBrief(String id) async {
    setState(() => _briefDismissed = id);
    try {
      await widget.source.http.post<dynamic>('/api/v1/notifications/$id/read');
    } catch (_) {
      // Hidden for now; it comes back on the next load if the server missed it.
    }
  }

  /// The long-press menu: other sizes, remove, or edit the whole of Home.
  Future<void> _showMenu(TileSpec spec, Offset position) async {
    unawaited(HapticFeedback.mediumImpact());
    final current = widget.layout.sizeOf(spec.id);
    final overlay =
        Overlay.of(context).context.findRenderObject()! as RenderBox;
    final choice = await showMenu<String>(
      context: context,
      position: RelativeRect.fromRect(
        position & const Size(1, 1),
        Offset.zero & overlay.size,
      ),
      items: [
        for (final size in spec.sizes)
          if (size != current)
            PopupMenuItem(
              key: Key('menu-size-${size.name}'),
              value: 'size:${size.name}',
              child: Text('Show as ${size.label.toLowerCase()}'),
            ),
        const PopupMenuItem(
          key: Key('menu-edit'),
          value: 'edit',
          child: Text('Edit Home'),
        ),
        const PopupMenuItem(
          key: Key('menu-remove'),
          value: 'remove',
          child: Text('Remove from Home'),
        ),
      ],
    );
    if (!mounted || choice == null) return;
    if (choice == 'edit') {
      setState(() => _editing = true);
    } else if (choice == 'remove') {
      widget.layout.unpin(spec.id);
    } else if (choice.startsWith('size:')) {
      final size = TileSize.parse(choice.substring(5));
      if (size != null) widget.layout.pin(spec.id, size);
    }
  }

  @override
  Widget build(BuildContext context) {
    final now = _now;
    final upcoming = upcomingItems(_briefing, now);
    final approvals = jsonMaps(_briefing?['approvals']);
    final calendar = _briefing?['calendar'];
    final calendarOff =
        calendar is Map && calendar['connected'] == false && _briefing != null;
    final hero = _hero;
    final homeContext = _context;
    final evening = now.hour >= 18;
    final tomorrow = hero?.kind == 'wind_down'
        ? const <HomeAgendaItem>[]
        : homeContext?.tomorrow ?? const <HomeAgendaItem>[];
    final noticed = [
      for (final item in homeContext?.noticed ?? const <HomeInsight>[])
        if (!_gone.contains(item.id)) item,
    ];
    final dayOne = homeContext == null ? null : _dayOneSteps();
    final showDayOne = dayOne != null && DayOneCard.shows(dayOne);
    final list = OrbRefresh(
      onRefresh: _refresh,
      child: ListView(
        key: const Key('home-list'),
        controller: _scroll,
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 28),
        children: [
          Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 640),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  HeroGlow(
                    parallax: _scroll,
                    child: ClockHeader(
                      scroll: _scroll,
                      now: now,
                      next: upcoming.isEmpty ? null : upcoming.first,
                      preferredName: _preferredName,
                      dayLine: homeContext?.dayLine,
                      lead: hero == null
                          ? null
                          : RightNowCard(
                              hero: hero,
                              now: now,
                              onAction: (action) =>
                                  unawaited(_heroAction(hero, action)),
                              onDismiss: ({required mute}) =>
                                  _dismissCard(hero.id, hero.kind, mute: mute),
                            ),
                      onOpen: () => widget.onOpen('today'),
                      emptyHint:
                          calendarOff &&
                              !showDayOne &&
                              widget.onSuggestion != null
                          ? 'Connect a calendar'
                          : null,
                      onEmptyHint: () =>
                          widget.onSuggestion?.call(mcpCalendarPrompt),
                    ),
                  ),
                  const SizedBox(height: 20),
                  if (!_editing) ...[
                    FadeSlideIn(
                      index: 1,
                      child: AskBar(
                        controller: _ask,
                        busy: widget.jarvisBusy,
                        suggestions: homeSuggestions(
                          now: now,
                          briefing: _briefing,
                          chats: widget.chats,
                        ),
                        onSubmit: widget.onSuggestion,
                        onVoice: () => widget.onOpen('voice'),
                      ),
                    ),
                    const SizedBox(height: 28),
                    if (_briefing != null)
                      FadeSlideIn(
                        index: 2,
                        child: NeedsYouCard(
                          items: needsYouItems(
                            now: now,
                            briefing: _briefing,
                            chats: widget.chats,
                          ),
                          http: widget.source.http,
                          pending: _pending,
                          failed: _failed,
                          onReplyWith: widget.onReplyWith == null
                              ? null
                              : (item, draft) =>
                                    widget.onReplyWith!(item.id, draft),
                          onSeeAll: approvals.isEmpty
                              ? null
                              : () => widget.onOpen('approvals'),
                          onAction: (item, action) =>
                              unawaited(_needsYou(item, action)),
                        ),
                      ),
                    const SizedBox(height: 20),
                    if (noticed.isNotEmpty)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 24),
                        child: FadeSlideIn(
                          index: 3,
                          child: NoticedCard(
                            insights: noticed,
                            onAction: (item) => unawaited(_noticed(item)),
                            onDismiss: (item) =>
                                _dismissCard(item.id, item.kind),
                          ),
                        ),
                      ),
                    if (evening &&
                        homeContext != null &&
                        EveningCard.shows(homeContext.recap, tomorrow))
                      Padding(
                        padding: const EdgeInsets.only(bottom: 20),
                        child: FadeSlideIn(
                          index: 4,
                          child: EveningCard(
                            recap: homeContext.recap,
                            tomorrow: tomorrow,
                            onWrite: widget.onSuggestion == null
                                ? null
                                : () => widget.onSuggestion!(
                                    'Help me write a short journal entry about today. Ask me one question at a time.',
                                  ),
                            onPlan: widget.onSuggestion == null
                                ? null
                                : () => widget.onSuggestion!(
                                    'Help me plan tomorrow.',
                                  ),
                          ),
                        ),
                      )
                    else if (_briefNote case final note?
                        when hero?.kind != 'brief')
                      Padding(
                        padding: const EdgeInsets.only(bottom: 20),
                        child: FadeSlideIn(
                          index: 3,
                          child: BriefCard(
                            note: note,
                            onDismiss: () => unawaited(
                              _dismissBrief(asJsonString(note['id']) ?? ''),
                            ),
                            onFollowUp: widget.onSuggestion == null
                                ? null
                                : () => _ask.text = 'About my morning brief: ',
                          ),
                        ),
                      ),
                  ],
                  if (widget.ready && _briefing != null) ...[
                    CodexSignInCard(http: widget.source.http),
                    if (showDayOne && !_editing)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 20),
                        child: DayOneCard(
                          steps: dayOne,
                          http: widget.source.http,
                        ),
                      )
                    else
                      GetStartedCard(
                        http: widget.source.http,
                        briefing: _briefing,
                        onSuggestion: widget.onSuggestion,
                        refreshRevision: widget.refreshRevision,
                      ),
                  ],
                  if (_editing)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: Row(
                        children: [
                          Expanded(
                            child: Text(
                              'Hold and drag to move. Use the corner button to change size.',
                              key: const Key('home-edit-hint'),
                              style: TextStyle(
                                fontSize: 12.5,
                                color: JarvisColors.of(context).inkSoft,
                              ),
                            ),
                          ),
                          const SizedBox(width: 12),
                          TextButton(
                            key: const Key('home-edit-done'),
                            onPressed: () => setState(() => _editing = false),
                            child: const Text('Done'),
                          ),
                        ],
                      ),
                    ),
                  ListenableBuilder(
                    listenable: widget.layout,
                    builder: (context, _) => !widget.layout.ready
                        ? const SizedBox(height: 240)
                        : TileGrid(
                            layout: widget.layout.layout,
                            data: _tileData,
                            editing: _editing,
                            onOpen: (spec) => widget.onOpen(spec.destination),
                            onRowTap: (spec, row) {
                              final target = row.target;
                              if (target != null &&
                                  target.startsWith('chat:')) {
                                widget.onOpenChat(target.substring(5));
                              } else if (target != null &&
                                  (target.startsWith(entityDestinationPrefix) ||
                                      target.startsWith(
                                        appDestinationPrefix,
                                      ))) {
                                widget.onOpen(target);
                              } else {
                                widget.onOpen(spec.destination);
                              }
                            },
                            onEdit: () => setState(() => _editing = true),
                            now: now,
                            isLoading: _isLoading,
                            pending: _pending,
                            onAction: _runAction,
                            onMenu: _showMenu,
                            onRemove: (item) => widget.layout.unpin(item.id),
                            onResize: (item) =>
                                widget.layout.cycleSize(item.id),
                            onReorder: widget.layout.move,
                          ),
                  ),
                  if (!_editing)
                    Padding(
                      padding: const EdgeInsets.only(top: 20),
                      child: Center(
                        child: TextButton.icon(
                          key: const Key('home-edit'),
                          onPressed: () => setState(() => _editing = true),
                          style: TextButton.styleFrom(
                            foregroundColor: JarvisColors.of(context).inkSoft,
                            minimumSize: const Size(44, 44),
                          ),
                          icon: const Icon(
                            PhosphorIconsRegular.sliders,
                            size: 16,
                          ),
                          label: const Text('Customize Home'),
                        ),
                      ),
                    ),
                  if (_editing)
                    Padding(
                      padding: const EdgeInsets.only(top: 12),
                      child: OutlinedButton.icon(
                        key: const Key('home-add-tile'),
                        onPressed: widget.onAddTile,
                        icon: const Icon(PhosphorIconsRegular.plus, size: 18),
                        label: const Text('Add tile'),
                        style: OutlinedButton.styleFrom(
                          minimumSize: const Size.fromHeight(50),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(tileRadius),
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
    return Stack(
      children: [
        list,
        _CompactHeader(
          scroll: _scroll,
          title: [
            greetingFor(now, context.l10n),
            if (_preferredName?.trim() case final name? when name.isNotEmpty)
              name,
          ].join(', '),
        ),
      ],
    );
  }
}

/// Once the greeting has scrolled away, a slim frosted bar takes its place
/// at the top; tapping it glides back up.
class _CompactHeader extends StatelessWidget {
  const _CompactHeader({required this.scroll, required this.title});

  final ScrollController scroll;
  final String title;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Positioned(
      top: 0,
      left: 0,
      right: 0,
      child: AnimatedBuilder(
        animation: scroll,
        builder: (context, child) {
          final offset = scroll.hasClients ? scroll.offset : 0.0;
          final t = ((offset - 120) / 60).clamp(0.0, 1.0);
          if (t == 0) return const SizedBox.shrink();
          return IgnorePointer(
            ignoring: t < 1,
            child: Opacity(
              opacity: t,
              child: Transform.translate(
                offset: Offset(0, -8 * (1 - t)),
                child: child,
              ),
            ),
          );
        },
        child: ClipRect(
          child: BackdropFilter(
            filter: ui.ImageFilter.blur(sigmaX: 16, sigmaY: 16),
            child: Material(
              color: colors.canvas.withValues(alpha: .78),
              child: InkWell(
                key: const Key('home-compact-header'),
                onTap: () => scroll.animateTo(
                  0,
                  duration: JarvisMotion.of(
                    context,
                    const Duration(milliseconds: 520),
                  ),
                  curve: JarvisMotion.emphasized,
                ),
                child: Container(
                  height: 48,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    border: Border(
                      bottom: BorderSide(
                        color: colors.outline.withValues(alpha: .6),
                      ),
                    ),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const JarvisOrb(size: 18, glow: false),
                      const SizedBox(width: 8),
                      Flexible(
                        child: Text(
                          title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontFamily: 'Geist',
                            fontSize: 15,
                            fontWeight: FontWeight.w600,
                            letterSpacing: -.2,
                            color: colors.ink,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
