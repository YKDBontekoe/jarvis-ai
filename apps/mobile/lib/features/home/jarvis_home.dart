import 'dart:async';

import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import '../../json_maps.dart';
import '../chat/mcp_setup.dart';
import '../chat/tool_catalog.dart' show humanizeToolName;
import '../chats/chat_list.dart';
import '../settings/codex_sign_in_card.dart';
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
  int _generation = 0;

  DateTime get _now => widget.clock?.call() ?? DateTime.now();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.layout.addListener(_layoutChanged);
    widget.chats.addListener(_rebuild);
    _tick = Timer.periodic(const Duration(seconds: 30), (_) => _rebuild());
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
      subtitle: unread > 0
          ? [
              for (final item in chats.whatsApp)
                if (item.unread > 0) item.title,
            ].take(3).join(', ')
          : items.first.title,
      attention: unread > 0,
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
                    builder: (context, _) => TileGrid(
                      layout: widget.layout.layout,
                      data: _tileData,
                      editing: _editing,
                      onOpen: (spec) => widget.onOpen(spec.destination),
                      onRowTap: (spec, row) {
                        final target = row.target;
                        if (target != null && target.startsWith('chat:')) {
                          widget.onOpenChat(target.substring(5));
                        } else {
                          widget.onOpen(spec.destination);
                        }
                      },
                      onEdit: () => setState(() => _editing = true),
                      onRemove: (item) => widget.layout.unpin(item.id),
                      onResize: (item) => widget.layout.cycleSize(item.id),
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
                            borderRadius: BorderRadius.circular(20),
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
        borderRadius: BorderRadius.circular(18),
        child: InkWell(
          key: const Key('home-approvals'),
          borderRadius: BorderRadius.circular(18),
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
