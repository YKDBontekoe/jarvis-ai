import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../chats/chat_row.dart' show ChatRowAvatar, ChatTag, UnreadCount;
import '../chats/quick_replies.dart';
import '../tiles/tile_card.dart' show tileRadius;
import 'home_insights.dart';

/// What a row's button asked for.
enum NeedsYouAction { open, done, snooze, reply, draft }

/// Everything waiting on the person, in one card. A row slides away the
/// moment it is handled, and the card shrinks to a single line once nothing
/// is left.
class NeedsYouCard extends StatefulWidget {
  const NeedsYouCard({
    required this.items,
    required this.onAction,
    this.onSeeAll,
    this.onReplyWith,
    this.http,
    this.pending = const {},
    super.key,
  });

  /// Opens a person's chat with a quick reply in the composer, not sent.
  final void Function(NeedsYouItem item, String draft)? onReplyWith;

  final List<NeedsYouItem> items;
  final void Function(NeedsYouItem item, NeedsYouAction action) onAction;
  final VoidCallback? onSeeAll;
  final Dio? http;

  /// Ids an action is running on.
  final Set<String> pending;

  @override
  State<NeedsYouCard> createState() => _NeedsYouCardState();
}

class _NeedsYouCardState extends State<NeedsYouCard> {
  /// Rows sliding out; they leave the list once the slide is done.
  final Set<String> _leaving = {};

  /// Rows already handled here, kept away until Home reloads without them.
  final Set<String> _handled = {};

  @override
  void didUpdateWidget(NeedsYouCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    final ids = {for (final item in widget.items) item.id};
    _handled.retainAll(ids);
  }

  void _act(NeedsYouItem item, NeedsYouAction action) {
    final resolves =
        action == NeedsYouAction.done || action == NeedsYouAction.snooze;
    if (!resolves) {
      widget.onAction(item, action);
      return;
    }
    unawaited(HapticFeedback.lightImpact());
    setState(() => _leaving.add(item.id));
    final wait = JarvisMotion.of(context, JarvisMotion.fast);
    Timer(wait, () {
      if (!mounted) return;
      setState(() {
        _leaving.remove(item.id);
        _handled.add(item.id);
      });
      widget.onAction(item, action);
    });
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final visible = [
      for (final item in widget.items)
        if (!_handled.contains(item.id)) item,
    ];
    return MotionSize(
      child: visible.isEmpty
          ? Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: PopIn(
                key: const Key('home-needs-you-clear'),
                child: Row(
                  children: [
                    Icon(
                      PhosphorIconsRegular.checkCircle,
                      size: 18,
                      color: colors.accent,
                    ),
                    const SizedBox(width: 8),
                    Text(
                      "You're all caught up",
                      style: TextStyle(
                        fontSize: 14,
                        fontWeight: FontWeight.w500,
                        color: colors.inkSoft,
                      ),
                    ),
                  ],
                ),
              ),
            )
          : Column(
              key: const Key('home-needs-you'),
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(4, 0, 4, 10),
                  child: Row(
                    children: [
                      Semantics(
                        header: true,
                        child: Text(
                          'Needs you',
                          style: TextStyle(
                            fontSize: 20,
                            fontWeight: FontWeight.w600,
                            letterSpacing: -.5,
                            color: colors.ink,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      PopIn(
                        key: ValueKey(visible.length),
                        child: UnreadCount(visible.length),
                      ),
                      const Spacer(),
                      if (widget.onSeeAll != null)
                        TextButton(
                          key: const Key('home-needs-you-all'),
                          onPressed: widget.onSeeAll,
                          child: const Text('See all'),
                        ),
                    ],
                  ),
                ),
                Container(
                  decoration: BoxDecoration(
                    color: colors.surface,
                    borderRadius: BorderRadius.circular(tileRadius),
                    boxShadow: JarvisShadows.soft(colors.brightness),
                  ),
                  clipBehavior: Clip.antiAlias,
                  child: Column(
                    children: [
                      for (final (index, item) in visible.indexed)
                        _LeavingRow(
                          key: ValueKey(item.id),
                          leaving: _leaving.contains(item.id),
                          child: FadeSlideIn(
                            index: index,
                            offset: 8,
                            child: _NeedsYouRow(
                              item: item,
                              http: widget.http,
                              divider: index > 0,
                              busy: widget.pending.contains(item.id),
                              onAction: (action) => _act(item, action),
                              replies:
                                  item == _firstPerson(visible) &&
                                      widget.onReplyWith != null
                                  ? (text) => widget.onReplyWith!(item, text)
                                  : null,
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}

/// The first one-to-one chat: it gets quick replies.
NeedsYouItem? _firstPerson(List<NeedsYouItem> items) {
  for (final item in items) {
    if (item.kind == NeedsYouKind.person && item.chat?.group == false) {
      return item;
    }
  }
  return null;
}

/// Slides a handled row toward the side it was sent and fades it.
class _LeavingRow extends StatelessWidget {
  const _LeavingRow({required this.leaving, required this.child, super.key});

  final bool leaving;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final duration = JarvisMotion.of(context, JarvisMotion.fast);
    return AnimatedSlide(
      offset: leaving ? const Offset(.12, 0) : Offset.zero,
      duration: duration,
      curve: JarvisMotion.exit,
      child: AnimatedOpacity(
        opacity: leaving ? 0 : 1,
        duration: duration,
        curve: JarvisMotion.exit,
        child: child,
      ),
    );
  }
}

class _NeedsYouRow extends StatelessWidget {
  const _NeedsYouRow({
    required this.item,
    required this.onAction,
    required this.divider,
    required this.busy,
    this.http,
    this.replies,
  });

  final NeedsYouItem item;

  /// Set on the row that offers quick replies.
  final ValueChanged<String>? replies;
  final ValueChanged<NeedsYouAction> onAction;
  final bool divider;
  final bool busy;
  final Dio? http;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final (leading, actions) = _parts(context);
    return Semantics(
      container: true,
      label: '${item.title}, ${item.detail}',
      child: InkWell(
        key: Key('home-needs-you-${item.kind.name}-${item.id}'),
        onTap: () => onAction(
          item.kind == NeedsYouKind.person
              ? NeedsYouAction.reply
              : NeedsYouAction.open,
        ),
        child: Container(
          decoration: BoxDecoration(
            border: divider
                ? Border(top: BorderSide(color: colors.outline))
                : null,
          ),
          padding: const EdgeInsets.fromLTRB(16, 14, 12, 14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  leading,
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Flexible(
                              child: Text(
                                item.title,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  fontSize: 15.5,
                                  fontWeight: FontWeight.w600,
                                  letterSpacing: -.2,
                                  color: colors.ink,
                                ),
                              ),
                            ),
                            if (item.relationship case final relationship?) ...[
                              const SizedBox(width: 6),
                              Flexible(
                                child: ChatTag(
                                  relationship,
                                  background: colors.rose.withValues(
                                    alpha: colors.isDark ? .18 : .1,
                                  ),
                                  foreground: colors.rose,
                                ),
                              ),
                            ],
                          ],
                        ),
                        const SizedBox(height: 2),
                        Text(
                          item.detail,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 13,
                            color: item.overdue
                                ? colors.warning
                                : colors.inkSoft,
                          ),
                        ),
                        if (item.hint case final hint?) ...[
                          const SizedBox(height: 2),
                          Text(
                            hint,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(fontSize: 12, color: colors.muted),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  if (busy)
                    const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  else
                    ...actions,
                ],
              ),
              if ((http, item.chat, replies) case (
                final http?,
                final chat?,
                final pick?,
              ))
                QuickReplies(
                  http: http,
                  item: chat,
                  onPick: pick,
                  padding: const EdgeInsets.only(left: 58, top: 10),
                ),
            ],
          ),
        ),
      ),
    );
  }

  (Widget, List<Widget>) _parts(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget icon(IconData data, Color tint, Color background) => Container(
      width: 44,
      height: 44,
      decoration: BoxDecoration(color: background, shape: BoxShape.circle),
      child: Icon(data, size: 21, color: tint),
    );
    return switch (item.kind) {
      NeedsYouKind.approval => (
        icon(
          PhosphorIconsRegular.shieldCheck,
          colors.accent,
          colors.accentSoft,
        ),
        [
          _RowButton(
            'Review',
            primary: true,
            onTap: () => onAction(NeedsYouAction.open),
          ),
        ],
      ),
      NeedsYouKind.reminder => (
        icon(PhosphorIconsRegular.bell, colors.warning, colors.warningSoft),
        [
          _RowButton(
            '+10 min',
            semantic: 'Snooze ten minutes',
            onTap: () => onAction(NeedsYouAction.snooze),
          ),
          const SizedBox(width: 6),
          _RowButton(
            'Done',
            primary: true,
            onTap: () => onAction(NeedsYouAction.done),
          ),
        ],
      ),
      NeedsYouKind.person => (
        item.chat == null
            ? icon(
                PhosphorIconsRegular.user,
                colors.inkSoft,
                colors.surfaceMuted,
              )
            : ChatRowAvatar(item: item.chat!, http: http, size: 44),
        [
          _RowButton('Draft', onTap: () => onAction(NeedsYouAction.draft)),
          const SizedBox(width: 6),
          _RowButton(
            'Reply',
            primary: true,
            onTap: () => onAction(NeedsYouAction.reply),
          ),
        ],
      ),
      NeedsYouKind.task => (
        icon(PhosphorIconsRegular.robot, colors.info, colors.infoSoft),
        [_RowButton('Open', onTap: () => onAction(NeedsYouAction.open))],
      ),
      NeedsYouKind.catchUp => (
        icon(PhosphorIconsRegular.sparkle, colors.accent, colors.accentSoft),
        [_RowButton('Catch up', onTap: () => onAction(NeedsYouAction.open))],
      ),
    };
  }
}

class _RowButton extends StatelessWidget {
  const _RowButton(
    this.label, {
    required this.onTap,
    this.primary = false,
    this.semantic,
  });

  final String label;
  final VoidCallback onTap;
  final bool primary;
  final String? semantic;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      label: semantic ?? label,
      excludeSemantics: true,
      child: PressFeedback(
        scale: .95,
        builder: (context, highlight) => Material(
          color: primary ? colors.ink : colors.surfaceMuted,
          borderRadius: BorderRadius.circular(12),
          child: InkWell(
            key: Key('home-needs-you-button-$label'),
            onTap: onTap,
            onHighlightChanged: highlight,
            borderRadius: BorderRadius.circular(12),
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 36, minWidth: 44),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 13),
                child: Center(
                  widthFactor: 1,
                  child: Text(
                    label,
                    style: TextStyle(
                      fontSize: 13.5,
                      fontWeight: FontWeight.w600,
                      color: primary ? colors.onInk : colors.ink,
                    ),
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
