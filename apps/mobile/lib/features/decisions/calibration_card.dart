import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import 'decision_format.dart';

/// How well the owner's confidence matches reality: the Brier score with a plain reading, and for each confidence
/// band what they said against how often it happened. Hidden until a decision has been answered.
class CalibrationCard extends StatelessWidget {
  const CalibrationCard({required this.report, super.key});

  final Map<String, dynamic>? report;

  static double? _number(Object? value) =>
      value is num ? value.toDouble() : null;

  @override
  Widget build(BuildContext context) {
    final data = report;
    final resolved = asJsonInt(data?['resolved']);
    if (data == null || resolved == 0) return const SizedBox.shrink();
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final brier = _number(data['brier']);
    final said = _number(data['meanPredicted']);
    final happened = _number(data['hitRate']);
    final trend = asJsonString(data['trend']);
    final buckets = [
      for (final bucket in jsonMaps(data['buckets']))
        if (asJsonInt(bucket['count']) > 0) bucket,
    ];

    return SurfaceCard(
      key: const Key('calibration-card'),
      margin: const EdgeInsets.only(bottom: 14),
      borderColor: colors.outline,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(
                brier == null ? '–' : brier.toStringAsFixed(2),
                key: const Key('calibration-brier'),
                style: text.headlineMedium,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.only(bottom: 4),
                  child: Text(
                    brier == null ? 'Brier score' : brierVerdict(brier),
                    style: text.titleSmall,
                  ),
                ),
              ),
              if (trend != null)
                StatusPill(
                  key: const Key('calibration-trend'),
                  label: trendLabel(trend),
                  color: switch (trend) {
                    'improving' => colors.success,
                    'worsening' => colors.warning,
                    _ => colors.muted,
                  },
                ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            said == null || happened == null
                ? '$resolved answered'
                : '$resolved answered · you were ${percentLabel(said)} sure on '
                      'average and ${percentLabel(happened)} came true',
            style: TextStyle(fontSize: 13, color: colors.inkSoft),
          ),
          Text(
            'Lower is better. Always saying 50% scores 0.25.',
            style: TextStyle(fontSize: 12, color: colors.muted),
          ),
          if (buckets.isNotEmpty) ...[
            const SizedBox(height: 14),
            Row(
              children: [
                _Legend(color: colors.accent, label: 'You said'),
                const SizedBox(width: 14),
                _Legend(color: colors.success, label: 'Happened'),
              ],
            ),
            const SizedBox(height: 8),
            for (var i = 0; i < buckets.length; i++)
              _BucketRow(key: Key('calibration-bucket-$i'), bucket: buckets[i]),
          ],
        ],
      ),
    );
  }
}

class _Legend extends StatelessWidget {
  const _Legend({required this.color, required this.label});

  final Color color;
  final String label;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(
        width: 8,
        height: 8,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      ),
      const SizedBox(width: 6),
      Text(
        label,
        style: TextStyle(fontSize: 12, color: JarvisColors.of(context).muted),
      ),
    ],
  );
}

class _BucketRow extends StatelessWidget {
  const _BucketRow({required this.bucket, super.key});

  final Map<String, dynamic> bucket;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final said = CalibrationCard._number(bucket['meanPredicted']) ?? 0;
    final happened = CalibrationCard._number(bucket['actualRate']) ?? 0;
    final count = asJsonInt(bucket['count']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  asJsonString(bucket['label']) ?? '',
                  style: const TextStyle(fontSize: 13),
                ),
              ),
              Text(
                '$count ${count == 1 ? 'call' : 'calls'} · '
                '${percentLabel(said)} vs ${percentLabel(happened)}',
                style: TextStyle(fontSize: 12, color: colors.muted),
              ),
            ],
          ),
          const SizedBox(height: 4),
          _Bar(fraction: said, color: colors.accent),
          const SizedBox(height: 3),
          _Bar(fraction: happened, color: colors.success),
        ],
      ),
    );
  }
}

class _Bar extends StatelessWidget {
  const _Bar({required this.fraction, required this.color});

  final double fraction;
  final Color color;

  @override
  Widget build(BuildContext context) => ClipRRect(
    borderRadius: BorderRadius.circular(3),
    child: Container(
      height: 6,
      color: JarvisColors.of(context).surfaceMuted,
      alignment: Alignment.centerLeft,
      child: FractionallySizedBox(
        widthFactor: fraction.clamp(0.0, 1.0),
        child: Container(color: color),
      ),
    ),
  );
}
