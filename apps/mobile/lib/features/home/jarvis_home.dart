import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import '../../json_maps.dart';
import '../chat/mcp_setup.dart';
import '../chat/tool_catalog.dart' show humanizeToolName;
import '../chats/chat_list.dart';
import '../settings/codex_sign_in_card.dart';
import '../tiles/tile_actions.dart';
import '../tiles/tile_card.dart' show tileRadius;
import '../tiles/tile_controller.dart';
import '../tiles/tile_grid.dart';
import '../tiles/tile_models.dart';
import '../tiles/tile_registry.dart';
import '../whatsapp/whatsapp_models.dart';
import 'clock_header.dart';
import 'get_started_card.dart';
import 'next_up.dart';

/// Home: the next thing on your day as a clock, then the tiles you chose.
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
    required this.onSettings,
    this.onSuggestion,
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
  final VoidCallback onSettings;
  final ValueChanged<String>? onSuggestion;

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
  bool _editing = false;
  Timer? _tick;
  int _ticks = 0;
  int _generation = 0;

  /// Items an action is running on, so their rows can show it.
  final Set<String> _pending = {};

  DateTime get _now => widget.clock?.call() ?? DateTime.now();

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
      });
    } else if (!oldWidget.ready ||
        oldWidget.refreshRevision != widget.refreshRevision) {
      unawaited(_refresh());
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && widget.ready) {
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
    widget.source.invalidate();
    final briefing = await widget.source.briefing();
    if (!mounted || generation != _generation) return;
    setState(() => _briefing = briefing);
    await Future.wait([
      for (final item in widget.layout.layout)
        if (tileSpecFor(item.id) case final spec? when spec.load != null)
          _loadTile(spec, generation),
    ]);
  }

  /// What the Chats tile shows, straight from the shared chat list.
  TileData _chatsData() {
    final chats = widget.chats;
    final items = chats.all.take(8).toList();
    final unread = chats.unreadCount;
    if (items.isEmpty) return const TileData(subtitle: 'No conversations yet');
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
    );
  }

  Map<String, TileData?> get _tileData => {..._data, 'chats': _chatsData()};

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
    return RefreshIndicator(
      onRefresh: _refresh,
      child: ListView(
        key: const Key('home-list'),
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 28),
        children: [
          Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 640),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ClockHeader(
                    now: now,
                    next: upcoming.isEmpty ? null : upcoming.first,
                    editing: _editing,
                    onEdit: () => setState(() => _editing = true),
                    onDone: () => setState(() => _editing = false),
                    onSettings: widget.onSettings,
                    onOpen: () => widget.onOpen('today'),
                    emptyHint: calendarOff && widget.onSuggestion != null
                        ? 'Connect a calendar'
                        : null,
                    onEmptyHint: () =>
                        widget.onSuggestion?.call(mcpCalendarPrompt),
                  ),
                  const SizedBox(height: 22),
                  if (!_editing && approvals.isNotEmpty)
                    _ApprovalsBanner(
                      approvals: approvals,
                      onTap: () => widget.onOpen('approvals'),
                    ),
                  if (widget.ready && _briefing != null) ...[
                    CodexSignInCard(http: widget.source.http),
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
                      child: Text(
                        'Hold and drag to move. Use the corner button to change size.',
                        key: const Key('home-edit-hint'),
                        style: TextStyle(
                          fontSize: 12.5,
                          color: JarvisColors.of(context).inkSoft,
                        ),
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
  }
}

/// Pending approvals stay in view on Home: Jarvis is waiting on the person.
class _ApprovalsBanner extends StatelessWidget {
  const _ApprovalsBanner({required this.approvals, required this.onTap});

  final List<Map<String, dynamic>> approvals;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final count = approvals.length;
    final names = approvals
        .take(3)
        .map((item) {
          final name = humanizeToolName(asJsonString(item['toolName']) ?? '');
          return name[0].toUpperCase() + name.substring(1);
        })
        .join(' · ');
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Material(
        color: colors.accentSoft,
        borderRadius: BorderRadius.circular(tileRadius),
        child: InkWell(
          key: const Key('home-approvals'),
          borderRadius: BorderRadius.circular(tileRadius),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(14, 12, 12, 12),
            child: Row(
              children: [
                Icon(
                  PhosphorIconsRegular.shieldCheck,
                  size: 22,
                  color: colors.accent,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '$count ${count == 1 ? 'approval' : 'approvals'} waiting',
                        style: TextStyle(
                          fontSize: 14.5,
                          fontWeight: FontWeight.w600,
                          color: colors.ink,
                        ),
                      ),
                      if (names.isNotEmpty)
                        Text(
                          names,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 12.5,
                            color: colors.inkSoft,
                          ),
                        ),
                    ],
                  ),
                ),
                Text(
                  'Review',
                  style: TextStyle(
                    fontSize: 13.5,
                    fontWeight: FontWeight.w600,
                    color: colors.accent,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
