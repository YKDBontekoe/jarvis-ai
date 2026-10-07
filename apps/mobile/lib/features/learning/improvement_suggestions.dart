import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// "Suggestions to review": changes Jarvis would like to make to itself, and the few low-risk memories it already
/// saved on its own, which stay undoable. Nothing waiting here changes anything until the owner accepts it.
class ImprovementSuggestionsSection extends StatelessWidget {
  const ImprovementSuggestionsSection({
    required this.items,
    required this.busy,
    required this.onAccept,
    required this.onDismiss,
    required this.onUndo,
    super.key,
  });

  final List<Map<String, dynamic>> items;
  final Set<String> busy;
  final void Function(String id) onAccept;
  final void Function(String id) onDismiss;
  final void Function(String id) onUndo;

  @override
  Widget build(BuildContext context) {
    final waiting = items
        .where((item) => asJsonString(item['status']) == 'pending')
        .toList();
    final done = items
        .where(
          (item) =>
              asJsonString(item['status']) != 'pending' &&
              asJsonBool(item['canUndo']),
        )
        .toList();
    if (waiting.isEmpty && done.isEmpty) return const SizedBox.shrink();
    return Column(
      key: const Key('improvement-suggestions'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (waiting.isNotEmpty) ...[
          const SectionHeader('Suggestions to review'),
          for (final item in waiting)
            _WaitingCard(
              item: item,
              busy: busy.contains(jsonId(item) ?? ''),
              onAccept: onAccept,
              onDismiss: onDismiss,
            ),
        ],
        if (done.isNotEmpty) ...[
          const SectionHeader('Jarvis did this'),
          for (final item in done)
            _DoneCard(
              item: item,
              busy: busy.contains(jsonId(item) ?? ''),
              onUndo: onUndo,
            ),
        ],
        const SizedBox(height: 8),
      ],
    );
  }
}

String _acceptLabel(String kind) => switch (kind) {
  'memory' => 'Remember it',
  'skill' => 'Save skill',
  _ => 'Accept',
};

IconData _icon(String kind) => switch (kind) {
  'memory' => PhosphorIconsRegular.brain,
  'skill' => PhosphorIconsRegular.magicWand,
  _ => PhosphorIconsRegular.lightbulb,
};

class _WaitingCard extends StatelessWidget {
  const _WaitingCard({
    required this.item,
    required this.busy,
    required this.onAccept,
    required this.onDismiss,
  });

  final Map<String, dynamic> item;
  final bool busy;
  final void Function(String id) onAccept;
  final void Function(String id) onDismiss;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final id = jsonId(item) ?? '';
    final kind = asJsonString(item['kind']) ?? 'review';
    final evidence = asJsonString(item['evidence']) ?? '';
    return SurfaceCard(
      key: Key('improvement-$id'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(icon: _icon(kind)),
              const SizedBox(width: 14),
              Expanded(
                child: Text(
                  asJsonString(item['title']) ?? 'Suggestion',
                  style: text.titleSmall?.copyWith(fontSize: 15),
                ),
              ),
            ],
          ),
          if (evidence.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(
              evidence,
              style: text.bodySmall?.copyWith(color: colors.inkSoft),
            ),
          ],
          const SizedBox(height: 8),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              TextButton(
                key: Key('improvement-dismiss-$id'),
                onPressed: busy || id.isEmpty ? null : () => onDismiss(id),
                child: const Text('Not for me'),
              ),
              const SizedBox(width: 8),
              FilledButton.tonal(
                key: Key('improvement-accept-$id'),
                onPressed: busy || id.isEmpty ? null : () => onAccept(id),
                style: FilledButton.styleFrom(minimumSize: const Size(0, 36)),
                child: Text(_acceptLabel(kind)),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _DoneCard extends StatelessWidget {
  const _DoneCard({
    required this.item,
    required this.busy,
    required this.onUndo,
  });

  final Map<String, dynamic> item;
  final bool busy;
  final void Function(String id) onUndo;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final id = jsonId(item) ?? '';
    final kind = asJsonString(item['kind']) ?? 'review';
    final automatic = asJsonString(item['status']) == 'applied';
    return SurfaceCard(
      key: Key('improvement-$id'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
      child: Row(
        children: [
          IconBadge(icon: _icon(kind)),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  asJsonString(item['title']) ?? 'Change',
                  style: text.titleSmall?.copyWith(fontSize: 15),
                ),
                const SizedBox(height: 2),
                Text(
                  automatic ? 'Saved automatically' : 'Accepted',
                  style: text.bodySmall?.copyWith(color: colors.muted),
                ),
              ],
            ),
          ),
          TextButton(
            key: Key('improvement-undo-$id'),
            onPressed: busy || id.isEmpty ? null : () => onUndo(id),
            child: const Text('Undo'),
          ),
        ],
      ),
    );
  }
}
