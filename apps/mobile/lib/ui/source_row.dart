import 'package:flutter/material.dart';

import '../theme.dart';
import 'phosphor_icons.dart';

/// One piece of supporting evidence, drawn the same everywhere: where it came
/// from, when, and a way to open it. A source that has been deleted says so
/// instead of offering a link that goes nowhere.
class SourceRow extends StatelessWidget {
  const SourceRow({
    required this.name,
    this.kind,
    this.date,
    this.onOpen,
    this.unavailable = false,
    super.key,
  });

  /// What the source is called: a chat title, a project name, a file name.
  final String name;

  /// What kind of thing it is ("Chat", "Project"), shown beside the date.
  final String? kind;
  final DateTime? date;

  /// Opens the source. Null while it cannot be opened.
  final VoidCallback? onOpen;

  /// The source no longer exists.
  final bool unavailable;

  static String dateLabel(BuildContext context, DateTime date) =>
      MaterialLocalizations.of(context).formatMediumDate(date.toLocal());

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final detail = [
      ?kind,
      if (date != null) dateLabel(context, date!),
    ].join(' · ');
    final title = unavailable ? 'Source no longer available' : name;
    return Semantics(
      container: true,
      label: [
        title,
        if (detail.isNotEmpty) detail,
        if (onOpen != null && !unavailable) 'Open',
      ].join(', '),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: Row(
          children: [
            Icon(
              unavailable
                  ? PhosphorIconsRegular.linkBreak
                  : PhosphorIconsRegular.link,
              size: 16,
              color: colors.muted,
            ),
            const SizedBox(width: 10),
            Expanded(
              child: ExcludeSemantics(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      key: const Key('source-name'),
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 14,
                        fontWeight: FontWeight.w500,
                        color: unavailable ? colors.muted : colors.ink,
                        fontStyle: unavailable ? FontStyle.italic : null,
                      ),
                    ),
                    if (detail.isNotEmpty && !unavailable)
                      Text(
                        detail,
                        key: const Key('source-detail'),
                        style: TextStyle(fontSize: 12.5, color: colors.muted),
                      ),
                  ],
                ),
              ),
            ),
            if (onOpen != null && !unavailable)
              TextButton(
                key: const Key('source-open'),
                onPressed: onOpen,
                style: TextButton.styleFrom(
                  visualDensity: VisualDensity.compact,
                ),
                child: const Text('Open'),
              ),
          ],
        ),
      ),
    );
  }
}
