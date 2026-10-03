part of 'chat_widgets.dart';

class ApprovalCard extends StatelessWidget {
  const ApprovalCard({
    required this.approval,
    required this.onDecide,
    this.onAlwaysAllow,
    this.loadMemoryText,
    super.key,
  });

  final ApprovalEntry approval;
  final void Function(bool approved) onDecide;

  /// Approves this call and stores a standing grant for its category.
  final VoidCallback? onAlwaysAllow;

  /// Looks up the text of a memory so its approval shows what will be lost.
  final Future<String?> Function(String memoryId)? loadMemoryText;

  @override
  Widget build(BuildContext context) {
    final description = describeTool(approval.toolName);
    final arguments = approval.arguments.entries
        .where((entry) => entry.value != null && '${entry.value}'.isNotEmpty)
        .toList();
    final decided =
        approval.status == ApprovalStatus.approved ||
        approval.status == ApprovalStatus.denied;
    final needsRetry =
        approval.retry || approval.status == ApprovalStatus.failed;
    final accent = switch (approval.status) {
      ApprovalStatus.approved => JarvisColors.of(context).success,
      ApprovalStatus.denied => JarvisColors.of(context).muted,
      ApprovalStatus.failed => JarvisColors.of(context).danger,
      _ => JarvisColors.of(context).warning,
    };
    final submitting = approval.status == ApprovalStatus.submitting;
    final colors = JarvisColors.of(context);
    final pending = !decided;
    final showAlways =
        pending &&
        !needsRetry &&
        approval.canRememberCategory &&
        onAlwaysAllow != null;
    final alwaysLabel = _lowerFirst(
      approval.categoryLabel ?? 'this kind of action',
    );
    final eyebrow = switch (approval.status) {
      ApprovalStatus.approved => 'Approved',
      ApprovalStatus.denied => 'Declined',
      _ =>
        needsRetry
            ? (approval.decision == false
                  ? 'Declined, but not finished'
                  : 'Approved, but not finished')
            : 'Jarvis needs your approval',
    };
    return Padding(
      padding: const EdgeInsets.only(left: 40, bottom: 18),
      child: AnimatedContainer(
        duration: JarvisMotion.of(context, JarvisMotion.base),
        curve: JarvisMotion.standard,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(JarvisRadii.lg),
          border: Border.all(
            color: pending
                ? accent.withValues(alpha: .35)
                : colors.outline.withValues(alpha: .75),
          ),
          boxShadow: pending
              ? JarvisShadows.soft(colors.brightness)
              : JarvisShadows.hairline(colors.brightness),
        ),
        clipBehavior: Clip.antiAlias,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    width: 40,
                    height: 40,
                    decoration: BoxDecoration(
                      color: accent.withValues(alpha: colors.isDark ? .18 : .1),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Icon(
                      decided
                          ? (approval.status == ApprovalStatus.approved
                                ? PhosphorIconsFill.shieldCheck
                                : PhosphorIconsRegular.prohibit)
                          : description.icon,
                      color: accent,
                      size: 20,
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          eyebrow,
                          style: TextStyle(
                            fontWeight: FontWeight.w600,
                            fontSize: 12.5,
                            letterSpacing: .1,
                            color: accent,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          description.active,
                          style: TextStyle(
                            color: colors.ink,
                            fontSize: 15.5,
                            fontWeight: FontWeight.w600,
                            letterSpacing: -.2,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              if (arguments.isNotEmpty) ...[
                const SizedBox(height: 14),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.fromLTRB(14, 10, 14, 10),
                  decoration: BoxDecoration(
                    color: colors.surfaceMuted,
                    borderRadius: BorderRadius.circular(JarvisRadii.md),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      for (final entry in arguments.take(6))
                        if (entry.key == 'memoryId' &&
                            loadMemoryText != null &&
                            approval.toolName == 'ForgetMemory')
                          _MemoryPreview(
                            memoryId: '${entry.value}',
                            load: loadMemoryText!,
                          )
                        else
                          Padding(
                            padding: const EdgeInsets.symmetric(vertical: 4),
                            child: Text.rich(
                              TextSpan(
                                children: [
                                  TextSpan(
                                    text:
                                        '${_sentence(humanizeToolName(entry.key))}\n',
                                    style: TextStyle(
                                      color: colors.muted,
                                      fontSize: 12,
                                      height: 1.5,
                                      fontWeight: FontWeight.w500,
                                    ),
                                  ),
                                  TextSpan(
                                    text: '${entry.value}',
                                    style: TextStyle(
                                      fontSize: 14,
                                      height: 1.4,
                                      color: colors.ink,
                                    ),
                                  ),
                                ],
                              ),
                              maxLines: 4,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                    ],
                  ),
                ),
              ],
              if (approval.error case final error?) ...[
                const SizedBox(height: 12),
                InlineNotice(message: error, tone: NoticeTone.danger),
              ],
              AnimatedSize(
                duration: JarvisMotion.of(context, JarvisMotion.base),
                curve: JarvisMotion.standard,
                alignment: Alignment.topCenter,
                child: decided
                    ? const SizedBox(width: double.infinity)
                    : Padding(
                        padding: const EdgeInsets.only(top: 16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Row(
                              children: [
                                if (!needsRetry) ...[
                                  Expanded(
                                    child: OutlinedButton(
                                      onPressed: submitting
                                          ? null
                                          : () => onDecide(false),
                                      style: OutlinedButton.styleFrom(
                                        minimumSize: const Size(0, 46),
                                      ),
                                      child: const Text('Decline'),
                                    ),
                                  ),
                                  const SizedBox(width: 10),
                                ],
                                Expanded(
                                  child: FilledButton.icon(
                                    onPressed: submitting
                                        ? null
                                        : () {
                                            unawaited(
                                              HapticFeedback.mediumImpact(),
                                            );
                                            onDecide(
                                              needsRetry
                                                  ? (approval.decision ?? true)
                                                  : true,
                                            );
                                          },
                                    style: FilledButton.styleFrom(
                                      minimumSize: const Size(0, 46),
                                    ),
                                    icon: submitting
                                        ? SizedBox.square(
                                            dimension: 14,
                                            child: CircularProgressIndicator(
                                              strokeWidth: 2,
                                              color: colors.muted,
                                            ),
                                          )
                                        : Icon(
                                            needsRetry
                                                ? PhosphorIconsRegular
                                                      .arrowsClockwise
                                                : PhosphorIconsRegular.check,
                                            size: 18,
                                          ),
                                    label: Text(
                                      needsRetry ? 'Retry' : 'Approve',
                                    ),
                                  ),
                                ),
                              ],
                            ),
                            if (showAlways) ...[
                              const SizedBox(height: 6),
                              TextButton(
                                onPressed: submitting
                                    ? null
                                    : () => unawaited(
                                        _confirmAlwaysAllow(
                                          context,
                                          alwaysLabel,
                                        ),
                                      ),
                                style: TextButton.styleFrom(
                                  minimumSize: const Size(0, 40),
                                  alignment: Alignment.centerLeft,
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 4,
                                  ),
                                ),
                                child: Text(
                                  'Always allow $alwaysLabel',
                                  textAlign: TextAlign.start,
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _confirmAlwaysAllow(BuildContext context, String label) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Always allow this?'),
        content: Text(
          'Jarvis will approve $label from now on, without asking. You can turn this off in Approvals.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Not now'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Always allow'),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      unawaited(HapticFeedback.mediumImpact());
      onAlwaysAllow?.call();
    }
  }
}

/// "Forgetting memories" → "forgetting memories".
String _lowerFirst(String text) =>
    text.isEmpty ? text : text[0].toLowerCase() + text.substring(1);

/// "start time" → "Start time".
String _sentence(String text) =>
    text.isEmpty ? text : text[0].toUpperCase() + text.substring(1);

/// Shows the text of the memory an approval is about, falling back to a
/// short label while loading or when it cannot be read.
class _MemoryPreview extends StatefulWidget {
  const _MemoryPreview({required this.memoryId, required this.load});

  final String memoryId;
  final Future<String?> Function(String memoryId) load;

  @override
  State<_MemoryPreview> createState() => _MemoryPreviewState();
}

class _MemoryPreviewState extends State<_MemoryPreview> {
  late final Future<String?> _text = widget.load(widget.memoryId);

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return FutureBuilder<String?>(
      future: _text,
      builder: (context, snapshot) {
        final text = snapshot.data;
        final label = snapshot.connectionState != ConnectionState.done
            ? 'Loading…'
            : (text == null || text.isEmpty ? 'This memory' : '“$text”');
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 3),
          child: Text.rich(
            TextSpan(
              children: [
                TextSpan(
                  text: 'Memory  ',
                  style: TextStyle(
                    color: colors.muted,
                    fontSize: 12.5,
                    fontWeight: FontWeight.w500,
                  ),
                ),
                TextSpan(
                  text: label,
                  style: TextStyle(fontSize: 14, color: colors.ink),
                ),
              ],
            ),
            maxLines: 4,
            overflow: TextOverflow.ellipsis,
          ),
        );
      },
    );
  }
}
