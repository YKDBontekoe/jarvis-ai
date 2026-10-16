import 'dart:async';
import 'dart:math' as math;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/tile_card.dart' show tileRadius;

/// One thing Jarvis can learn from, and whether it already does.
@immutable
class DayOneStep {
  const DayOneStep({
    required this.id,
    required this.label,
    required this.unlocks,
    required this.icon,
    required this.done,
    required this.onTap,
  });

  final String id;
  final String label;

  /// What connecting it lets Jarvis do, in a few words.
  final String unlocks;
  final IconData icon;
  final bool done;
  final VoidCallback onTap;
}

/// The first days: how well Jarvis knows you, what to connect next and what
/// each connection unlocks, and one quick question about the morning brief.
class DayOneCard extends StatefulWidget {
  const DayOneCard({required this.steps, this.http, super.key});

  final List<DayOneStep> steps;

  /// Reads and saves the brief time. Without it the question is left out.
  final Dio? http;

  /// Shown while fewer than two of the steps are done.
  static bool shows(List<DayOneStep> steps) =>
      steps.where((step) => step.done).length < 2;

  @override
  State<DayOneCard> createState() => _DayOneCardState();
}

class _DayOneCardState extends State<DayOneCard> {
  static const _times = ['07:00', '08:00', '09:00'];

  Map<String, dynamic>? _brief;
  String? _saving;

  String? get _time {
    final value = asJsonString(_brief?['localTime']);
    if (value == null || !asJsonBool(_brief?['enabled'])) return null;
    return value.length >= 5 ? value.substring(0, 5) : value;
  }

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final http = widget.http;
    if (http == null) return;
    try {
      final response = await http.get<dynamic>('/api/v1/briefings/daily');
      if (mounted) setState(() => _brief = jsonObject(response.data));
    } catch (_) {
      // The question waits for a connection.
    }
  }

  Future<void> _pick(String time) async {
    final http = widget.http;
    final brief = _brief;
    if (http == null || brief == null || _saving != null) return;
    unawaited(HapticFeedback.selectionClick());
    final before = brief;
    setState(() {
      _saving = time;
      _brief = {...brief, 'enabled': true, 'localTime': '$time:00'};
    });
    try {
      final response = await http.put<dynamic>(
        '/api/v1/briefings/daily',
        data: {
          'enabled': true,
          'localTime': '$time:00',
          'timeZoneId': asJsonString(brief['timeZoneId']) ?? 'UTC',
        },
      );
      if (!mounted) return;
      setState(() => _brief = jsonObject(response.data) ?? _brief);
    } catch (_) {
      if (!mounted) return;
      setState(() => _brief = before);
      ScaffoldMessenger.maybeOf(context)?.showSnackBar(
        const SnackBar(content: Text('Could not save the brief time')),
      );
    } finally {
      if (mounted) setState(() => _saving = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final steps = widget.steps;
    final done = steps.where((step) => step.done).length;
    final progress = steps.isEmpty ? 0.0 : done / steps.length;
    return Container(
      key: const Key('home-day-one'),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(tileRadius),
        boxShadow: JarvisShadows.soft(colors.brightness),
      ),
      padding: const EdgeInsets.fromLTRB(18, 18, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              _Ring(progress: progress),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Getting to know you',
                      style: TextStyle(
                        fontSize: 18,
                        fontWeight: FontWeight.w600,
                        letterSpacing: -.4,
                        color: colors.ink,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      'Each connection makes Home more yours',
                      style: TextStyle(fontSize: 13, color: colors.inkSoft),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          for (final step in steps)
            InkWell(
              key: Key('home-day-one-${step.id}'),
              borderRadius: BorderRadius.circular(14),
              onTap: step.done ? null : step.onTap,
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 9),
                child: Row(
                  children: [
                    AnimatedContainer(
                      duration: JarvisMotion.of(context, JarvisMotion.base),
                      width: 36,
                      height: 36,
                      decoration: BoxDecoration(
                        color: step.done
                            ? colors.accentSoft
                            : colors.surfaceMuted,
                        borderRadius: BorderRadius.circular(11),
                      ),
                      child: Icon(
                        step.done ? PhosphorIconsRegular.check : step.icon,
                        size: 18,
                        color: step.done ? colors.accent : colors.inkSoft,
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            step.label,
                            style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w500,
                              color: step.done ? colors.muted : colors.ink,
                            ),
                          ),
                          Text(
                            step.unlocks,
                            style: TextStyle(
                              fontSize: 12.5,
                              color: colors.muted,
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (!step.done)
                      Icon(
                        PhosphorIconsRegular.caretRight,
                        size: 16,
                        color: colors.muted,
                      ),
                  ],
                ),
              ),
            ),
          if (widget.http != null && _brief != null) ...[
            const SizedBox(height: 8),
            Divider(height: 1, color: colors.outline),
            const SizedBox(height: 12),
            Text(
              'When should your morning brief arrive?',
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w500,
                color: colors.ink,
              ),
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final time in _times)
                  ChoiceChip(
                    key: Key('home-day-one-brief-$time'),
                    label: Text(time),
                    selected: _time == time,
                    showCheckmark: false,
                    onSelected: (_) => unawaited(_pick(time)),
                    labelStyle: TextStyle(
                      fontFamily: 'Geist',
                      fontWeight: FontWeight.w600,
                      fontFeatures: const [FontFeature.tabularFigures()],
                      color: _time == time ? colors.onInk : colors.ink,
                    ),
                    selectedColor: colors.ink,
                    backgroundColor: colors.surfaceMuted,
                    side: BorderSide.none,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(12),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 6),
          ],
        ],
      ),
    );
  }
}

/// How far along Jarvis is, as a ring that fills when a step completes.
class _Ring extends StatelessWidget {
  const _Ring({required this.progress});

  final double progress;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      label: '${(progress * 100).round()} percent set up',
      excludeSemantics: true,
      child: TweenAnimationBuilder<double>(
        tween: Tween(end: progress),
        duration: JarvisMotion.of(context, JarvisMotion.slow),
        curve: JarvisMotion.emphasized,
        builder: (context, value, _) => CustomPaint(
          size: const Size.square(48),
          painter: _RingPainter(
            value: value,
            track: colors.surfaceMuted,
            fill: colors.accent,
          ),
          child: SizedBox.square(
            dimension: 48,
            child: Center(
              child: Text(
                '${(value * 100).round()}%',
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w700,
                  fontFeatures: const [FontFeature.tabularFigures()],
                  color: colors.ink,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _RingPainter extends CustomPainter {
  const _RingPainter({
    required this.value,
    required this.track,
    required this.fill,
  });

  final double value;
  final Color track;
  final Color fill;

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 4.5
      ..strokeCap = StrokeCap.round;
    final inset = rect.deflate(3);
    canvas.drawArc(inset, 0, math.pi * 2, false, paint..color = track);
    if (value > 0) {
      canvas.drawArc(
        inset,
        -math.pi / 2,
        math.pi * 2 * value,
        false,
        paint..color = fill,
      );
    }
  }

  @override
  bool shouldRepaint(_RingPainter old) =>
      old.value != value || old.track != track || old.fill != fill;
}
