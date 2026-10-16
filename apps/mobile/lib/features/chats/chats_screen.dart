import 'dart:async';
import 'dart:ui' as ui;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../whatsapp/catch_up_card.dart';
import '../whatsapp/read_along_screen.dart';
import '../whatsapp/whatsapp_models.dart';
import 'chat_list.dart';
import 'chat_row.dart';
import 'quick_replies.dart';

/// Every conversation in one place: your chats with Jarvis and your WhatsApp.
/// "For you" leads with who is waiting on you; the other filters list chats
/// by when they last moved.
class ChatsScreen extends StatefulWidget {
  const ChatsScreen({
    required this.chats,
    required this.onOpen,
    required this.onNewChat,
    required this.onSearch,
    required this.onRefresh,
    this.onManage,
    this.onConnectWhatsApp,
    this.onAsk,
    this.onReplyWith,
    this.selectedKey,
    this.http,
    this.clock,
    super.key,
  });

  final ChatList chats;
  final ValueChanged<ChatListItem> onOpen;
  final VoidCallback? onNewChat;
  final VoidCallback onSearch;
  final Future<void> Function() onRefresh;

  /// Opens the full conversation history, to rename, pin, export or delete.
  final VoidCallback? onManage;

  /// Offered on the WhatsApp filter while no account is linked.
  final VoidCallback? onConnectWhatsApp;

  /// Asks Jarvis something about a chat, such as a summary or a draft reply.
  final ValueChanged<String>? onAsk;

  /// Opens a WhatsApp chat with a quick reply in its composer, not sent.
  final void Function(ChatListItem item, String draft)? onReplyWith;

  /// The row to mark as open (wide layout).
  final String? selectedKey;

  /// Loads WhatsApp profile pictures, pins Jarvis chats and marks WhatsApp
  /// chats read. Without it, rows keep their initials and swipes are off.
  final Dio? http;

  /// Replaces the wall clock in tests.
  final DateTime Function()? clock;

  @override
  State<ChatsScreen> createState() => _ChatsScreenState();
}

class _ChatsScreenState extends State<ChatsScreen> {
  final _query = TextEditingController();
  final _searchFocus = FocusNode();
  var _filter = ChatFilter.forYou;
  var _remindersOpen = false;

  /// When each chat last moved, so a newer message can light its row up.
  final Map<String, DateTime?> _seen = {};

  @override
  void initState() {
    super.initState();
    HardwareKeyboard.instance.addHandler(_key);
  }

  @override
  void dispose() {
    HardwareKeyboard.instance.removeHandler(_key);
    _query.dispose();
    _searchFocus.dispose();
    super.dispose();
  }

  /// ⌘K or Ctrl+K jumps to the search field.
  bool _key(KeyEvent event) {
    if (event is! KeyDownEvent ||
        event.logicalKey != LogicalKeyboardKey.keyK ||
        !(HardwareKeyboard.instance.isMetaPressed ||
            HardwareKeyboard.instance.isControlPressed) ||
        !mounted ||
        !(TickerMode.valuesOf(context).enabled)) {
      return false;
    }
    _searchFocus.requestFocus();
    _query.selection = TextSelection(
      baseOffset: 0,
      extentOffset: _query.text.length,
    );
    return true;
  }

  /// True once for a chat whose last message is newer than the one seen
  /// before; the first look at a chat never counts.
  bool _arrived(ChatListItem item) {
    final known = _seen.containsKey(item.key);
    final before = _seen[item.key];
    _seen[item.key] = item.time;
    final time = item.time;
    return known && time != null && (before == null || time.isAfter(before));
  }

  void _select(ChatFilter filter) {
    if (_filter == filter) return;
    unawaited(HapticFeedback.selectionClick());
    setState(() => _filter = filter);
  }

  Future<void> _pin(ChatListItem item) async {
    final http = widget.http;
    if (http == null) return;
    final ok = await widget.chats.setPinned(http, item, !item.pinned);
    if (!mounted) return;
    unawaited(HapticFeedback.lightImpact());
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(
            ok
                ? (item.pinned ? 'Unpinned' : 'Pinned to the top')
                : 'Could not change the pin',
          ),
        ),
      );
  }

  Future<void> _markRead(ChatListItem item) async {
    final http = widget.http;
    if (http == null) return;
    final ok = await widget.chats.markRead(http, item);
    if (!mounted || ok) return;
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(const SnackBar(content: Text('Could not mark it read')));
  }

  void _ask(String prompt) {
    final ask = widget.onAsk;
    if (ask == null) return;
    ask(prompt);
  }

  Future<void> _preview(ChatListItem item) async {
    unawaited(HapticFeedback.mediumImpact());
    final action = await showChatPreview(
      context,
      item: item,
      http: widget.http,
      canAsk: widget.onAsk != null,
      canChange: widget.http != null,
      relationship: widget.chats.personFor(item)?.relationship,
      onReply: widget.onReplyWith == null
          ? null
          : (text) => widget.onReplyWith!(item, text),
    );
    if (!mounted || action == null) return;
    switch (action) {
      case ChatPreviewAction.open:
        widget.onOpen(item);
      case ChatPreviewAction.pin:
        await _pin(item);
      case ChatPreviewAction.read:
        await _markRead(item);
      case ChatPreviewAction.summarize:
        _ask(
          item.isJarvis
              ? 'Summarize our conversation "${item.title}" in a few lines.'
              : 'Summarize my WhatsApp chat with ${item.title} and tell me what needs a reply.',
        );
      case ChatPreviewAction.draft:
        _ask(
          'Draft a short reply to ${item.title} on WhatsApp, in the language of the chat.',
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 640),
        child: ListenableBuilder(
          listenable: widget.chats,
          builder: (context, _) {
            final now = widget.clock?.call() ?? DateTime.now();
            final searching = _query.text.trim().isNotEmpty;
            final groups = widget.chats.grouped(
              _filter,
              query: _query.text,
              now: now,
            );
            final titleCounts = <(ChatSource, String), int>{};
            for (final item in widget.chats.all) {
              final key = (item.source, item.title.toLowerCase());
              titleCounts.update(key, (count) => count + 1, ifAbsent: () => 1);
            }
            bool duplicate(ChatListItem item) =>
                (titleCounts[(item.source, item.title.toLowerCase())] ?? 0) > 1;
            // One row for all catch-ups, not a card per chat. Hidden where it
            // would be in the way: Jarvis-only lists and while searching.
            final showCatchUp =
                !searching &&
                _filter != ChatFilter.jarvis &&
                _filter != ChatFilter.groups;
            final catchUp = showCatchUp
                ? _catchUps()
                : const <CatchUpDigestEntry>[];
            final catchUpTime = widget.chats.all
                .where((item) => item.whatsApp?.catchUp != null)
                .map((item) => item.time)
                .nonNulls
                .firstOrNull;
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _Header(
                  onSearch: widget.onSearch,
                  onManage: widget.onManage,
                  onNewChat: widget.onNewChat,
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 14, 16, 0),
                  child: TextField(
                    key: const Key('chats-filter-field'),
                    controller: _query,
                    focusNode: _searchFocus,
                    onChanged: (_) => setState(() {}),
                    style: TextStyle(fontSize: 16, color: colors.ink),
                    decoration: InputDecoration(
                      hintText: 'Search chats',
                      prefixIcon: Icon(
                        PhosphorIconsRegular.magnifyingGlass,
                        size: 18,
                        color: colors.muted,
                      ),
                      suffixIcon: _query.text.isEmpty
                          ? null
                          : IconButton(
                              key: const Key('chats-clear-search'),
                              tooltip: 'Clear chat search',
                              onPressed: () => setState(_query.clear),
                              icon: Icon(
                                PhosphorIconsRegular.x,
                                size: 16,
                                color: colors.inkSoft,
                              ),
                            ),
                      filled: true,
                      fillColor: colors.surfaceMuted,
                      contentPadding: const EdgeInsets.symmetric(vertical: 13),
                      border: _noBorder,
                      enabledBorder: _noBorder,
                      focusedBorder: _noBorder,
                    ),
                  ),
                ),
                FilterPills(
                  selected: _filter,
                  unread: widget.chats.unreadChats,
                  onSelected: _select,
                ),
                Expanded(
                  child: OrbRefresh(
                    onRefresh: widget.onRefresh,
                    // Filters slide toward the tab you picked.
                    child: PageSwitcher(
                      index: _filter.index,
                      child: KeyedSubtree(
                        key: ValueKey((_filter, groups.isEmpty)),
                        child: groups.isEmpty
                            ? _Empty(
                                filter: _filter,
                                searching: searching,
                                linked: widget.chats.whatsAppLinked,
                                onConnect: widget.onConnectWhatsApp,
                                onNewChat: widget.onNewChat,
                              )
                            : CustomScrollView(
                                key: const Key('chats-list'),
                                physics: const AlwaysScrollableScrollPhysics(),
                                slivers: [
                                  if (widget.chats.whatsAppError)
                                    const SliverPadding(
                                      padding: EdgeInsets.fromLTRB(
                                        16,
                                        12,
                                        16,
                                        0,
                                      ),
                                      sliver: SliverToBoxAdapter(
                                        child: InlineNotice(
                                          key: Key('chats-whatsapp-error'),
                                          message:
                                              'WhatsApp is out of reach. These are the last chats it sent.',
                                        ),
                                      ),
                                    ),
                                  if (catchUp.isNotEmpty)
                                    SliverPadding(
                                      padding: const EdgeInsets.fromLTRB(
                                        16,
                                        14,
                                        16,
                                        0,
                                      ),
                                      sliver: SliverToBoxAdapter(
                                        child: CatchUpRow(
                                          entries: catchUp,
                                          time: catchUpTime,
                                          onTap: () => unawaited(
                                            showCatchUpSheet(context, catchUp),
                                          ),
                                        ),
                                      ),
                                    ),
                                  for (final group in groups)
                                    _section(
                                      context,
                                      group,
                                      searching: searching,
                                      duplicate: duplicate,
                                      now: now,
                                    ),
                                  if (_filter == ChatFilter.forYou &&
                                      !searching)
                                    SliverToBoxAdapter(
                                      child: Padding(
                                        padding: const EdgeInsets.only(top: 16),
                                        child: Center(
                                          child: TextButton(
                                            key: const Key('chats-see-all'),
                                            onPressed: () =>
                                                _select(ChatFilter.all),
                                            child: const Text('See all chats'),
                                          ),
                                        ),
                                      ),
                                    ),
                                  const SliverToBoxAdapter(
                                    child: SizedBox(height: 28),
                                  ),
                                ],
                              ),
                      ),
                    ),
                  ),
                ),
              ],
            );
          },
        ),
      ),
    );
  }

  List<CatchUpDigestEntry> _catchUps() => [
    for (final item in widget.chats.all)
      if (item.whatsApp?.catchUp != null)
        CatchUpDigestEntry(
          id: item.key,
          title: item.title,
          catchUp: item.whatsApp!.catchUp!,
          onOpen: () => widget.onOpen(item),
          leading: item.channelId == null
              ? null
              : ChatAvatar(
                  chat: item.whatsApp,
                  http: widget.http,
                  channelId: item.channelId,
                  size: 36,
                ),
        ),
  ];

  Widget _section(
    BuildContext context,
    ChatGroup group, {
    required bool searching,
    required bool Function(ChatListItem) duplicate,
    required DateTime now,
  }) {
    final waiting = group.section == ChatSection.waiting;
    final rows = <Widget>[
      for (final (index, item) in group.items.indexed)
        _row(
          item,
          index: index,
          waiting: waiting,
          replies: waiting && index == 0,
          duplicate: duplicate(item),
          query: _query.text,
          now: now,
          divider:
              index < group.items.length - 1 ||
              group.reminders.isNotEmpty ||
              group.busyGroups.isNotEmpty,
        ),
      if (group.reminders.isNotEmpty) ...[
        _BundleRow(
          key: const Key('chats-reminders'),
          icon: PhosphorIconsRegular.bell,
          tint: JarvisColors.of(context).warning,
          background: JarvisColors.of(context).warningSoft,
          title: 'Reminders',
          count: group.reminders.length,
          detail: group.reminders.map((item) => item.title).take(2).join(', '),
          time: whatsAppListTime(group.reminders.first.time, now: now),
          open: _remindersOpen,
          onTap: () => setState(() => _remindersOpen = !_remindersOpen),
        ),
        MotionSize(
          child: _remindersOpen
              ? Column(
                  children: [
                    for (final (index, item) in group.reminders.indexed)
                      _row(
                        item,
                        index: index,
                        query: '',
                        now: now,
                        divider: index < group.reminders.length - 1,
                      ),
                  ],
                )
              : const SizedBox(width: double.infinity),
        ),
      ],
      if (group.busyGroups.isNotEmpty)
        _BusyGroups(
          groups: group.busyGroups,
          http: widget.http,
          onTap: () {
            final withCatchUp = [
              for (final item in group.busyGroups)
                if (item.whatsApp?.catchUp != null) item,
            ];
            if (withCatchUp.isEmpty) {
              _select(ChatFilter.groups);
              return;
            }
            unawaited(
              showCatchUpSheet(context, [
                for (final item in withCatchUp)
                  CatchUpDigestEntry(
                    id: item.key,
                    title: item.title,
                    catchUp: item.whatsApp!.catchUp!,
                    onOpen: () => widget.onOpen(item),
                  ),
              ]),
            );
          },
        ),
    ];
    final trailing = switch (group.section) {
      ChatSection.jarvis when _filter == ChatFilter.forYou => TextButton(
        key: const Key('chats-section-jarvis-all'),
        onPressed: () => _select(ChatFilter.jarvis),
        style: TextButton.styleFrom(
          visualDensity: VisualDensity.compact,
          textStyle: const TextStyle(
            fontFamily: 'Geist',
            fontSize: 13,
            fontWeight: FontWeight.w600,
          ),
        ),
        child: const Text('See all'),
      ),
      _ => null,
    };
    final body = SliverPadding(
      padding: const EdgeInsets.symmetric(horizontal: 8),
      sliver: SliverList.list(children: rows),
    );
    if (searching) {
      return SliverPadding(
        padding: const EdgeInsets.only(top: 8),
        sliver: body,
      );
    }
    return SliverMainAxisGroup(
      slivers: [
        PinnedHeaderSliver(
          child: _SectionHeader(group.section, trailing: trailing),
        ),
        body,
      ],
    );
  }

  Widget _row(
    ChatListItem item, {
    required int index,
    required String query,
    required DateTime now,
    bool waiting = false,
    bool duplicate = false,
    bool divider = true,
    bool replies = false,
  }) {
    final colors = JarvisColors.of(context);
    final http = widget.http;
    final reply = widget.onReplyWith;
    final canChange = widget.http != null;
    SwipeAction? start;
    if (canChange && item.isJarvis) {
      start = SwipeAction(
        label: item.pinned ? 'Unpin' : 'Pin',
        icon: PhosphorIconsRegular.pushPin,
        color: colors.accent,
        onTrigger: () => _pin(item),
      );
    } else if (canChange && item.unread > 0) {
      start = SwipeAction(
        label: 'Mark read',
        icon: PhosphorIconsRegular.check,
        color: colors.info,
        onTrigger: () => _markRead(item),
      );
    }
    final arrived = _arrived(item);
    return FadeSlideIn(
      key: ValueKey(item.key),
      index: index,
      offset: 10,
      child: _Arrival(
        key: ValueKey('${item.key}@${item.time?.microsecondsSinceEpoch}'),
        fresh: arrived,
        child: SwipeActions(
          id: item.key,
          start: start,
          bottomGap: 0,
          radius: 18,
          child: ChatRow(
            item: item,
            selected: item.key == widget.selectedKey,
            http: widget.http,
            showSource: _filter != ChatFilter.whatsapp,
            duplicate: duplicate,
            waiting: waiting,
            query: query,
            divider: divider,
            now: now,
            person: widget.chats.personFor(item),
            footer: replies && http != null && reply != null && !item.group
                ? QuickReplies(
                    http: http,
                    item: item,
                    onPick: (text) => reply(item, text),
                  )
                : null,
            onTap: () => widget.onOpen(item),
            onLongPress: () => unawaited(_preview(item)),
          ),
        ),
      ),
    );
  }
}

/// A row whose chat just got a new message: a soft accent wash that fades,
/// and one sheen across it.
class _Arrival extends StatefulWidget {
  const _Arrival({required this.fresh, required this.child, super.key});

  final bool fresh;
  final Widget child;

  @override
  State<_Arrival> createState() => _ArrivalState();
}

class _ArrivalState extends State<_Arrival>
    with SingleTickerProviderStateMixin {
  late final bool _fresh = widget.fresh;
  late final AnimationController _wash = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 900),
    value: _fresh ? 1 : 0,
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_fresh || _wash.isAnimating || _wash.value == 0) return;
    if (JarvisMotion.reduced(context)) {
      _wash.value = 0;
    } else {
      unawaited(
        _wash.animateTo(0, curve: Curves.easeOut).orCancel.catchError((_) {}),
      );
    }
  }

  @override
  void dispose() {
    _wash.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!_fresh) return widget.child;
    final colors = JarvisColors.of(context);
    final row = AnimatedBuilder(
      animation: _wash,
      child: widget.child,
      builder: (context, child) => DecoratedBox(
        decoration: BoxDecoration(
          color: colors.accentSoft.withValues(alpha: _wash.value),
          borderRadius: BorderRadius.circular(18),
        ),
        child: child,
      ),
    );
    if (JarvisMotion.reduced(context)) return row;
    return Sheen(
      delay: Duration.zero,
      strength: .22,
      borderRadius: BorderRadius.circular(18),
      child: row,
    );
  }
}

const _noBorder = OutlineInputBorder(
  borderRadius: BorderRadius.all(Radius.circular(14)),
  borderSide: BorderSide.none,
);

class _Header extends StatelessWidget {
  const _Header({
    required this.onSearch,
    required this.onManage,
    required this.onNewChat,
  });

  final VoidCallback onSearch;
  final VoidCallback? onManage;
  final VoidCallback? onNewChat;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 12, 12, 0),
      child: Row(
        children: [
          Expanded(
            // Large text shrinks the title rather than breaking the word.
            child: FittedBox(
              fit: BoxFit.scaleDown,
              alignment: AlignmentDirectional.centerStart,
              child: Text(
                'Chats',
                maxLines: 1,
                style: JarvisType.displayOf(context).copyWith(
                  fontSize: 34,
                  fontWeight: FontWeight.w700,
                  letterSpacing: -1.2,
                ),
              ),
            ),
          ),
          PopupMenuButton<String>(
            key: const Key('chats-more'),
            tooltip: 'More chat options',
            style: IconButton.styleFrom(
              backgroundColor: colors.surfaceMuted,
              fixedSize: const Size(44, 44),
            ),
            icon: Icon(
              PhosphorIconsRegular.dotsThree,
              size: 22,
              color: colors.ink,
            ),
            onSelected: (action) {
              if (action == 'manage') onManage?.call();
              if (action == 'search') onSearch();
            },
            itemBuilder: (_) => [
              if (onManage != null)
                const PopupMenuItem(
                  value: 'manage',
                  child: Text('Manage conversations'),
                ),
              const PopupMenuItem(
                value: 'search',
                child: Text('Search everything'),
              ),
            ],
          ),
          const SizedBox(width: 8),
          IconButton(
            key: const Key('chats-new'),
            tooltip: 'New chat',
            onPressed: onNewChat,
            style: IconButton.styleFrom(
              backgroundColor: colors.ink,
              disabledBackgroundColor: colors.surfaceMuted,
              fixedSize: const Size(44, 44),
            ),
            icon: Icon(
              PhosphorIconsRegular.plus,
              size: 20,
              color: onNewChat == null ? colors.muted : colors.onInk,
            ),
          ),
        ],
      ),
    );
  }
}

/// The filters as pills. The ink pill slides and resizes to the one you pick.
class FilterPills extends StatefulWidget {
  const FilterPills({
    required this.selected,
    required this.unread,
    required this.onSelected,
    super.key,
  });

  final ChatFilter selected;
  final int unread;
  final ValueChanged<ChatFilter> onSelected;

  @override
  State<FilterPills> createState() => _FilterPillsState();
}

class _FilterPillsState extends State<FilterPills> {
  final _keys = {for (final filter in ChatFilter.values) filter: GlobalKey()};
  final _scroll = ScrollController();
  Rect? _pill;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure());
  }

  @override
  void didUpdateWidget(FilterPills oldWidget) {
    super.didUpdateWidget(oldWidget);
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure(reveal: true));
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _measure({bool reveal = false}) {
    if (!mounted) return;
    final box = _keys[widget.selected]!.currentContext?.findRenderObject();
    final row = context.findRenderObject();
    if (box is! RenderBox || row is! RenderBox || !box.hasSize) return;
    final offset = box.localToGlobal(Offset.zero, ancestor: row);
    final rect = offset & box.size;
    if (rect != _pill) setState(() => _pill = rect);
    if (reveal) {
      final context = _keys[widget.selected]!.currentContext;
      if (context != null) {
        unawaited(
          Scrollable.ensureVisible(
            context,
            alignmentPolicy: ScrollPositionAlignmentPolicy.keepVisibleAtEnd,
            duration: JarvisMotion.of(this.context, JarvisMotion.base),
            curve: JarvisMotion.standard,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final duration = JarvisMotion.of(context, JarvisMotion.slow);
    final pill = _pill;
    return NotificationListener<ScrollNotification>(
      onNotification: (_) {
        _measure();
        return false;
      },
      child: SizedBox(
        height: 36 + 28,
        child: Stack(
          children: [
            if (pill != null)
              AnimatedPositioned(
                duration: duration,
                curve: JarvisMotion.emphasized,
                left: pill.left,
                top: pill.top,
                width: pill.width,
                height: pill.height,
                child: IgnorePointer(
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      color: colors.ink,
                      borderRadius: BorderRadius.circular(999),
                    ),
                  ),
                ),
              ),
            SingleChildScrollView(
              controller: _scroll,
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
              child: Row(
                children: [
                  for (final filter in ChatFilter.values) ...[
                    if (filter != ChatFilter.values.first)
                      const SizedBox(width: 8),
                    _Pill(
                      key: _keys[filter],
                      filter: filter,
                      selected: filter == widget.selected,
                      count: filter == ChatFilter.unread ? widget.unread : 0,
                      onTap: () => widget.onSelected(filter),
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Pill extends StatelessWidget {
  const _Pill({
    required this.filter,
    required this.selected,
    required this.count,
    required this.onTap,
    super.key,
  });

  final ChatFilter filter;
  final bool selected;
  final int count;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final duration = JarvisMotion.of(context, JarvisMotion.base);
    final foreground = selected ? colors.onInk : colors.ink;
    return Semantics(
      selected: selected,
      button: true,
      label: count > 0 ? '${filter.label}, $count' : filter.label,
      excludeSemantics: true,
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          key: Key('chats-tab-${filter.name}'),
          onTap: onTap,
          // The ink pill sliding over is the feedback; no ripple on top.
          splashFactory: NoSplash.splashFactory,
          overlayColor: const WidgetStatePropertyAll(Colors.transparent),
          borderRadius: BorderRadius.circular(999),
          child: AnimatedContainer(
            duration: duration,
            curve: JarvisMotion.standard,
            height: 36,
            padding: const EdgeInsets.symmetric(horizontal: 15),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(999),
              border: Border.all(
                color: selected ? Colors.transparent : colors.outline,
              ),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                AnimatedDefaultTextStyle(
                  duration: duration,
                  style: TextStyle(
                    fontFamily: 'Geist',
                    fontSize: 14,
                    fontWeight: FontWeight.w500,
                    color: foreground,
                  ),
                  child: Text(filter.label),
                ),
                if (count > 0) ...[
                  const SizedBox(width: 6),
                  Text(
                    count > 99 ? '99+' : '$count',
                    key: Key('chats-tab-${filter.name}-count'),
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                      fontFeatures: const [FontFeature.tabularFigures()],
                      color: selected ? colors.onInk : colors.accent,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// A caps label that sticks to the top while its section scrolls under it.
class _SectionHeader extends StatelessWidget {
  const _SectionHeader(this.section, {this.trailing});

  final ChatSection section;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: JarvisColors.of(context).canvas,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 18, 12, 4),
        child: SizedBox(
          height: 32,
          child: Row(
            children: [
              Expanded(
                child: Semantics(
                  header: true,
                  child: Text(
                    section.label.toUpperCase(),
                    key: Key('chats-section-${section.name}'),
                    style: JarvisType.capsOf(context),
                  ),
                ),
              ),
              ?trailing,
            ],
          ),
        ),
      ),
    );
  }
}

/// Several chats folded into one row: reminders Jarvis is handling.
class _BundleRow extends StatelessWidget {
  const _BundleRow({
    required this.icon,
    required this.tint,
    required this.background,
    required this.title,
    required this.count,
    required this.detail,
    required this.time,
    required this.open,
    required this.onTap,
    super.key,
  });

  final IconData icon;
  final Color tint;
  final Color background;
  final String title;
  final int count;
  final String detail;
  final String time;
  final bool open;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      expanded: open,
      label: '$title, $count, $detail',
      excludeSemantics: true,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(18),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8),
          child: Row(
            children: [
              Container(
                width: ChatRow.avatarSize,
                height: ChatRow.avatarSize,
                decoration: BoxDecoration(
                  color: background,
                  shape: BoxShape.circle,
                ),
                child: Icon(icon, size: 22, color: tint),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Container(
                  constraints: const BoxConstraints(minHeight: ChatRow.height),
                  padding: const EdgeInsets.symmetric(vertical: 14),
                  decoration: BoxDecoration(
                    border: Border(bottom: BorderSide(color: colors.outline)),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Row(
                        children: [
                          Text(
                            title,
                            style: TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.w600,
                              letterSpacing: -.25,
                              color: colors.ink,
                            ),
                          ),
                          const SizedBox(width: 6),
                          ChatTag(
                            '$count',
                            background: background,
                            foreground: tint,
                          ),
                          const Spacer(),
                          Text(
                            time,
                            style: TextStyle(
                              fontSize: 12.5,
                              color: colors.muted,
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 3),
                      Row(
                        children: [
                          Expanded(
                            child: Text(
                              detail,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                fontSize: 14,
                                color: colors.inkSoft,
                              ),
                            ),
                          ),
                          AnimatedRotation(
                            turns: open ? .25 : 0,
                            duration: JarvisMotion.of(
                              context,
                              JarvisMotion.base,
                            ),
                            curve: JarvisMotion.standard,
                            child: Icon(
                              PhosphorIconsRegular.caretRight,
                              size: 16,
                              color: colors.muted,
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Busy groups that ask nothing of you, as one quiet line with a recap.
class _BusyGroups extends StatelessWidget {
  const _BusyGroups({required this.groups, required this.onTap, this.http});

  final List<ChatListItem> groups;
  final VoidCallback onTap;
  final Dio? http;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final unread = groups.fold(0, (total, item) => total + item.unread);
    final names = groups.map((item) => item.title).take(2).join(', ');
    final more = groups.length - 2;
    final who = more > 0 ? '$names and $more more' : names;
    final title = groups.length == 1
        ? '1 busy group'
        : '${groups.length} busy groups';
    return Padding(
      padding: const EdgeInsets.fromLTRB(0, 10, 0, 0),
      child: Semantics(
        button: true,
        label: '$title, $unread new, nothing for you. $who',
        excludeSemantics: true,
        child: Material(
          color: colors.surfaceMuted,
          borderRadius: BorderRadius.circular(20),
          child: InkWell(
            key: const Key('chats-busy-groups'),
            onTap: onTap,
            borderRadius: BorderRadius.circular(20),
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Row(
                children: [
                  SizedBox(
                    width: ChatRow.avatarSize,
                    height: ChatRow.avatarSize,
                    child: Stack(
                      children: [
                        for (final (index, item) in groups.take(3).indexed)
                          Positioned(
                            left: const [0.0, 20.0, 8.0][index],
                            top: const [0.0, 6.0, 22.0][index],
                            child: Container(
                              decoration: BoxDecoration(
                                shape: BoxShape.circle,
                                border: Border.all(
                                  color: colors.surfaceMuted,
                                  width: 2.5,
                                ),
                              ),
                              child: ChatRowAvatar(
                                item: item,
                                http: http,
                                size: const [30.0, 28.0, 26.0][index],
                                showSource: false,
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          title,
                          style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w600,
                            letterSpacing: -.25,
                            color: colors.ink,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '$unread new, nothing for you',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(fontSize: 14, color: colors.inkSoft),
                        ),
                      ],
                    ),
                  ),
                  Text(
                    'Recap',
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w600,
                      color: colors.accent,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Empty extends StatelessWidget {
  const _Empty({
    required this.filter,
    required this.searching,
    required this.linked,
    required this.onConnect,
    required this.onNewChat,
  });

  final ChatFilter filter;
  final bool searching;
  final bool linked;
  final VoidCallback? onConnect;
  final VoidCallback? onNewChat;

  @override
  Widget build(BuildContext context) {
    final (icon, title, message) = switch ((searching, filter)) {
      (true, _) => (
        PhosphorIconsRegular.magnifyingGlass,
        'No chats match',
        'Try another word.',
      ),
      (_, ChatFilter.unread) => (
        PhosphorIconsRegular.checkCircle,
        'You are all caught up',
        'Unread WhatsApp chats show up here.',
      ),
      (_, ChatFilter.whatsapp) when !linked => (
        PhosphorIconsRegular.whatsappLogo,
        'Bring your WhatsApp in',
        'Link your account and its chats appear here next to Jarvis.',
      ),
      (_, ChatFilter.whatsapp) => (
        PhosphorIconsRegular.whatsappLogo,
        'No WhatsApp chats yet',
        'Messages show up here once they arrive.',
      ),
      (_, ChatFilter.groups) => (
        PhosphorIconsRegular.users,
        'No group chats',
        'WhatsApp groups show up here.',
      ),
      _ => (
        PhosphorIconsRegular.chatCircle,
        'No conversations yet',
        'Ask Jarvis anything to start one.',
      ),
    };
    final action = switch ((searching, filter)) {
      (false, ChatFilter.whatsapp) when !linked && onConnect != null =>
        FilledButton(
          key: const Key('chats-connect-whatsapp'),
          onPressed: onConnect,
          child: const Text('Connect WhatsApp'),
        ),
      (false, ChatFilter.forYou || ChatFilter.all || ChatFilter.jarvis)
          when onNewChat != null =>
        FilledButton(
          key: const Key('chats-empty-new'),
          onPressed: onNewChat,
          child: const Text('Start a chat'),
        ),
      _ => null,
    };
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        SizedBox(
          height: 360,
          child: EmptyState(
            icon: icon,
            title: title,
            message: message,
            action: action,
          ),
        ),
      ],
    );
  }
}

/// What the person picked in the hold-to-preview menu.
enum ChatPreviewAction { open, pin, read, summarize, draft }

/// Press and hold a chat: the list blurs, the chat lifts out with what was
/// said last (and Jarvis's catch-up), and the quick actions follow.
Future<ChatPreviewAction?> showChatPreview(
  BuildContext context, {
  required ChatListItem item,
  Dio? http,
  bool canAsk = false,
  bool canChange = false,
  String? relationship,
  ValueChanged<String>? onReply,
}) {
  final duration = JarvisMotion.of(context, JarvisMotion.slow);
  final reduced = JarvisMotion.reduced(context);
  return showGeneralDialog<ChatPreviewAction>(
    context: context,
    barrierDismissible: true,
    barrierLabel: 'Close preview',
    barrierColor: Colors.transparent,
    transitionDuration: duration,
    pageBuilder: (context, _, _) => _ChatPreview(
      item: item,
      http: http,
      canAsk: canAsk,
      canChange: canChange,
      relationship: relationship,
      onReply: onReply,
    ),
    transitionBuilder: (context, animation, _, child) {
      final fade = CurvedAnimation(
        parent: animation,
        curve: const Interval(0, .6, curve: Curves.easeOutCubic),
        reverseCurve: Curves.easeInCubic,
      );
      final lift = CurvedAnimation(
        parent: animation,
        curve: reduced ? Curves.linear : const SpringCurve(damping: 9),
        reverseCurve: Curves.easeInCubic,
      );
      return Stack(
        children: [
          Positioned.fill(
            child: GestureDetector(
              onTap: () => Navigator.of(context).maybePop(),
              child: AnimatedBuilder(
                animation: fade,
                builder: (context, _) => BackdropFilter(
                  filter: ui.ImageFilter.blur(
                    sigmaX: reduced ? 0 : 8 * fade.value,
                    sigmaY: reduced ? 0 : 8 * fade.value,
                  ),
                  child: ColoredBox(
                    color: Colors.black.withValues(alpha: .28 * fade.value),
                  ),
                ),
              ),
            ),
          ),
          FadeTransition(
            opacity: fade,
            child: reduced
                ? child
                : AnimatedBuilder(
                    animation: lift,
                    builder: (context, child) => Transform.translate(
                      offset: Offset(0, 16 * (1 - lift.value)),
                      child: Transform.scale(
                        scale: .96 + .04 * lift.value,
                        alignment: Alignment.topCenter,
                        child: child,
                      ),
                    ),
                    child: child,
                  ),
          ),
        ],
      );
    },
  );
}

class _ChatPreview extends StatelessWidget {
  const _ChatPreview({
    required this.item,
    required this.canAsk,
    required this.canChange,
    this.http,
    this.relationship,
    this.onReply,
  });

  final ChatListItem item;
  final Dio? http;
  final bool canAsk;
  final bool canChange;

  /// Who this is to you, from People.
  final String? relationship;

  /// Opens the chat with a quick reply in the composer.
  final ValueChanged<String>? onReply;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final catchUp = item.whatsApp?.catchUp;
    final preview = item.preview?.trim();
    final actions = <(ChatPreviewAction, IconData, String)>[
      (ChatPreviewAction.open, PhosphorIconsRegular.chatCircle, 'Open'),
      if (canChange && item.isJarvis)
        (
          ChatPreviewAction.pin,
          PhosphorIconsRegular.pushPin,
          item.pinned ? 'Unpin' : 'Pin',
        ),
      if (canChange && !item.isJarvis && item.unread > 0)
        (ChatPreviewAction.read, PhosphorIconsRegular.check, 'Read'),
      if (canAsk && !item.isJarvis)
        (ChatPreviewAction.draft, PhosphorIconsRegular.pencilSimple, 'Draft'),
      if (canAsk)
        (ChatPreviewAction.summarize, PhosphorIconsRegular.sparkle, 'Recap'),
    ];
    return SafeArea(
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 480),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 56, 16, 16),
            child: Material(
              type: MaterialType.transparency,
              child: Column(
                key: const Key('chat-preview'),
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Container(
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: colors.surface,
                      borderRadius: BorderRadius.circular(24),
                      boxShadow: [
                        BoxShadow(
                          color: Colors.black.withValues(alpha: .22),
                          blurRadius: 60,
                          offset: const Offset(0, 30),
                          spreadRadius: -20,
                        ),
                      ],
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            ChatRowAvatar(
                              item: item,
                              http: http,
                              size: 40,
                              showSource: false,
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    item.title,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      fontSize: 16,
                                      fontWeight: FontWeight.w600,
                                      color: colors.ink,
                                    ),
                                  ),
                                  Text(
                                    [
                                      item.isJarvis
                                          ? (item.profile ?? 'Jarvis')
                                          : (item.group
                                                ? 'WhatsApp group'
                                                : relationship ?? 'WhatsApp'),
                                      if (item.unread > 0) '${item.unread} new',
                                    ].join(' · '),
                                    style: TextStyle(
                                      fontSize: 13,
                                      color: colors.inkSoft,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                        if (catchUp != null) ...[
                          const SizedBox(height: 14),
                          Container(
                            width: double.infinity,
                            padding: const EdgeInsets.all(14),
                            decoration: BoxDecoration(
                              color: colors.accentSoft,
                              borderRadius: BorderRadius.circular(16),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Row(
                                  children: [
                                    const JarvisOrb(size: 18, glow: false),
                                    const SizedBox(width: 8),
                                    Text(
                                      'WHAT YOU MISSED',
                                      style: JarvisType.capsOf(
                                        context,
                                      ).copyWith(color: colors.accentDeep),
                                    ),
                                  ],
                                ),
                                const SizedBox(height: 8),
                                Text(
                                  catchUp.summary,
                                  style: TextStyle(
                                    fontSize: 14.5,
                                    height: 1.45,
                                    color: colors.ink,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                        if (preview != null && preview.isNotEmpty) ...[
                          const SizedBox(height: 14),
                          Align(
                            alignment: item.fromMe
                                ? Alignment.centerRight
                                : Alignment.centerLeft,
                            child: Container(
                              constraints: const BoxConstraints(maxWidth: 320),
                              padding: const EdgeInsets.symmetric(
                                horizontal: 13,
                                vertical: 9,
                              ),
                              decoration: BoxDecoration(
                                color: item.fromMe
                                    ? colors.ink
                                    : colors.surfaceMuted,
                                borderRadius: BorderRadius.circular(18),
                              ),
                              child: Text(
                                preview,
                                maxLines: 4,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  fontSize: 14.5,
                                  height: 1.4,
                                  color: item.fromMe
                                      ? colors.onInk
                                      : colors.ink,
                                ),
                              ),
                            ),
                          ),
                        ],
                        if ((http, onReply) case (
                          final http?,
                          final reply?,
                        ) when !item.isJarvis && !item.group && item.unread > 0)
                          QuickReplies(
                            http: http,
                            item: item,
                            padding: const EdgeInsets.only(top: 12),
                            onPick: (text) {
                              Navigator.of(context).pop();
                              reply(text);
                            },
                          ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 10),
                  Row(
                    children: [
                      for (final (index, (action, icon, label))
                          in actions.indexed) ...[
                        if (index > 0) const SizedBox(width: 8),
                        Expanded(
                          child: FadeSlideIn(
                            index: index,
                            offset: 8,
                            child: _QuickAction(
                              key: Key('chat-preview-${action.name}'),
                              icon: icon,
                              label: label,
                              onTap: () => Navigator.of(context).pop(action),
                            ),
                          ),
                        ),
                      ],
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _QuickAction extends StatelessWidget {
  const _QuickAction({
    required this.icon,
    required this.label,
    required this.onTap,
    super.key,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Material(
      color: colors.surface,
      borderRadius: BorderRadius.circular(16),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(16),
        child: SizedBox(
          height: 68,
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 22, color: colors.ink),
              const SizedBox(height: 6),
              Text(
                label,
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w500,
                  color: colors.ink,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
