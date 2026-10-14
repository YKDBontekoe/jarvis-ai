import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/tool_catalog.dart' show humanizeToolName;

/// How smoothly turns ran in the period: reply time, failed turns, slow tools,
/// and how often the owner had to stop for an approval. Built from the
/// `turnQuality` part of `GET /api/v1/usage`; hidden on older servers.
class TurnQualityCard extends StatelessWidget {
  const TurnQualityCard({required this.data, super.key});

  final Map<String, dynamic> data;

  static const _flagText = {
    'slow_replies':
        'Replies are slow: one in ten takes over 30 s. A faster chat model in '
        'Settings → Models helps most.',
    'failing_turns':
        'More than one in ten turns failed. The Codex sign-in or the server '
        'log usually says why.',
    'approval_friction':
        'You approve a lot by hand. Always allow a category on its next '
        'approval card, or raise the autonomy level in Learning.',
    'low_cache':
        'Few prompts are reused from cache, so each reply costs more time.',
  };

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final modelCalls = asJsonInt(data['modelCalls']);
    final median = _int(data['medianModelMs']);
    final p90 = _int(data['p90ModelMs']);
    final failureRate = _num(data['modelFailureRate']);
    final cacheRate = _num(data['cacheHitRate']);
    final turns = asJsonInt(data['turns']);
    final medianTurn = _int(data['medianTurnMs']);
    final failedTurns = asJsonInt(data['failedTurns']);
    final cancelledTurns = asJsonInt(data['cancelledTurns']);
    final toolCalls = asJsonInt(data['toolCalls']);
    final toolFailureRate = _num(data['toolFailureRate']);
    final asked = asJsonInt(data['approvalsAsked']);
    final denied = asJsonInt(data['approvalsDenied']);
    final auto = asJsonInt(data['autoApproved']);
    final slowTools = jsonMaps(data['slowTools']);
    final flags = [
      for (final flag in jsonStrings(data['flags'])) ?_flagText[flag],
    ];
    final metrics = <(String, String)>[
      if (median != null) ('Typical reply', formatTurnDuration(median)),
      if (p90 != null) ('Slowest 1 in 10', formatTurnDuration(p90)),
      if (medianTurn != null) ('Typical turn', formatTurnDuration(medianTurn)),
      if (cacheRate != null) ('From cache', '${(cacheRate * 100).round()}%'),
    ];
    final lines = <String>[
      if (failureRate != null && failureRate > 0)
        '${(failureRate * 100).toStringAsFixed(failureRate < .1 ? 1 : 0)}% of '
            '$modelCalls model calls failed',
      if (failedTurns > 0 || cancelledTurns > 0)
        '${_count(failedTurns, 'turn')} failed, $cancelledTurns stopped '
            '(of $turns traced)',
      if (toolFailureRate != null && toolCalls > 0)
        '${_count(toolCalls, 'tool call')}, '
            '${(toolFailureRate * 100).round()}% failed',
      if (asked > 0)
        '${_count(asked, 'approval')} asked, $denied declined, '
            '$auto handled automatically',
    ];

    return SurfaceCard(
      key: const Key('usage-turn-quality'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.timer),
              const SizedBox(width: 14),
              Expanded(
                child: Text('Turn quality', style: theme.textTheme.titleMedium),
              ),
            ],
          ),
          const SizedBox(height: 12),
          if (metrics.isEmpty && lines.isEmpty)
            Text(
              'Not enough turns yet. Reply times, failures and approvals '
              'show up here after a few chats.',
              style: TextStyle(color: colors.inkSoft, height: 1.35),
            ),
          if (metrics.isNotEmpty)
            Wrap(
              spacing: 24,
              runSpacing: 10,
              children: [
                for (final (label, value) in metrics)
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        label,
                        style: TextStyle(color: colors.muted, fontSize: 11),
                      ),
                      Text(
                        value,
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                    ],
                  ),
              ],
            ),
          if (lines.isNotEmpty) const SizedBox(height: 10),
          for (final line in lines)
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Text(
                line,
                style: TextStyle(color: colors.inkSoft, height: 1.35),
              ),
            ),
          if (slowTools.isNotEmpty) ...[
            const SizedBox(height: 6),
            Text('Slowest tools', style: theme.textTheme.labelLarge),
            const SizedBox(height: 4),
            for (final tool in slowTools)
              Text(
                '${humanizeToolName(asJsonString(tool['tool']) ?? 'tool')}: '
                '${formatTurnDuration(asJsonInt(tool['averageMs']))} on average',
                style: TextStyle(color: colors.inkSoft, height: 1.35),
              ),
          ],
          for (final flag in flags) ...[
            const SizedBox(height: 10),
            InlineNotice(message: flag, tone: NoticeTone.warning),
          ],
        ],
      ),
    );
  }

  static num? _num(dynamic value) => value is num ? value : null;

  static int? _int(dynamic value) => value is num ? value.round() : null;

  static String _count(int count, String noun) =>
      '$count ${count == 1 ? noun : '${noun}s'}';
}

/// "850 ms", "4.2 s", "38 s", "2 min 5 s".
String formatTurnDuration(int ms) {
  if (ms < 1000) return '$ms ms';
  if (ms < 10000) return '${(ms / 1000).toStringAsFixed(1)} s';
  final seconds = (ms / 1000).round();
  if (seconds < 60) return '$seconds s';
  final rest = seconds % 60;
  return rest == 0 ? '${seconds ~/ 60} min' : '${seconds ~/ 60} min $rest s';
}
