import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/tile_card.dart' show tileRadius;

const _seenKey = 'jarvis.home.learning_seen_at.v1';

/// Remembers, on this phone, up to when the person has seen what Jarvis changed about itself, so "Got it" and opening
/// Learning put the Home card away until something new happens.
abstract final class LearningSeenStore {
  static Future<DateTime?> read() async {
    final raw = (await SharedPreferences.getInstance()).getString(_seenKey);
    return raw == null ? null : DateTime.tryParse(raw);
  }

  static Future<void> mark(DateTime when) async =>
      (await SharedPreferences.getInstance()).setString(
        _seenKey,
        when.toUtc().toIso8601String(),
      );

  static Future<void> clear() async =>
      (await SharedPreferences.getInstance()).remove(_seenKey);
}

/// One thing Jarvis changed by itself, from `/home` `learning.changes`.
@immutable
class LearningChange {
  const LearningChange({
    required this.id,
    required this.kind,
    required this.title,
    required this.detail,
    required this.action,
    required this.target,
    required this.at,
  });

  final String id;
  final String kind;
  final String title;
  final String detail;

  /// `undo`, `disable_skill`, `enable_skill` or `open_skill`.
  final String action;
  final String target;
  final DateTime at;

  String get actionLabel => switch (action) {
    'enable_skill' => 'Turn on',
    'open_skill' => 'Open',
    _ => 'Undo',
  };

  static List<LearningChange> fromBriefing(
    Map<String, dynamic>? briefing, {
    DateTime? seenAt,
  }) => [
    for (final change in jsonMaps(
      jsonObject(briefing?['learning'])?['changes'],
    ))
      if ((
            asJsonString(change['id']),
            asJsonString(change['title']),
            asJsonString(change['action']),
            asJsonString(change['target']),
            jsonDate(change['at']),
          )
          case (
            final id?,
            final title?,
            final action?,
            final target?,
            final at?,
          ) when seenAt == null || at.isAfter(seenAt))
        LearningChange(
          id: id,
          kind: asJsonString(change['kind']) ?? '',
          title: title,
          detail: asJsonString(change['detail']) ?? '',
          action: action,
          target: target,
          at: at,
        ),
  ];
}

/// "Jarvis is learning": what Jarvis changed about itself this week, each line with the one step that reverses it.
/// Decisions only the person can make are not here; they are Needs you rows. It never takes the dark focal style.
class LearningCard extends StatelessWidget {
  const LearningCard({
    required this.changes,
    required this.total,
    required this.onAction,
    required this.onOpenLearning,
    required this.onSeen,
    this.pending = const {},
    super.key,
  });

  final List<LearningChange> changes;

  /// All changes this week, which can be more than the lines shown.
  final int total;
  final ValueChanged<LearningChange> onAction;
  final VoidCallback onOpenLearning;
  final VoidCallback onSeen;
  final Set<String> pending;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final count = total < changes.length ? changes.length : total;
    final more = count - changes.length;
    return Container(
      key: const Key('home-learning'),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(tileRadius),
        boxShadow: JarvisShadows.soft(colors.brightness),
      ),
      padding: const EdgeInsets.fromLTRB(16, 16, 12, 6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              _Badge(
                icon: PhosphorIconsRegular.magicWand,
                size: 44,
                tint: colors.accent,
                wash: colors.accentSoft,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Semantics(
                      header: true,
                      child: Text(
                        'JARVIS IS LEARNING',
                        style: JarvisType.capsOf(
                          context,
                        ).copyWith(color: colors.accent),
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      count == 1
                          ? '1 change this week'
                          : '$count changes this week',
                      style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w600,
                        letterSpacing: -.25,
                        color: colors.ink,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          for (final change in changes)
            _ChangeRow(
              change: change,
              busy: pending.contains(change.id),
              onAction: () => onAction(change),
            ),
          Divider(height: 14, color: colors.outline),
          // Wraps so "Got it" and the link stay whole at large text.
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              TextButton(
                key: const Key('home-learning-seen'),
                onPressed: onSeen,
                style: TextButton.styleFrom(
                  foregroundColor: colors.inkSoft,
                  textStyle: const TextStyle(
                    fontFamily: 'Geist',
                    fontSize: 13.5,
                    fontWeight: FontWeight.w500,
                  ),
                ),
                child: const Text('Got it'),
              ),
              TextButton(
                key: const Key('home-learning-open'),
                onPressed: onOpenLearning,
                style: TextButton.styleFrom(
                  foregroundColor: colors.ink,
                  textStyle: const TextStyle(
                    fontFamily: 'Geist',
                    fontSize: 13.5,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                child: Text(more > 0 ? '$more more in Learning ›' : 'Learning ›'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _ChangeRow extends StatelessWidget {
  const _ChangeRow({
    required this.change,
    required this.busy,
    required this.onAction,
  });

  final LearningChange change;
  final bool busy;
  final VoidCallback onAction;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final (icon, tint, wash) = switch (change.kind) {
      'skill_learned' || 'skill_improved' => (
        PhosphorIconsRegular.magicWand,
        colors.accent,
        colors.accentSoft,
      ),
      'lesson' => (PhosphorIconsRegular.lightbulb, colors.info, colors.infoSoft),
      'memory' => (PhosphorIconsRegular.brain, colors.info, colors.infoSoft),
      'skill_retired' => (
        PhosphorIconsRegular.prohibit,
        colors.inkSoft,
        colors.surfaceMuted,
      ),
      _ => (PhosphorIconsRegular.sparkle, colors.accent, colors.accentSoft),
    };
    final large = MediaQuery.textScalerOf(context).scale(1) >= 1.5;
    final button = busy
        ? const Padding(
            padding: EdgeInsets.all(10),
            child: SizedBox(
              width: 18,
              height: 18,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
          )
        : TextButton(
            key: Key('home-learning-action-${change.id}'),
            onPressed: onAction,
            style: TextButton.styleFrom(
              foregroundColor: colors.accent,
              minimumSize: const Size(44, 44),
              textStyle: const TextStyle(
                fontFamily: 'Geist',
                fontSize: 13.5,
                fontWeight: FontWeight.w600,
              ),
            ),
            child: Semantics(
              label: '${change.actionLabel}: ${change.title}',
              excludeSemantics: true,
              child: Text(change.actionLabel),
            ),
          );
    final text = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          change.title,
          maxLines: large ? 3 : 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            fontSize: 14.5,
            fontWeight: FontWeight.w500,
            color: colors.ink,
          ),
        ),
        if (change.detail.isNotEmpty)
          Text(
            change.detail,
            maxLines: large ? 3 : 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 13, color: colors.inkSoft),
          ),
      ],
    );
    return Padding(
      key: Key('home-learning-change-${change.id}'),
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: large
          // At large text the button moves under the words instead of squeezing them (playbook: stack from 1.5×).
          ? Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    _Badge(icon: icon, size: 28, tint: tint, wash: wash),
                    const SizedBox(width: 12),
                    Expanded(child: text),
                  ],
                ),
                Padding(
                  padding: const EdgeInsets.only(left: 32),
                  child: button,
                ),
              ],
            )
          : Row(
              children: [
                _Badge(icon: icon, size: 28, tint: tint, wash: wash),
                const SizedBox(width: 12),
                Expanded(child: text),
                const SizedBox(width: 4),
                button,
              ],
            ),
    );
  }
}

class _Badge extends StatelessWidget {
  const _Badge({
    required this.icon,
    required this.size,
    required this.tint,
    required this.wash,
  });

  final IconData icon;
  final double size;
  final Color tint;
  final Color wash;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      color: wash,
      shape: size >= 40 ? BoxShape.circle : BoxShape.rectangle,
      borderRadius: size >= 40 ? null : BorderRadius.circular(9),
    ),
    child: Icon(icon, size: size * .5, color: tint),
  );
}
