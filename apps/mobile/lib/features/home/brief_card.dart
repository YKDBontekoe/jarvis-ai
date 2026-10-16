import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/tile_card.dart' show tileRadius;
import 'next_up.dart' show clockTime;

/// Today's morning briefing as delivered, read right on Home. It opens to
/// five lines; "Read more" unfolds the rest in place.
class BriefCard extends StatefulWidget {
  const BriefCard({
    required this.note,
    required this.onDismiss,
    this.onFollowUp,
    super.key,
  });

  /// The `latestBriefing` object from `/home`.
  final Map<String, dynamic> note;
  final VoidCallback onDismiss;

  /// Asks Jarvis about the briefing.
  final VoidCallback? onFollowUp;

  /// Whether [note] is a briefing worth showing: present and not read yet.
  static bool shows(Map<String, dynamic>? note) =>
      note != null &&
      asJsonString(note['body'])?.trim().isNotEmpty == true &&
      !asJsonBool(note['read']);

  @override
  State<BriefCard> createState() => _BriefCardState();
}

class _BriefCardState extends State<BriefCard> {
  var _open = false;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final body = plainBriefing(asJsonString(widget.note['body']) ?? '');
    final at = jsonDate(widget.note['createdAt'], local: true);
    final style = TextStyle(fontSize: 15, height: 1.5, color: colors.ink);
    return Sheen(
      trigger: asJsonString(widget.note['id']),
      borderRadius: BorderRadius.circular(tileRadius),
      child: Container(
        key: const Key('home-brief'),
        padding: const EdgeInsets.fromLTRB(18, 14, 8, 16),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(tileRadius),
          border: Border.all(color: colors.accent.withValues(alpha: .14)),
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [colors.accentSoft, colors.surface],
            stops: const [0, .7],
          ),
        ),
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
                    [
                      'MORNING BRIEF',
                      if (at != null) clockTime(at),
                    ].join(' · '),
                    style: JarvisType.capsOf(
                      context,
                    ).copyWith(color: colors.accentDeep),
                  ),
                ),
                IconButton(
                  key: const Key('home-brief-dismiss'),
                  tooltip: 'Dismiss the brief',
                  onPressed: widget.onDismiss,
                  icon: Icon(
                    PhosphorIconsRegular.x,
                    size: 16,
                    color: colors.inkSoft,
                  ),
                ),
              ],
            ),
            LayoutBuilder(
              builder: (context, box) {
                final more = _overflows(
                  body,
                  style,
                  box.maxWidth - 10,
                  MediaQuery.textScalerOf(context),
                );
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Padding(
                      padding: const EdgeInsets.only(right: 10),
                      child: MotionSize(
                        child: Text(
                          body,
                          key: const Key('home-brief-body'),
                          maxLines: _open ? null : _lines,
                          overflow: _open
                              ? TextOverflow.visible
                              : TextOverflow.fade,
                          style: style,
                        ),
                      ),
                    ),
                    const SizedBox(height: 10),
                    Wrap(
                      spacing: 4,
                      children: [
                        if (widget.onFollowUp != null)
                          FilledButton.tonal(
                            key: const Key('home-brief-follow-up'),
                            onPressed: widget.onFollowUp,
                            child: const Text('Ask about it'),
                          ),
                        if (more)
                          TextButton(
                            key: const Key('home-brief-more'),
                            onPressed: () => setState(() => _open = !_open),
                            child: Text(_open ? 'Show less' : 'Read more'),
                          ),
                      ],
                    ),
                  ],
                );
              },
            ),
          ],
        ),
      ),
    );
  }

  static const _lines = 5;

  /// Whether [body] needs more than [_lines] lines at this width.
  bool _overflows(
    String body,
    TextStyle style,
    double width,
    TextScaler scaler,
  ) {
    final painter = TextPainter(
      text: TextSpan(text: body, style: style),
      maxLines: _lines,
      textDirection: Directionality.of(context),
      textScaler: scaler,
    )..layout(maxWidth: width.clamp(1, double.infinity));
    final overflows = painter.didExceedMaxLines;
    painter.dispose();
    return overflows;
  }
}

/// The briefing body as reading text: Markdown marks taken out, list items
/// kept on their own lines.
String plainBriefing(String body) {
  final lines = <String>[];
  for (final raw in body.split('\n')) {
    var line = raw.trim();
    if (line.isEmpty) {
      if (lines.isNotEmpty && lines.last.isNotEmpty) lines.add('');
      continue;
    }
    line = line
        .replaceFirst(RegExp(r'^#{1,6}\s+'), '')
        .replaceFirst(RegExp(r'^[-*+]\s+'), '• ')
        .replaceAll(RegExp(r'\*\*|__|`'), '')
        .replaceAllMapped(RegExp(r'\[([^\]]+)\]\([^)]*\)'), (m) => m[1]!);
    lines.add(line);
  }
  while (lines.isNotEmpty && lines.last.isEmpty) {
    lines.removeLast();
  }
  return lines.join('\n');
}
