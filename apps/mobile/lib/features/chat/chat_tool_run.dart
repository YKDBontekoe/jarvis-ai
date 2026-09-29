part of 'chat_widgets.dart';

class ToolRunView extends StatelessWidget {
  const ToolRunView({required this.run, super.key});

  final ToolRunEntry run;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(left: 40, bottom: 14),
    child: Wrap(
      spacing: 6,
      runSpacing: 6,
      children: [for (final step in run.steps) _ToolChip(step: step)],
    ),
  );
}

class _ToolChip extends StatelessWidget {
  const _ToolChip({required this.step});

  final ToolStep step;

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
    return AnimatedContainer(
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
          switch (step.status) {
            ToolStepStatus.running => SizedBox.square(
              dimension: 11,
              child: CircularProgressIndicator(strokeWidth: 1.6, color: color),
            ),
            ToolStepStatus.completed => Icon(
              PhosphorIconsRegular.check,
              size: 14,
              color: color,
            ),
            ToolStepStatus.failed => Icon(
              PhosphorIconsRegular.x,
              size: 14,
              color: color,
            ),
          },
        ],
      ),
    );
  }
}
