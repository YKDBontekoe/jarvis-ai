import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/tile_card.dart' show tileRadius;
import 'home_context.dart';

/// "Jarvis noticed": one pattern at a time, with the evidence for it. Swipe or
/// tap the dots for the next one; each can be acted on or waved away.
class NoticedCard extends StatefulWidget {
  const NoticedCard({
    required this.insights,
    required this.onAction,
    required this.onDismiss,
    super.key,
  });

  final List<HomeInsight> insights;
  final ValueChanged<HomeInsight> onAction;
  final ValueChanged<HomeInsight> onDismiss;

  @override
  State<NoticedCard> createState() => _NoticedCardState();
}

class _NoticedCardState extends State<NoticedCard> {
  int _index = 0;

  /// Waved away here, hidden before Home reloads.
  final Set<String> _gone = {};

  List<HomeInsight> get _visible => [
    for (final item in widget.insights)
      if (!_gone.contains(item.id)) item,
  ];

  void _go(int delta) {
    final count = _visible.length;
    if (count < 2) return;
    unawaited(HapticFeedback.selectionClick());
    setState(() => _index = (_index + delta) % count);
  }

  void _dismiss(HomeInsight item) {
    unawaited(HapticFeedback.lightImpact());
    setState(() {
      _gone.add(item.id);
      final count = _visible.length;
      if (count > 0 && _index >= count) _index = count - 1;
    });
    widget.onDismiss(item);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final visible = _visible;
    if (visible.isEmpty) return const SizedBox.shrink();
    final index = _index.clamp(0, visible.length - 1);
    final item = visible[index];
    final look = _look(item.kind, colors);
    return Column(
      key: const Key('home-noticed'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 0, 4, 10),
          child: Row(
            children: [
              Expanded(
                child: Semantics(
                  header: true,
                  child: Text(
                    'Jarvis noticed',
                    style: TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.w600,
                      letterSpacing: -.5,
                      color: colors.ink,
                    ),
                  ),
                ),
              ),
              if (visible.length > 1)
                for (var i = 0; i < visible.length; i++)
                  GestureDetector(
                    onTap: () => setState(() => _index = i),
                    child: AnimatedContainer(
                      duration: JarvisMotion.of(context, JarvisMotion.base),
                      curve: JarvisMotion.emphasized,
                      margin: const EdgeInsets.only(left: 5),
                      width: i == index ? 18 : 6,
                      height: 6,
                      decoration: BoxDecoration(
                        color: i == index ? colors.accent : colors.outline,
                        borderRadius: BorderRadius.circular(99),
                      ),
                    ),
                  ),
            ],
          ),
        ),
        GestureDetector(
          onHorizontalDragEnd: (details) {
            final velocity = details.primaryVelocity ?? 0;
            if (velocity.abs() < 200) return;
            _go(velocity < 0 ? 1 : -1);
          },
          child: Container(
            decoration: BoxDecoration(
              color: colors.surface,
              borderRadius: BorderRadius.circular(tileRadius),
              boxShadow: JarvisShadows.soft(colors.brightness),
            ),
            padding: const EdgeInsets.fromLTRB(18, 18, 14, 14),
            child: MotionSwitcher(
              resize: true,
              child: Column(
                key: ValueKey(item.id),
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        width: 40,
                        height: 40,
                        decoration: BoxDecoration(
                          color: look.$2,
                          borderRadius: BorderRadius.circular(13),
                        ),
                        child: Icon(look.$1, size: 20, color: look.$3),
                      ),
                      const SizedBox(width: 14),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              item.title,
                              key: Key('home-noticed-${item.kind}'),
                              style: TextStyle(
                                fontSize: 16.5,
                                height: 1.3,
                                fontWeight: FontWeight.w600,
                                letterSpacing: -.3,
                                color: colors.ink,
                              ),
                            ),
                            if (item.evidence.isNotEmpty) ...[
                              const SizedBox(height: 4),
                              Text(
                                item.evidence,
                                style: TextStyle(
                                  fontSize: 13.5,
                                  height: 1.35,
                                  color: colors.inkSoft,
                                ),
                              ),
                            ],
                            if (item.confidence case final confidence?) ...[
                              const SizedBox(height: 10),
                              _Confidence(confidence),
                            ],
                          ],
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 14),
                  Padding(
                    padding: const EdgeInsets.only(left: 54),
                    child: Wrap(
                      spacing: 6,
                      runSpacing: 4,
                      children: [
                        FilledButton(
                          key: const Key('home-noticed-action'),
                          onPressed: () {
                            unawaited(HapticFeedback.lightImpact());
                            widget.onAction(item);
                          },
                          style: FilledButton.styleFrom(
                            backgroundColor: colors.ink,
                            foregroundColor: colors.onInk,
                            minimumSize: const Size(44, 40),
                            padding: const EdgeInsets.symmetric(horizontal: 16),
                            textStyle: const TextStyle(
                              fontFamily: 'Geist',
                              fontSize: 14,
                              fontWeight: FontWeight.w600,
                            ),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(12),
                            ),
                          ),
                          child: Text(item.actionLabel),
                        ),
                        TextButton(
                          key: const Key('home-noticed-dismiss'),
                          onPressed: () => _dismiss(item),
                          style: TextButton.styleFrom(
                            foregroundColor: colors.inkSoft,
                            minimumSize: const Size(44, 40),
                            textStyle: const TextStyle(
                              fontFamily: 'Geist',
                              fontSize: 14,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                          child: const Text('Not useful'),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }

  static (IconData, Color, Color) _look(String kind, JarvisColors colors) =>
      switch (kind) {
        'routine' => (
          PhosphorIconsRegular.repeat,
          colors.accentSoft,
          colors.accent,
        ),
        'drift' => (
          PhosphorIconsRegular.heart,
          colors.rose.withValues(alpha: colors.isDark ? .18 : .1),
          colors.rose,
        ),
        'birthday' => (
          PhosphorIconsRegular.gift,
          colors.warningSoft,
          colors.warning,
        ),
        _ => (PhosphorIconsRegular.sparkle, colors.accentSoft, colors.accent),
      };
}

/// How sure Jarvis is of a pattern, as a short bar.
class _Confidence extends StatelessWidget {
  const _Confidence(this.value);

  final double value;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final percent = (value.clamp(0, 1) * 100).round();
    return Semantics(
      label: '$percent percent sure',
      excludeSemantics: true,
      child: Row(
        children: [
          SizedBox(
            width: 64,
            height: 4,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(99),
              child: LinearProgressIndicator(
                value: value.clamp(0, 1).toDouble(),
                backgroundColor: colors.surfaceMuted,
                color: colors.accent,
              ),
            ),
          ),
          const SizedBox(width: 8),
          Flexible(
            child: Text(
              '$percent% sure',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 12, color: colors.muted),
            ),
          ),
        ],
      ),
    );
  }
}
