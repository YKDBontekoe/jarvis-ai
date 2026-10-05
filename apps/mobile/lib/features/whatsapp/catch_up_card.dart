import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import 'whatsapp_models.dart';

/// The catch-up at the top of one chat: what happened while you were away and
/// what is waiting for an answer. Each item can start a drafted reply.
class CatchUpCard extends StatelessWidget {
  const CatchUpCard({
    required this.catchUp,
    required this.onDismiss,
    required this.onDraft,
    super.key,
  });

  final WhatsAppCatchUp catchUp;
  final VoidCallback onDismiss;

  /// Starts a reply draft about one item; null drafts a reply to everything.
  final ValueChanged<WhatsAppReplyItem?> onDraft;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);
    return Container(
      key: const Key('whatsapp-catch-up'),
      margin: const EdgeInsets.fromLTRB(12, 8, 12, 4),
      padding: const EdgeInsets.fromLTRB(14, 10, 4, 12),
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(PhosphorIconsRegular.sparkle, size: 16, color: colors.muted),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  catchUp.messageCount > 0
                      ? 'Catch up · ${catchUp.messageCount} unread'
                      : 'Catch up',
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: colors.muted,
                  ),
                ),
              ),
              IconButton(
                key: const Key('whatsapp-catch-up-dismiss'),
                tooltip: 'Hide catch up',
                visualDensity: VisualDensity.compact,
                onPressed: onDismiss,
                icon: Icon(
                  PhosphorIconsRegular.x,
                  size: 16,
                  color: colors.muted,
                ),
              ),
            ],
          ),
          Padding(
            padding: const EdgeInsets.only(right: 10),
            child: Text(catchUp.summary, style: theme.textTheme.bodyMedium),
          ),
          if (catchUp.toReply.isNotEmpty) ...[
            const SizedBox(height: 10),
            Text(
              'To reply',
              style: theme.textTheme.labelMedium?.copyWith(color: colors.muted),
            ),
            for (final item in catchUp.toReply)
              Padding(
                padding: const EdgeInsets.only(top: 4, right: 10),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        item.who.isEmpty
                            ? item.about
                            : '${item.who}: ${item.about}',
                        style: theme.textTheme.bodyMedium,
                      ),
                    ),
                    TextButton(
                      key: Key('whatsapp-catch-up-draft-${item.about}'),
                      onPressed: () => onDraft(item),
                      child: const Text('Draft reply'),
                    ),
                  ],
                ),
              ),
          ],
        ],
      ),
    );
  }
}

/// One row at the top of the chat list that stands for every catch-up at once.
/// It only says how much is waiting; [showCatchUpSheet] has the full text.
class CatchUpRow extends StatelessWidget {
  const CatchUpRow({
    required this.entries,
    required this.onTap,
    this.time,
    super.key,
  });

  final List<CatchUpDigestEntry> entries;
  final VoidCallback onTap;

  /// When the newest chat in the catch-up last had a message.
  final DateTime? time;

  static const _namesShown = 3;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final toReply = entries.fold(
      0,
      (total, entry) => total + entry.catchUp.toReply.length,
    );
    final names = [for (final entry in entries) entry.title];
    final shown = names.take(_namesShown).join(', ');
    final more = names.length - _namesShown;
    final who = more > 0 ? '$shown and $more more' : shown;
    final detail = [
      entries.length == 1 ? '1 chat' : '${entries.length} chats',
      if (toReply > 0) '$toReply to reply',
    ].join(' · ');
    return Semantics(
      button: true,
      label: 'Catch up, $detail, $who',
      onTap: onTap,
      excludeSemantics: true,
      child: InkWell(
        key: const Key('whatsapp-catch-up-row'),
        borderRadius: BorderRadius.circular(14),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 4),
          child: Row(
            children: [
              Container(
                width: 44,
                height: 44,
                decoration: BoxDecoration(
                  color: colors.accentSoft,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  PhosphorIconsRegular.sparkle,
                  size: 20,
                  color: colors.accentDeep,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            'Catch up',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w600,
                              letterSpacing: -.1,
                              color: colors.ink,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          whatsAppListTime(time),
                          style: TextStyle(fontSize: 12, color: colors.accent),
                        ),
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(
                      who,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontSize: 13.5, color: colors.inkSoft),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      detail,
                      style: TextStyle(fontSize: 12, color: colors.inkSoft),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Every catch-up in full, one section per chat. Tapping a section opens it.
Future<void> showCatchUpSheet(
  BuildContext context,
  List<CatchUpDigestEntry> entries,
) => showModalBottomSheet<void>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  useSafeArea: true,
  builder: (sheet) => _CatchUpSheet(
    entries: entries,
    onOpen: (entry) {
      Navigator.of(sheet).pop();
      entry.onOpen();
    },
  ),
);

class _CatchUpSheet extends StatelessWidget {
  const _CatchUpSheet({required this.entries, required this.onOpen});

  final List<CatchUpDigestEntry> entries;
  final ValueChanged<CatchUpDigestEntry> onOpen;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);
    return ConstrainedBox(
      constraints: BoxConstraints(
        maxHeight: MediaQuery.sizeOf(context).height * .85,
      ),
      child: ListView(
        key: const Key('whatsapp-catch-up-sheet'),
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
        children: [
          Row(
            children: [
              Icon(PhosphorIconsRegular.sparkle, size: 18, color: colors.muted),
              const SizedBox(width: 8),
              Text('Catch up', style: theme.textTheme.titleLarge),
            ],
          ),
          for (var i = 0; i < entries.length; i++) ...[
            if (i > 0) Divider(height: 28, color: colors.outline),
            if (i == 0) const SizedBox(height: 16),
            _CatchUpSection(entry: entries[i], onOpen: onOpen),
          ],
        ],
      ),
    );
  }
}

class _CatchUpSection extends StatelessWidget {
  const _CatchUpSection({required this.entry, required this.onOpen});

  final CatchUpDigestEntry entry;
  final ValueChanged<CatchUpDigestEntry> onOpen;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);
    final catchUp = entry.catchUp;
    return InkWell(
      key: Key('whatsapp-catch-up-${entry.id}'),
      borderRadius: BorderRadius.circular(12),
      onTap: () => onOpen(entry),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                if (entry.leading != null) ...[
                  entry.leading!,
                  const SizedBox(width: 10),
                ],
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        entry.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.titleSmall,
                      ),
                      if (catchUp.messageCount > 0)
                        Text(
                          '${catchUp.messageCount} unread',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: colors.muted,
                          ),
                        ),
                    ],
                  ),
                ),
                Icon(
                  PhosphorIconsRegular.caretRight,
                  size: 16,
                  color: colors.muted,
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              catchUp.summary,
              style: theme.textTheme.bodyMedium?.copyWith(height: 1.4),
            ),
            if (catchUp.toReply.isNotEmpty) ...[
              const SizedBox(height: 10),
              for (final item in catchUp.toReply)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Padding(
                        padding: const EdgeInsets.only(top: 3, right: 8),
                        child: Icon(
                          PhosphorIconsRegular.chatCircle,
                          size: 14,
                          color: colors.accent,
                        ),
                      ),
                      Expanded(
                        child: Text(
                          item.who.isEmpty
                              ? item.about
                              : '${item.who}: ${item.about}',
                          style: theme.textTheme.bodyMedium,
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ],
        ),
      ),
    );
  }
}

/// One chat in [CatchUpDigest] and [showCatchUpSheet].
class CatchUpDigestEntry {
  const CatchUpDigestEntry({
    required this.id,
    required this.title,
    required this.catchUp,
    required this.onOpen,
    this.leading,
  });

  final String id;
  final String title;
  final WhatsAppCatchUp catchUp;
  final VoidCallback onOpen;
  final Widget? leading;
}

/// Catch-ups from every read-along chat in one card. Hidden when there are none.
class CatchUpDigest extends StatelessWidget {
  const CatchUpDigest({
    required this.entries,
    this.margin = const EdgeInsets.fromLTRB(16, 8, 16, 4),
    super.key,
  });

  final List<CatchUpDigestEntry> entries;
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context) {
    if (entries.isEmpty) return const SizedBox.shrink();
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);
    return Container(
      key: const Key('whatsapp-catch-up-digest'),
      margin: margin,
      padding: const EdgeInsets.symmetric(vertical: 8),
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 4, 14, 4),
            child: Row(
              children: [
                Icon(
                  PhosphorIconsRegular.sparkle,
                  size: 16,
                  color: colors.muted,
                ),
                const SizedBox(width: 6),
                Text(
                  'Catch up',
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: colors.muted,
                  ),
                ),
              ],
            ),
          ),
          for (final entry in entries)
            InkWell(
              key: Key('whatsapp-catch-up-${entry.id}'),
              onTap: entry.onOpen,
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 14,
                  vertical: 8,
                ),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (entry.leading != null) ...[
                      entry.leading!,
                      const SizedBox(width: 10),
                    ],
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            entry.title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.titleSmall,
                          ),
                          Text(
                            entry.catchUp.summary,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: colors.inkSoft,
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (entry.catchUp.toReply.isNotEmpty) ...[
                      const SizedBox(width: 8),
                      Chip(
                        visualDensity: VisualDensity.compact,
                        label: Text('${entry.catchUp.toReply.length} to reply'),
                      ),
                    ],
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}
