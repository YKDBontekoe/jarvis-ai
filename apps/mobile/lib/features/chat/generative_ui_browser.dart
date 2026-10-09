part of 'generative_ui.dart';

/// Loads one step's screenshot; null when it cannot be shown.
typedef ComputerScreenshotLoader =
    Future<Uint8List?> Function(String sessionId, int ordinal);

class BrowserTimelineView extends StatelessWidget {
  const BrowserTimelineView({
    required this.session,
    this.loadScreenshot,
    this.onWatch,
    this.onTakeOver,
    this.onHandBack,
    super.key,
  });

  final BrowserSessionEntry session;

  /// Computer sessions only: the latest screenshot and live-view controls.
  final ComputerScreenshotLoader? loadScreenshot;
  final VoidCallback? onWatch;
  final VoidCallback? onTakeOver;
  final VoidCallback? onHandBack;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final textTheme = Theme.of(context).textTheme;
    final shot = session.latestScreenshot;
    final loader = loadScreenshot;
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                IconBadge(
                  icon: session.isComputer
                      ? PhosphorIconsRegular.monitor
                      : PhosphorIconsRegular.browser,
                  size: 34,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(session.goal, style: textTheme.titleSmall),
                ),
              ],
            ),
            if (session.isComputer && session.isLive && session.userHasControl)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  'You have control. Jarvis waits until you hand it back.',
                  style: textTheme.bodySmall?.copyWith(color: colors.warning),
                ),
              ),
            if (shot != null && loader != null)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: _ComputerScreenshot(
                  key: ValueKey('${session.id}:${shot.ordinal}'),
                  load: () => loader(session.id, shot.ordinal!),
                ),
              ),
            const SizedBox(height: 8),
            for (final step in session.steps)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  children: [
                    Icon(
                      step.success
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.warningCircle,
                      size: 14,
                      color: step.success ? colors.success : colors.danger,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(step.summary, style: textTheme.bodySmall),
                    ),
                  ],
                ),
              ),
            if (session.isComputer && session.isLive)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    if (onWatch != null)
                      OutlinedButton.icon(
                        onPressed: onWatch,
                        icon: const Icon(PhosphorIconsRegular.eye, size: 16),
                        label: const Text('Watch live'),
                      ),
                    if (session.userHasControl && onHandBack != null)
                      FilledButton.icon(
                        onPressed: onHandBack,
                        icon: const Icon(PhosphorIconsRegular.robot, size: 16),
                        label: const Text('Hand back to Jarvis'),
                      )
                    else if (onTakeOver != null)
                      OutlinedButton.icon(
                        onPressed: onTakeOver,
                        icon: const Icon(PhosphorIconsRegular.hand, size: 16),
                        label: const Text('Take over'),
                      ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// The sandbox screen after the latest step; tap to see it full size.
class _ComputerScreenshot extends StatefulWidget {
  const _ComputerScreenshot({required this.load, super.key});

  final Future<Uint8List?> Function() load;

  @override
  State<_ComputerScreenshot> createState() => _ComputerScreenshotState();
}

class _ComputerScreenshotState extends State<_ComputerScreenshot> {
  late final Future<Uint8List?> _bytes = widget.load();

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<Uint8List?>(
      future: _bytes,
      builder: (context, snapshot) {
        final bytes = snapshot.data;
        return ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: AspectRatio(
            aspectRatio: 16 / 10,
            child: bytes == null
                ? ColoredBox(
                    color: JarvisColors.of(context).surfaceMuted,
                    child: snapshot.connectionState == ConnectionState.done
                        ? const Center(child: Icon(PhosphorIconsRegular.image))
                        : null,
                  )
                : Semantics(
                    label: 'Screenshot of the sandbox computer',
                    image: true,
                    button: true,
                    child: GestureDetector(
                      onTap: () => _showFullSize(context, bytes),
                      child: Image.memory(
                        bytes,
                        fit: BoxFit.cover,
                        gaplessPlayback: true,
                      ),
                    ),
                  ),
          ),
        );
      },
    );
  }

  void _showFullSize(BuildContext context, Uint8List bytes) {
    unawaited(
      showDialog<void>(
        context: context,
        builder: (context) => Dialog(
          insetPadding: const EdgeInsets.all(12),
          child: InteractiveViewer(
            maxScale: 4,
            child: Image.memory(bytes, fit: BoxFit.contain),
          ),
        ),
      ),
    );
  }
}
