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

/// In Customize Home: a switch per card above the tiles, and a way to let
/// Jarvis arrange Home from what it knows.
class HomeCardsEditor extends StatelessWidget {
  const HomeCardsEditor({
    required this.isShown,
    required this.onChanged,
    this.onAskJarvis,
    super.key,
  });

  final bool Function(String card) isShown;
  final void Function(String card, bool visible) onChanged;

  /// Ends editing and asks Jarvis for a layout; null while chat is busy.
  final VoidCallback? onAskJarvis;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Column(
      key: const Key('home-cards-editor'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const GroupLabel('Cards on Home'),
        Material(
          color: colors.surface,
          borderRadius: BorderRadius.circular(tileRadius),
          clipBehavior: Clip.antiAlias,
          child: Column(
            children: [
              for (final (index, entry) in HomeCardIds.all.entries.indexed) ...[
                if (index > 0)
                  Divider(height: 1, indent: 18, color: colors.outline),
                SwitchListTile(
                  key: Key('home-card-${entry.key}'),
                  value: isShown(entry.key),
                  onChanged: (visible) {
                    HapticFeedback.selectionClick();
                    onChanged(entry.key, visible);
                  },
                  contentPadding: const EdgeInsets.symmetric(horizontal: 18),
                  title: Text(
                    entry.value,
                    style: TextStyle(
                      fontFamily: 'Geist',
                      fontSize: 15.5,
                      fontWeight: FontWeight.w600,
                      color: colors.ink,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.only(top: 8, left: 4, right: 4),
          child: Text(
            'Needs you and the next thing on your day always show.',
            style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
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
              'Or just say “put my habits first” or “hide the morning brief” in any chat.',
              style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
            ),
          ),
        ],
      ],
    );
  }
}
