import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../http_urls.dart';
import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'reading_models.dart';

/// Links saved to read later. Jarvis fetches each page in the background and
/// adds a short summary, key points, and a reading time.
class ReadingListScreen extends StatefulWidget {
  const ReadingListScreen({
    required this.http,
    this.onAskInChat,
    this.pollInterval = const Duration(seconds: 4),
    super.key,
  });

  final Dio http;

  /// Sends a prompt to chat; the "Summarize with Jarvis" button is hidden
  /// when null.
  final ValueChanged<String>? onAskInChat;

  /// How often to check again while a page is still being read.
  final Duration pollInterval;

  @override
  State<ReadingListScreen> createState() => _ReadingListScreenState();
}

class _ReadingListScreenState extends State<ReadingListScreen>
    with WidgetsBindingObserver {
  final _link = TextEditingController();
  final _linkFocus = FocusNode();
  List<ReadingItemData> _items = const [];
  final Set<String> _hidden = {};
  bool _loading = true;
  bool _saving = false;
  bool _showRead = false;
  String? _error;
  String? _linkError;
  Timer? _poll;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _link.addListener(_onLinkChanged);
    unawaited(_load());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _poll?.cancel();
    _link.dispose();
    _linkFocus.dispose();
    super.dispose();
  }

  // Links saved from chat or WhatsApp show up when the app comes back.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_load());
  }

  void _onLinkChanged() {
    if (_linkError != null) setState(() => _linkError = null);
    setState(() {});
  }

  List<ReadingItemData> get _visible =>
      _items.where((item) => !_hidden.contains(item.id)).toList();

  Future<void> _load({bool quiet = false}) async {
    final revision = ++_requestRevision;
    if (!quiet) setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/reading');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _items = ReadingItemData.listFromJson(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        if (!quiet) {
          _error =
              firstProblemMessage(error.response?.data) ??
              'Could not load your reading list.';
        }
      });
    }
    _schedulePoll();
  }

  // Keep checking while Jarvis is still reading a page.
  void _schedulePoll() {
    _poll?.cancel();
    if (!mounted || !_items.any((item) => item.pending)) return;
    _poll = Timer(widget.pollInterval, () => unawaited(_load(quiet: true)));
  }

  Future<void> _paste() async {
    final data = await Clipboard.getData(Clipboard.kTextPlain);
    final link = extractLink(data?.text);
    if (!mounted) return;
    if (link == null) {
      _snack('There is no link on your clipboard.');
      return;
    }
    _link.text = link;
    _link.selection = TextSelection.collapsed(offset: link.length);
    unawaited(_save());
  }

  Future<void> _save() async {
    final link = extractLink(_link.text) ?? _link.text.trim();
    if (link.isEmpty || _saving) return;
    setState(() {
      _saving = true;
      _linkError = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/reading',
        data: {'url': link},
      );
      if (!mounted) return;
      final item = ReadingItemData.fromJson(response.data);
      _link.clear();
      _linkFocus.unfocus();
      setState(() {
        _saving = false;
        _showRead = false;
        if (item != null) {
          _items = [
            item,
            ..._items.where((existing) => existing.id != item.id),
          ];
        }
      });
      if (response.statusCode == 200) {
        _snack('That link is already on your reading list.');
      }
      _schedulePoll();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _linkError =
            firstProblemMessage(error.response?.data) ??
            'Could not save that link.';
      });
    }
  }

  Future<void> _setRead(ReadingItemData item, bool read) async {
    setState(() {
      _items = [
        for (final existing in _items)
          existing.id == item.id ? existing.copyWith(read: read) : existing,
      ];
    });
    try {
      await widget.http.patch<dynamic>(
        '/api/v1/reading/${item.id}',
        data: {'read': read},
      );
      if (!mounted) return;
      _snack(
        read ? 'Marked as read' : 'Back on your unread list',
        undo: () => unawaited(_setRead(item.copyWith(read: read), !read)),
      );
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _items = [
          for (final existing in _items)
            existing.id == item.id ? item : existing,
        ];
      });
      _snack(
        firstProblemMessage(error.response?.data) ??
            'Could not update that link.',
      );
    }
  }

  // The item disappears at once; it is only deleted when the snackbar closes
  // without "Undo".
  void _remove(ReadingItemData item) {
    setState(() => _hidden.add(item.id));
    final messenger = ScaffoldMessenger.of(context);
    messenger.hideCurrentSnackBar();
    var undone = false;
    final controller = messenger.showSnackBar(
      SnackBar(
        content: const Text('Removed from your reading list'),
        action: SnackBarAction(
          label: 'Undo',
          onPressed: () {
            undone = true;
            if (mounted) setState(() => _hidden.remove(item.id));
          },
        ),
      ),
    );
    unawaited(
      controller.closed.then((_) async {
        if (undone) return;
        try {
          await widget.http.delete<dynamic>('/api/v1/reading/${item.id}');
          if (!mounted) return;
          setState(() {
            _items = _items
                .where((existing) => existing.id != item.id)
                .toList();
            _hidden.remove(item.id);
          });
        } on DioException {
          if (!mounted) return;
          setState(() => _hidden.remove(item.id));
          _snack('Could not remove that link.');
        }
      }),
    );
  }

  Future<void> _refresh(ReadingItemData item) async {
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/reading/${item.id}/refresh',
      );
      final updated = ReadingItemData.fromJson(response.data);
      if (!mounted || updated == null) return;
      setState(() {
        _items = [
          for (final existing in _items)
            existing.id == item.id ? updated : existing,
        ];
      });
      _schedulePoll();
    } on DioException catch (error) {
      if (!mounted) return;
      _snack(
        firstProblemMessage(error.response?.data) ?? 'Could not try again.',
      );
    }
  }

  Future<void> _open(ReadingItemData item) async {
    final uri = parseHttpUrl(item.url);
    if (uri == null || !await launchHttpUrl(uri)) {
      if (mounted) _snack('Could not open that link.');
    }
  }

  void _askJarvis() {
    final ask = widget.onAskInChat;
    if (ask == null) return;
    Navigator.of(context).maybePop();
    ask(readingSummaryPrompt);
  }

  void _snack(String message, {VoidCallback? undo}) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(message),
          action: undo == null
              ? null
              : SnackBarAction(label: 'Undo', onPressed: undo),
        ),
      );
  }

  @override
  Widget build(BuildContext context) {
    final visible = _visible;
    final unread = visible.where((item) => !item.read).toList();
    final read = visible.where((item) => item.read).toList();
    final shown = _showRead ? read : unread;
    return Scaffold(
      appBar: AppBar(title: const Text('Reading list')),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: false,
        onRetry: () => unawaited(_load()),
        onRefresh: _load,
        empty: const SizedBox.shrink(),
        child: ListView(
          padding: EdgeInsets.fromLTRB(
            16,
            8,
            16,
            32 + MediaQuery.paddingOf(context).bottom,
          ),
          children: [
            ContentWidth(
              child: _LinkComposer(
                controller: _link,
                focusNode: _linkFocus,
                saving: _saving,
                error: _linkError,
                onSave: () => unawaited(_save()),
                onPaste: () => unawaited(_paste()),
              ),
            ),
            if (_loading && _items.isEmpty)
              const Padding(
                padding: EdgeInsets.only(top: 48),
                child: Center(child: CircularProgressIndicator()),
              )
            else if (visible.isEmpty)
              const ContentWidth(
                child: Padding(
                  padding: EdgeInsets.only(top: 24),
                  child: EmptyState(
                    icon: PhosphorIconsRegular.bookmarkSimple,
                    title: 'Nothing saved yet',
                    message:
                        'Paste a link above, or tell Jarvis “read this later” '
                        'with a link in chat or WhatsApp. Jarvis reads the page '
                        'and adds a summary and reading time.',
                  ),
                ),
              )
            else ...[
              const SizedBox(height: 18),
              ContentWidth(
                child: _Overview(
                  unread: unread,
                  readCount: read.length,
                  showRead: _showRead,
                  onShowRead: (value) => setState(() => _showRead = value),
                  onAskJarvis: widget.onAskInChat == null || unread.isEmpty
                      ? null
                      : _askJarvis,
                ),
              ),
              const SizedBox(height: 12),
              if (shown.isEmpty)
                ContentWidth(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: 32),
                    child: Text(
                      _showRead
                          ? 'Links you finish show up here.'
                          : 'You are all caught up.',
                      textAlign: TextAlign.center,
                      style: TextStyle(color: JarvisColors.of(context).muted),
                    ),
                  ),
                ),
              for (final (index, item) in shown.indexed)
                ContentWidth(
                  key: ValueKey(item.id),
                  child: FadeSlideIn(
                    index: index,
                    child: _Swipeable(
                      item: item,
                      onToggleRead: () => unawaited(_setRead(item, !item.read)),
                      onRemove: () => _remove(item),
                      child: _ReadingCard(
                        item: item,
                        onOpen: () => unawaited(_open(item)),
                        onToggleRead: () =>
                            unawaited(_setRead(item, !item.read)),
                        onRetry: () => unawaited(_refresh(item)),
                        onRemove: () => _remove(item),
                      ),
                    ),
                  ),
                ),
            ],
          ],
        ),
      ),
    );
  }
}

class _LinkComposer extends StatelessWidget {
  const _LinkComposer({
    required this.controller,
    required this.focusNode,
    required this.saving,
    required this.error,
    required this.onSave,
    required this.onPaste,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final bool saving;
  final String? error;
  final VoidCallback onSave;
  final VoidCallback onPaste;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final hasText = controller.text.trim().isNotEmpty;
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(14, 12, 12, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(
                PhosphorIconsRegular.linkSimple,
                size: 20,
                color: colors.muted,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: TextField(
                  key: const Key('reading-link'),
                  controller: controller,
                  focusNode: focusNode,
                  keyboardType: TextInputType.url,
                  autocorrect: false,
                  textInputAction: TextInputAction.done,
                  onSubmitted: (_) => onSave(),
                  decoration: const InputDecoration(
                    hintText: 'Paste a link to read later',
                    border: InputBorder.none,
                    enabledBorder: InputBorder.none,
                    focusedBorder: InputBorder.none,
                    filled: false,
                    isDense: true,
                    contentPadding: EdgeInsets.symmetric(vertical: 10),
                  ),
                ),
              ),
              const SizedBox(width: 8),
              AnimatedSwitcher(
                duration: const Duration(milliseconds: 180),
                child: hasText || saving
                    ? FilledButton(
                        key: const Key('reading-save'),
                        onPressed: saving ? null : onSave,
                        style: FilledButton.styleFrom(
                          minimumSize: const Size(0, 40),
                          padding: const EdgeInsets.symmetric(horizontal: 16),
                        ),
                        child: saving
                            ? const SizedBox(
                                width: 16,
                                height: 16,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Text('Save'),
                      )
                    : OutlinedButton.icon(
                        key: const Key('reading-paste'),
                        onPressed: onPaste,
                        style: OutlinedButton.styleFrom(
                          minimumSize: const Size(0, 40),
                          padding: const EdgeInsets.symmetric(horizontal: 12),
                        ),
                        icon: const Icon(
                          PhosphorIconsRegular.clipboardText,
                          size: 16,
                        ),
                        label: const Text('Paste'),
                      ),
              ),
            ],
          ),
          if (error != null) ...[
            const SizedBox(height: 6),
            Text(error!, style: TextStyle(fontSize: 13, color: colors.danger)),
          ],
        ],
      ),
    );
  }
}

class _Overview extends StatelessWidget {
  const _Overview({
    required this.unread,
    required this.readCount,
    required this.showRead,
    required this.onShowRead,
    required this.onAskJarvis,
  });

  final List<ReadingItemData> unread;
  final int readCount;
  final bool showRead;
  final ValueChanged<bool> onShowRead;
  final VoidCallback? onAskJarvis;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                readingTotalsLabel(unread),
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            if (onAskJarvis != null)
              TextButton.icon(
                key: const Key('reading-ask-jarvis'),
                onPressed: onAskJarvis,
                icon: Icon(
                  PhosphorIconsRegular.sparkle,
                  size: 16,
                  color: colors.accent,
                ),
                label: const Text('Summarize'),
              ),
          ],
        ),
        const SizedBox(height: 10),
        SegmentedButton<bool>(
          segments: [
            ButtonSegment(
              value: false,
              label: Text('Unread · ${unread.length}'),
            ),
            ButtonSegment(value: true, label: Text('Read · $readCount')),
          ],
          selected: {showRead},
          showSelectedIcon: false,
          onSelectionChanged: (selection) => onShowRead(selection.first),
        ),
      ],
    );
  }
}

/// Swipe right to mark read (or unread), swipe left to remove.
class _Swipeable extends StatelessWidget {
  const _Swipeable({
    required this.item,
    required this.onToggleRead,
    required this.onRemove,
    required this.child,
  });

  final ReadingItemData item;
  final VoidCallback onToggleRead;
  final VoidCallback onRemove;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget background(Color color, IconData icon, String label, bool start) =>
        Container(
          margin: const EdgeInsets.only(bottom: 10),
          padding: const EdgeInsets.symmetric(horizontal: 22),
          alignment: start ? Alignment.centerLeft : Alignment.centerRight,
          decoration: BoxDecoration(
            color: color,
            borderRadius: BorderRadius.circular(16),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 18, color: colors.ink),
              const SizedBox(width: 8),
              Text(
                label,
                style: TextStyle(
                  fontWeight: FontWeight.w600,
                  color: colors.ink,
                ),
              ),
            ],
          ),
        );
    return Dismissible(
      key: ValueKey('swipe-${item.id}-${item.read}'),
      background: background(
        colors.successSoft,
        item.read ? PhosphorIconsRegular.eyeSlash : PhosphorIconsRegular.check,
        item.read ? 'Unread' : 'Read',
        true,
      ),
      secondaryBackground: background(
        colors.dangerSoft,
        PhosphorIconsRegular.trash,
        'Remove',
        false,
      ),
      confirmDismiss: (direction) async {
        if (direction == DismissDirection.startToEnd) {
          onToggleRead();
        } else {
          onRemove();
        }
        // The list itself moves the item; the card snaps back otherwise.
        return false;
      },
      child: child,
    );
  }
}

class _ReadingCard extends StatefulWidget {
  const _ReadingCard({
    required this.item,
    required this.onOpen,
    required this.onToggleRead,
    required this.onRetry,
    required this.onRemove,
  });

  final ReadingItemData item;
  final VoidCallback onOpen;
  final VoidCallback onToggleRead;
  final VoidCallback onRetry;
  final VoidCallback onRemove;

  @override
  State<_ReadingCard> createState() => _ReadingCardState();
}

class _ReadingCardState extends State<_ReadingCard> {
  bool _expanded = false;

  @override
  Widget build(BuildContext context) {
    final item = widget.item;
    final colors = JarvisColors.of(context);
    final time = readingTimeLabel(item.readingMinutes);
    final blurb = item.blurb;
    final canExpand = item.keyPoints.isNotEmpty;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      onTap: widget.onOpen,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBadge(
                icon: PhosphorIconsRegular.globeSimple,
                color: item.read ? colors.muted : colors.accent,
                size: 28,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  [item.source, relativeFromNow(item.createdAt)].join(' · '),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 12.5, color: colors.muted),
                ),
              ),
              if (time != null)
                _TimeChip(label: time)
              else if (item.pending)
                const _ReadingIndicator(),
              _CardMenu(
                item: item,
                onOpen: widget.onOpen,
                onToggleRead: widget.onToggleRead,
                onRetry: widget.onRetry,
                onRemove: widget.onRemove,
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            item.title,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.titleMedium?.copyWith(
              height: 1.3,
              color: item.read ? colors.inkSoft : colors.ink,
            ),
          ),
          if (item.pending) ...[
            const SizedBox(height: 8),
            Text(
              'Jarvis is reading the page…',
              style: TextStyle(fontSize: 13.5, color: colors.muted),
            ),
          ] else if (item.failed) ...[
            const SizedBox(height: 10),
            InlineNotice(
              message: item.failureReason ?? 'Jarvis could not read this page.',
              tone: NoticeTone.warning,
              actions: [
                TextButton(
                  onPressed: widget.onRetry,
                  child: const Text('Try again'),
                ),
              ],
            ),
          ] else if (blurb != null) ...[
            const SizedBox(height: 8),
            AnimatedSize(
              duration: const Duration(milliseconds: 220),
              curve: Curves.easeOutCubic,
              alignment: Alignment.topCenter,
              child: Text(
                blurb,
                maxLines: _expanded ? null : 3,
                overflow: _expanded ? null : TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 14,
                  height: 1.45,
                  color: colors.inkSoft,
                ),
              ),
            ),
          ],
          if (_expanded && canExpand) ...[
            const SizedBox(height: 10),
            for (final point in item.keyPoints)
              Padding(
                padding: const EdgeInsets.only(bottom: 6),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Padding(
                      padding: const EdgeInsets.only(top: 7, right: 10),
                      child: Container(
                        width: 5,
                        height: 5,
                        decoration: BoxDecoration(
                          color: colors.accent,
                          shape: BoxShape.circle,
                        ),
                      ),
                    ),
                    Expanded(
                      child: Text(
                        point,
                        style: TextStyle(
                          fontSize: 13.5,
                          height: 1.4,
                          color: colors.ink,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
          ],
          if (item.note != null) ...[
            const SizedBox(height: 8),
            Text(
              '“${item.note}”',
              style: TextStyle(
                fontSize: 13,
                fontStyle: FontStyle.italic,
                color: colors.inkSoft,
              ),
            ),
          ],
          if (!item.pending && !item.failed) ...[
            const SizedBox(height: 6),
            Row(
              children: [
                if (canExpand || (blurb?.length ?? 0) > 160)
                  TextButton(
                    key: Key('reading-expand-${item.id}'),
                    onPressed: () => setState(() => _expanded = !_expanded),
                    style: TextButton.styleFrom(
                      padding: const EdgeInsets.symmetric(horizontal: 8),
                      visualDensity: VisualDensity.compact,
                    ),
                    child: Text(
                      _expanded
                          ? 'Show less'
                          : canExpand
                          ? 'Key points'
                          : 'More',
                    ),
                  ),
                const Spacer(),
                TextButton.icon(
                  key: Key('reading-toggle-${item.id}'),
                  onPressed: widget.onToggleRead,
                  style: TextButton.styleFrom(
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    visualDensity: VisualDensity.compact,
                  ),
                  icon: Icon(
                    item.read
                        ? PhosphorIconsRegular.clockCounterClockwise
                        : PhosphorIconsRegular.check,
                    size: 16,
                  ),
                  label: Text(item.read ? 'Mark unread' : 'Mark read'),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

class _TimeChip extends StatelessWidget {
  const _TimeChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      padding: const EdgeInsets.fromLTRB(7, 3, 9, 3),
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(PhosphorIconsRegular.clock, size: 13, color: colors.inkSoft),
          const SizedBox(width: 4),
          Text(
            label,
            style: TextStyle(
              fontSize: 12,
              fontWeight: FontWeight.w500,
              color: colors.inkSoft,
            ),
          ),
        ],
      ),
    );
  }
}

class _ReadingIndicator extends StatelessWidget {
  const _ReadingIndicator();

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(horizontal: 4),
    child: SizedBox(
      width: 14,
      height: 14,
      child: CircularProgressIndicator(
        strokeWidth: 2,
        color: JarvisColors.of(context).accent,
        semanticsLabel: 'Reading the page',
      ),
    ),
  );
}

class _CardMenu extends StatelessWidget {
  const _CardMenu({
    required this.item,
    required this.onOpen,
    required this.onToggleRead,
    required this.onRetry,
    required this.onRemove,
  });

  final ReadingItemData item;
  final VoidCallback onOpen;
  final VoidCallback onToggleRead;
  final VoidCallback onRetry;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) => PopupMenuButton<String>(
    key: Key('reading-menu-${item.id}'),
    tooltip: 'More',
    icon: Icon(
      PhosphorIconsRegular.dotsThree,
      size: 20,
      color: JarvisColors.of(context).muted,
    ),
    onSelected: (value) => switch (value) {
      'open' => onOpen(),
      'read' => onToggleRead(),
      'refresh' => onRetry(),
      'copy' => unawaited(Clipboard.setData(ClipboardData(text: item.url))),
      'remove' => onRemove(),
      _ => null,
    },
    itemBuilder: (_) => [
      const PopupMenuItem(value: 'open', child: Text('Open link')),
      PopupMenuItem(
        value: 'read',
        child: Text(item.read ? 'Mark unread' : 'Mark read'),
      ),
      const PopupMenuItem(value: 'copy', child: Text('Copy link')),
      if (!item.pending)
        const PopupMenuItem(value: 'refresh', child: Text('Summarize again')),
      const PopupMenuItem(value: 'remove', child: Text('Remove')),
    ],
  );
}
