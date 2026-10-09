part of 'chat_widgets.dart';

class ToolRunView extends StatelessWidget {
  const ToolRunView({
    required this.run,
    this.onOpenTasks,
    this.onOpenEntity,
    super.key,
  });

  final ToolRunEntry run;

  /// Lets a finished "started a background task" step jump to the Tasks list
  /// when the server did not say which task it made.
  final VoidCallback? onOpenTasks;

  /// Opens a thing a step made or changed, from its result card.
  final ValueChanged<EntityRef>? onOpenEntity;

  @override
  Widget build(BuildContext context) {
    final made = [
      for (final step in run.steps)
        if (step.status == ToolStepStatus.completed)
          for (final ref in parseEntityRefs(step.refs)) (step, ref),
    ];
    return Padding(
      padding: const EdgeInsets.only(left: 40, bottom: 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
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
                            step.status == ToolStepStatus.completed &&
                            step.refs.isEmpty
                        ? onOpenTasks
                        : null,
                  ),
                ),
            ],
          ),
          // What the steps made, as cards that open the thing itself.
          if (made.isNotEmpty && onOpenEntity != null) ...[
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final (step, ref) in made)
                  PopIn(
                    key: ValueKey('card-$ref'),
                    from: .85,
                    alignment: Alignment.centerLeft,
                    child: EntityResultCard(
                      entity: ref,
                      label: describeTool(step.tool).done,
                      onTap: () => onOpenEntity!(ref),
                    ),
                  ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

/// A thing Jarvis just made or changed: what kind it is, what happened, and a
/// tap to open it.
class EntityResultCard extends StatelessWidget {
  const EntityResultCard({
    required this.entity,
    required this.label,
    required this.onTap,
    super.key,
  });

  final EntityRef entity;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final kind = entity.kind;
    return Semantics(
      button: true,
      label: '$label. Open ${kind.label.toLowerCase()}',
      child: Material(
        color: colors.surface,
        borderRadius: BorderRadius.circular(12),
        child: InkWell(
          key: Key('entity-card-$entity'),
          borderRadius: BorderRadius.circular(12),
          onTap: onTap,
          child: Container(
            constraints: const BoxConstraints(maxWidth: 280),
            padding: const EdgeInsets.fromLTRB(10, 8, 12, 8),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: colors.outline),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                IconBadge(icon: kind.icon, size: 30, color: colors.accent),
                const SizedBox(width: 10),
                Flexible(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        label,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                          color: colors.ink,
                        ),
                      ),
                      Text(
                        kind.label,
                        style: TextStyle(fontSize: 12, color: colors.muted),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 10),
                Icon(
                  PhosphorIconsRegular.arrowUpRight,
                  size: 15,
                  color: colors.inkSoft,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
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
