import 'package:flutter/material.dart';

import '../api/offline_cache.dart';
import '../theme.dart';
import 'motion.dart';
import 'phosphor_icons.dart';

/// A slim bar above every page while the server cannot be reached and screens
/// show saved data. It goes away by itself once a request gets through.
class OfflineBanner extends StatelessWidget {
  const OfflineBanner({super.key, required this.cache, required this.child});

  final OfflineCache cache;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<OfflineState?>(
      valueListenable: cache.state,
      builder: (context, offline, _) {
        final bar = offline == null
            ? const SizedBox(width: double.infinity)
            : _OfflineBar(state: offline, onRetry: cache.retry);
        return Column(
          children: [
            MotionSize(child: bar),
            Expanded(
              child: offline == null
                  ? child
                  : MediaQuery.removePadding(
                      context: context,
                      removeTop: true,
                      child: child,
                    ),
            ),
          ],
        );
      },
    );
  }
}

class _OfflineBar extends StatefulWidget {
  const _OfflineBar({required this.state, required this.onRetry});

  final OfflineState state;
  final Future<bool> Function() onRetry;

  @override
  State<_OfflineBar> createState() => _OfflineBarState();
}

class _OfflineBarState extends State<_OfflineBar> {
  bool _checking = false;

  Future<void> _retry() async {
    if (_checking) return;
    setState(() => _checking = true);
    try {
      await widget.onRetry();
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final savedAt = widget.state.savedAt;
    final label = savedAt == null
        ? 'Offline'
        : 'Offline · showing data from ${offlineAge(savedAt, DateTime.now())}';
    return Material(
      color: colors.warningSoft,
      child: SafeArea(
        bottom: false,
        child: Semantics(
          liveRegion: true,
          label: label,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 6, 8, 6),
            child: Row(
              children: [
                Icon(
                  PhosphorIconsRegular.cloudSlash,
                  size: 16,
                  color: colors.warning,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: ExcludeSemantics(
                    child: Text(
                      label,
                      key: const ValueKey('offline-banner'),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(
                        context,
                      ).textTheme.labelMedium?.copyWith(color: colors.ink),
                    ),
                  ),
                ),
                TextButton(
                  onPressed: _checking ? null : _retry,
                  style: TextButton.styleFrom(
                    visualDensity: VisualDensity.compact,
                    foregroundColor: colors.ink,
                    textStyle: const TextStyle(
                      fontFamily: 'Geist',
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  child: Text(_checking ? 'Checking…' : 'Retry'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// "just now", "5 min ago", "3 h ago", "2 days ago".
String offlineAge(DateTime savedAt, DateTime now) {
  final age = now.difference(savedAt);
  if (age.inMinutes < 1) return 'just now';
  if (age.inHours < 1) return '${age.inMinutes} min ago';
  if (age.inDays < 1) return '${age.inHours} h ago';
  return age.inDays == 1 ? 'yesterday' : '${age.inDays} days ago';
}
