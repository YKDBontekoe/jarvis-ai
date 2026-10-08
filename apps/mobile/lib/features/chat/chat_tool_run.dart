part of 'chat_widgets.dart';

class ToolRunView extends StatelessWidget {
  const ToolRunView({required this.run, this.onOpenTasks, super.key});

  final ToolRunEntry run;

  /// Lets a finished "started a background task" step jump to the Tasks list.
  final VoidCallback? onOpenTasks;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(left: 40, bottom: 14),
    child: Wrap(
      spacing: 6,
      runSpacing: 6,
      children: [
        // Each step pops in as Jarvis starts it.
        for (final (index, step) in run.steps.indexed)
          PopIn(
            key: ValueKey(index),
            from: .7,
            alignment: Alignment.centerLeft,
            child: _ToolChip(
              step: step,
              onTap:
                  step.tool == 'CreateTask' &&
                      step.status == ToolStepStatus.completed
                  ? onOpenTasks
                  : null,
            ),
          ),
      ],
    ),
  );
}

class _ToolChip extends StatelessWidget {
  const _ToolChip({required this.step, this.onTap});

  final ToolStep step;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final description = describeTool(step.tool);
    final (label, color) = switch (step.status) {
      ToolStepStatus.running => (
        description.active,
        JarvisColors.of(context).inkSoft,
      ),
      ToolStepStatus.completed => (
        description.done,
        JarvisColors.of(context).success,
      ),
      ToolStepStatus.failed => (
        description.failed,
        JarvisColors.of(context).danger,
      ),
    };
    final chip = AnimatedContainer(
      duration: const Duration(milliseconds: 250),
      padding: const EdgeInsets.fromLTRB(10, 6, 10, 6),
      decoration: BoxDecoration(
        color: JarvisColors.of(context).surface,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: JarvisColors.of(context).outline),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            description.icon,
            size: 14,
            color: JarvisColors.of(context).inkSoft,
          ),
          const SizedBox(width: 7),
          Text(
            label,
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w500,
              color: JarvisColors.of(context).inkSoft,
            ),
          ),
          const SizedBox(width: 7),
          // The spinner spins into a check (or a cross) when the step ends.
          AnimatedSwitcher(
            duration: JarvisMotion.of(
              context,
              const Duration(milliseconds: 380),
            ),
            transitionBuilder: JarvisMotion.morph,
            child: switch (step.status) {
              ToolStepStatus.running => SizedBox.square(
                key: const ValueKey('running'),
                dimension: 11,
                child: CircularProgressIndicator(
                  strokeWidth: 1.6,
                  color: color,
                ),
              ),
              ToolStepStatus.completed => Icon(
                PhosphorIconsRegular.check,
                key: const ValueKey('completed'),
                size: 14,
                color: color,
              ),
              ToolStepStatus.failed => Icon(
                PhosphorIconsRegular.x,
                key: const ValueKey('failed'),
                size: 14,
                color: color,
              ),
            },
          ),
          if (onTap != null) ...[
            const SizedBox(width: 6),
            Text(
              'View',
              style: TextStyle(
                fontSize: 12.5,
                fontWeight: FontWeight.w600,
                color: JarvisColors.of(context).ink,
              ),
            ),
          ],
        ],
      ),
    );
    if (onTap == null) return chip;
    return Semantics(
      button: true,
      label: '$label. View tasks',
      child: InkWell(
        borderRadius: BorderRadius.circular(8),
        onTap: onTap,
        child: chip,
      ),
    );
  }
}
