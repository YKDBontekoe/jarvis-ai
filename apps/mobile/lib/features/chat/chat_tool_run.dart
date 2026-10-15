part of 'chat_widgets.dart';

class ToolRunView extends StatelessWidget {
  const ToolRunView({
    required this.run,
    this.onOpenTasks,
    this.onOpenEntity,
    this.onOpenApps,
    super.key,
  });

  final ToolRunEntry run;

  /// Opens Connected apps, where an app Jarvis built can be reviewed.
  final VoidCallback? onOpenApps;

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
          if (AppBuildProgress.of(run) case final progress?) ...[
            const SizedBox(height: 8),
            PopIn(
              from: .85,
              alignment: Alignment.centerLeft,
              child: AppBuildCard(progress: progress, onOpen: onOpenApps),
            ),
          ],
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

/// How far Jarvis got building an app in one reply.
class AppBuildProgress {
  const AppBuildProgress({
    required this.written,
    required this.tested,
    required this.live,
    required this.failed,
    required this.working,
  });

  final bool written;
  final bool tested;
  final bool live;

  /// The last test or activation in this reply failed.
  final bool failed;
  final bool working;

  static const _tools = {
    'WriteCustomMcpServer',
    'TestCustomMcpServer',
    'ActivateCustomMcpServer',
  };

  static AppBuildProgress? of(ToolRunEntry run) {
    final steps = [
      for (final step in run.steps)
        if (_tools.contains(step.tool.replaceAll(RegExp(r'Async$'), ''))) step,
    ];
    if (steps.isEmpty) return null;
    bool done(String tool) => steps.any(
      (step) =>
          step.tool.startsWith(tool) && step.status == ToolStepStatus.completed,
    );
    return AppBuildProgress(
      written: done('WriteCustomMcpServer'),
      tested: done('TestCustomMcpServer'),
      live: done('ActivateCustomMcpServer'),
      failed: steps.last.status == ToolStepStatus.failed,
      working: steps.any((step) => step.status == ToolStepStatus.running),
    );
  }
}

/// Written → tested → live, for an app Jarvis is building in this chat.
class AppBuildCard extends StatelessWidget {
  const AppBuildCard({required this.progress, this.onOpen, super.key});

  final AppBuildProgress progress;
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final stages = [
      ('Code written', progress.written),
      ('Tested', progress.tested),
      ('Live', progress.live),
    ];
    final title = progress.live
        ? 'Your new app is live'
        : progress.tested
        ? 'Your new app is ready for your OK'
        : progress.working
        ? 'Jarvis is building an app'
        : 'Jarvis wrote an app';
    return Container(
      key: const Key('app-build-card'),
      constraints: const BoxConstraints(maxWidth: 360),
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: colors.outline),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(
                icon: PhosphorIconsRegular.magicWand,
                size: 28,
                color: colors.accent,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  title,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 10,
            runSpacing: 4,
            children: [
              for (final (label, reached) in stages)
                Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      reached
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.circle,
                      size: 14,
                      color: reached ? colors.success : colors.muted,
                    ),
                    const SizedBox(width: 4),
                    Text(
                      label,
                      style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
                    ),
                  ],
                ),
            ],
          ),
          if (progress.failed) ...[
            const SizedBox(height: 6),
            Text(
              'The last step did not work. Jarvis can fix the code and try again.',
              style: TextStyle(fontSize: 12.5, color: colors.danger),
            ),
          ],
          if (onOpen != null) ...[
            const SizedBox(height: 4),
            TextButton(
              onPressed: onOpen,
              style: TextButton.styleFrom(
                padding: EdgeInsets.zero,
                visualDensity: VisualDensity.compact,
              ),
              child: Text(progress.live ? 'Open the app' : 'Review the code'),
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
