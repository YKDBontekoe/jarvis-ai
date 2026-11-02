import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What the single line above the dock says right now.
enum DockStripKind { writing, replied, needsYou }

/// One contextual strip: what is happening, where tapping it goes, and a key
/// that changes when something new happens (so a dismissed strip stays gone
/// until then).
class DockStripData {
  const DockStripData({
    required this.kind,
    required this.key,
    required this.title,
    required this.onTap,
    this.detail,
    this.action = 'Open',
    this.onDismiss,
  });

  final DockStripKind kind;

  /// Identity of this moment, for example `waiting:rosa:1730000000`.
  final String key;
  final String title;
  final String? detail;
  final String action;
  final VoidCallback onTap;

  /// Hides the strip until something new comes in; null when it cannot be dismissed.
  final VoidCallback? onDismiss;
}

/// The strip to show, by priority: Jarvis writing, then a reply that finished
/// while you were away, then the most pressing thing that needs you. A dismissed
/// strip is skipped, so the next one can show.
DockStripData? chooseDockStrip({
  DockStripData? writing,
  DockStripData? replied,
  DockStripData? needsYou,
  Set<String> dismissed = const {},
}) {
  for (final candidate in [writing, replied, needsYou]) {
    if (candidate != null && !dismissed.contains(candidate.key)) {
      return candidate;
    }
  }
  return null;
}

/// The glass line above the dock.
class DockStrip extends StatelessWidget {
  const DockStrip({required this.data, super.key});

  final DockStripData data;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final writing = data.kind == DockStripKind.writing;
    return Semantics(
      container: true,
      liveRegion: true,
      label: [
        data.title,
        if (data.detail != null) data.detail!,
        data.action,
      ].join(', '),
      child: Material(
        key: const Key('dock-strip'),
        color: colors.surface.withValues(alpha: .94),
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(22),
          side: BorderSide(
            color: colors.outlineStrong.withValues(alpha: .3),
            width: .5,
          ),
        ),
        child: InkWell(
          key: const Key('dock-strip-open'),
          borderRadius: BorderRadius.circular(22),
          onTap: data.onTap,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 48),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
              child: Row(
                children: [
                  if (writing)
                    const SizedBox(width: 22, child: TypingDots())
                  else
                    JarvisOrb(size: 22, glow: false),
                  const SizedBox(width: 10),
                  Expanded(
                    child: ExcludeSemantics(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            data.title,
                            key: const Key('dock-strip-title'),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              fontSize: 13.5,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          if (data.detail != null)
                            Text(
                              data.detail!,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                fontSize: 12,
                                color: colors.muted,
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                  ExcludeSemantics(
                    child: Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 8),
                      child: Text(
                        data.action,
                        style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                          color: colors.accent,
                        ),
                      ),
                    ),
                  ),
                  if (data.onDismiss != null)
                    IconButton(
                      key: const Key('dock-strip-dismiss'),
                      tooltip: 'Hide until something new',
                      onPressed: data.onDismiss,
                      icon: const Icon(PhosphorIconsRegular.x, size: 16),
                      style: IconButton.styleFrom(
                        minimumSize: const Size(44, 44),
                        foregroundColor: colors.muted,
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
