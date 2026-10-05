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

/// One chat in [CatchUpDigest].
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
