part of 'chat_widgets.dart';

class ApprovalCard extends StatelessWidget {
  const ApprovalCard({
    required this.approval,
    required this.onDecide,
    super.key,
  });

  final ApprovalEntry approval;
  final void Function(bool approved) onDecide;

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
      ApprovalStatus.approved => JarvisColors.success,
      ApprovalStatus.denied => JarvisColors.muted,
      ApprovalStatus.failed => JarvisColors.danger,
      _ => JarvisColors.warning,
    };
    final submitting = approval.status == ApprovalStatus.submitting;
    return Padding(
      padding: const EdgeInsets.only(left: 40, bottom: 18),
      child: Container(
        decoration: BoxDecoration(
          color: JarvisColors.surface,
          borderRadius: BorderRadius.circular(JarvisRadii.lg),
          border: Border.all(color: JarvisColors.outline),
          boxShadow: decided ? null : JarvisShadows.soft,
        ),
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
              decoration: const BoxDecoration(
                color: JarvisColors.canvas,
                border: Border(bottom: BorderSide(color: JarvisColors.outline)),
              ),
              child: Row(
                children: [
                  Icon(
                    decided
                        ? (approval.status == ApprovalStatus.approved
                              ? PhosphorIconsFill.shieldCheck
                              : PhosphorIconsRegular.prohibit)
                        : PhosphorIconsRegular.shieldCheck,
                    color: accent,
                    size: 19,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      switch (approval.status) {
                        ApprovalStatus.approved => 'Approved',
                        ApprovalStatus.denied => 'Declined',
                        _ =>
                          needsRetry
                              ? (approval.decision == false
                                    ? 'Declined, but not finished'
                                    : 'Approved, but not finished')
                              : 'Jarvis needs your approval',
                      },
                      style: const TextStyle(
                        fontWeight: FontWeight.w600,
                        fontSize: 14.5,
                        color: JarvisColors.ink,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      IconBadge(icon: description.icon, size: 30),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Text(
                          description.active,
                          style: const TextStyle(
                            color: JarvisColors.ink,
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ),
                    ],
                  ),
                  if (arguments.isNotEmpty) ...[
                    const SizedBox(height: 12),
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: JarvisColors.canvas,
                        borderRadius: BorderRadius.circular(JarvisRadii.sm),
                        border: Border.all(color: JarvisColors.outline),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          for (final entry in arguments.take(6))
                            Padding(
                              padding: const EdgeInsets.symmetric(vertical: 3),
                              child: Text.rich(
                                TextSpan(
                                  children: [
                                    TextSpan(
                                      text: '${humanizeToolName(entry.key)}  ',
                                      style: const TextStyle(
                                        color: JarvisColors.muted,
                                        fontSize: 12.5,
                                        fontWeight: FontWeight.w500,
                                      ),
                                    ),
                                    TextSpan(
                                      text: '${entry.value}',
                                      style: const TextStyle(
                                        fontFamily: 'monospace',
                                        fontSize: 12.5,
                                        color: JarvisColors.ink,
                                      ),
                                    ),
                                  ],
                                ),
                                maxLines: 3,
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
                  if (!decided) ...[
                    const SizedBox(height: 14),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.end,
                      children: [
                        if (!needsRetry)
                          OutlinedButton(
                            onPressed: submitting
                                ? null
                                : () => onDecide(false),
                            style: OutlinedButton.styleFrom(
                              minimumSize: const Size(0, 42),
                            ),
                            child: const Text('Decline'),
                          ),
                        const SizedBox(width: 8),
                        FilledButton.icon(
                          onPressed: submitting
                              ? null
                              : () => onDecide(
                                  needsRetry
                                      ? (approval.decision ?? true)
                                      : true,
                                ),
                          style: FilledButton.styleFrom(
                            minimumSize: const Size(0, 42),
                          ),
                          icon: submitting
                              ? const SizedBox.square(
                                  dimension: 14,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : Icon(
                                  needsRetry
                                      ? PhosphorIconsRegular.arrowsClockwise
                                      : PhosphorIconsRegular.check,
                                  size: 18,
                                ),
                          label: Text(needsRetry ? 'Retry' : 'Approve'),
                        ),
                      ],
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
