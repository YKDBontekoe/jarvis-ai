import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/home_layout_sync.dart';
import '../tiles/tile_card.dart' show tileRadius;

/// What Home sends Jarvis from "Ask Jarvis to arrange it".
const arrangeHomePrompt =
    'Help me make my Home screen more personal. Look at what I use most and '
    'what matters to me, then suggest which tiles and cards to show, in what '
    'order and size. Ask before you change it.';

/// Says above the tiles that Jarvis changed Home, with its reason, and lets the
/// owner keep it or put back what was there. Calm surface, never the focal
/// style: the change already happened at the owner's request.
class JarvisHomeChangeCard extends StatelessWidget {
  const JarvisHomeChangeCard({
    required this.change,
    required this.onKeep,
    required this.onUndo,
    this.undoing = false,
    super.key,
  });

  final JarvisHomeChange change;
  final VoidCallback onKeep;
  final VoidCallback onUndo;
  final bool undoing;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final note = change.note;
    return Container(
      key: const Key('home-jarvis-change'),
      padding: const EdgeInsets.fromLTRB(18, 14, 18, 14),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(tileRadius),
        border: Border.all(color: colors.accent.withValues(alpha: .18)),
      ),
      child: Semantics(
        container: true,
        liveRegion: true,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  PhosphorIconsRegular.sparkle,
                  size: 16,
                  color: colors.accent,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'JARVIS CHANGED HOME',
                    style: JarvisType.capsOf(context)
                        .copyWith(color: colors.accentDeep),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              note == null || note.isEmpty
                  ? 'Your tiles and cards were rearranged as you asked.'
                  : note,
              style: TextStyle(fontSize: 15, height: 1.4, color: colors.ink),
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                PillAction(
                  key: const Key('home-jarvis-change-keep'),
                  label: 'Keep it',
                  primary: true,
                  compact: true,
                  onPressed: undoing
                      ? null
                      : () {
                          HapticFeedback.selectionClick();
                          onKeep();
                        },
                ),
                if (change.canUndo)
                  PillAction(
                    key: const Key('home-jarvis-change-undo'),
                    label: undoing ? 'Undoing…' : 'Undo',
                    icon: PhosphorIconsRegular.arrowBendUpLeft,
                    compact: true,
                    onPressed: undoing
                        ? null
                        : () {
                            HapticFeedback.selectionClick();
                            onUndo();
                          },
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// In Customize Home: Home's sections in order, dragged to reorder, with a
/// switch on each card that can be put away; the Ask suggestions switch; and a
/// way to let Jarvis arrange Home from what it knows.
class HomeCardsEditor extends StatelessWidget {
  const HomeCardsEditor({
    required this.sections,
    required this.isShown,
    required this.onChanged,
    required this.onMove,
    this.onAskJarvis,
    super.key,
  });

  /// Home's sections top to bottom ([HomeSectionIds]).
  final List<String> sections;
  final bool Function(String card) isShown;
  final void Function(String card, bool visible) onChanged;

  /// Moves the section at `from` to `to`, both in [sections].
  final void Function(int from, int to) onMove;

  /// Ends editing and asks Jarvis for a layout; null while chat is busy.
  final VoidCallback? onAskJarvis;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final title = TextStyle(
      fontFamily: 'Geist',
      fontSize: 15.5,
      fontWeight: FontWeight.w600,
      color: colors.ink,
    );
    final note = TextStyle(fontSize: 12.5, color: colors.inkSoft);

    Widget row(int index, String id) {
      final hideable = HomeCardIds.all.containsKey(id);
      final name = HomeSectionIds.names[id] ?? id;
      return Material(
        key: ValueKey('home-section-$id'),
        color: colors.surface,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (index > 0)
              Divider(height: 1, indent: 18, color: colors.outline),
            ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 56),
              child: Row(
                children: [
                  ReorderableDragStartListener(
                    index: index,
                    child: Semantics(
                      label: 'Drag to move $name',
                      child: SizedBox(
                        width: 48,
                        height: 48,
                        child: Icon(
                          PhosphorIconsRegular.list,
                          size: 18,
                          color: colors.muted,
                        ),
                      ),
                    ),
                  ),
                  Expanded(
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 8),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(name, style: title),
                          if (!hideable)
                            Text(
                              id == HomeSectionIds.tiles
                                  ? 'Your tiles'
                                  : 'Always shows',
                              style: note,
                            ),
                        ],
                      ),
                    ),
                  ),
                  if (hideable)
                    Switch(
                      key: Key('home-card-$id'),
                      value: isShown(id),
                      onChanged: (visible) {
                        HapticFeedback.selectionClick();
                        onChanged(id, visible);
                      },
                    ),
                  const SizedBox(width: 12),
                ],
              ),
            ),
          ],
        ),
      );
    }

    return Column(
      key: const Key('home-cards-editor'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const GroupLabel('Order and cards'),
        ClipRRect(
          borderRadius: BorderRadius.circular(tileRadius),
          child: ReorderableListView(
            key: const Key('home-sections'),
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            buildDefaultDragHandles: false,
            onReorderStart: (_) => HapticFeedback.mediumImpact(),
            onReorderItem: (from, to) {
              HapticFeedback.selectionClick();
              onMove(from, to);
            },
            children: [
              for (final (index, id) in sections.indexed) row(index, id),
            ],
          ),
        ),
        const SizedBox(height: 12),
        Material(
          color: colors.surface,
          borderRadius: BorderRadius.circular(tileRadius),
          clipBehavior: Clip.antiAlias,
          child: SwitchListTile(
            key: const Key('home-card-${HomeCardIds.suggestions}'),
            value: isShown(HomeCardIds.suggestions),
            onChanged: (visible) {
              HapticFeedback.selectionClick();
              onChanged(HomeCardIds.suggestions, visible);
            },
            contentPadding: const EdgeInsets.symmetric(horizontal: 18),
            title: Text(
              HomeCardIds.all[HomeCardIds.suggestions]!,
              style: title,
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.only(top: 8, left: 4, right: 4),
          child: Text(
            'Drag a row by its handle to change the order. The greeting and the Ask bar stay on top.',
            style: note,
          ),
        ),
        if (onAskJarvis != null) ...[
          const SizedBox(height: 16),
          Align(
            alignment: Alignment.centerLeft,
            child: PillAction(
              key: const Key('home-ask-jarvis-arrange'),
              label: 'Ask Jarvis to arrange it',
              icon: PhosphorIconsRegular.sparkle,
              onPressed: onAskJarvis,
            ),
          ),
          Padding(
            padding: const EdgeInsets.only(top: 8, left: 4, right: 4),
            child: Text(
              'Or just say “put my tiles first” or “hide the morning brief” in any chat.',
              style: note,
            ),
          ),
        ],
      ],
    );
  }
}
