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

  Widget _stepChip(int index, ToolStep step) => PopIn(
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
  );

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
              // Each step pops in as Jarvis starts it. A long run of finished
              // steps folds into one summary; running and failed ones stay out.
              for (final segment in foldToolSteps(run.steps))
                if (segment.steps.length == 1 && !segment.folded)
                  _stepChip(segment.start, segment.steps.single)
                else
                  _FoldedSteps(
                    key: ValueKey('fold-${segment.start}'),
                    segment: segment,
                    chipFor: _stepChip,
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

/// How many finished steps in a row it takes before they fold into a summary.
const toolFoldThreshold = 3;

/// A stretch of steps shown as one piece: a single step, or a run of finished
/// ones folded together. [start] is the index of its first step in the run.
class ToolSegment {
  const ToolSegment(this.start, this.steps, {this.folded = false});

  final int start;
  final List<ToolStep> steps;
  final bool folded;
}

/// Splits [steps] into what the transcript shows: running and failed steps stay
/// on their own, and [toolFoldThreshold] or more finished steps in a row fold.
List<ToolSegment> foldToolSteps(List<ToolStep> steps) {
  final segments = <ToolSegment>[];
  var i = 0;
  while (i < steps.length) {
    if (steps[i].status != ToolStepStatus.completed) {
      segments.add(ToolSegment(i, [steps[i]]));
      i++;
      continue;
    }
    var end = i;
    while (end < steps.length &&
        steps[end].status == ToolStepStatus.completed) {
      end++;
    }
    final run = steps.sublist(i, end);
    if (run.length >= toolFoldThreshold) {
      segments.add(ToolSegment(i, run, folded: true));
    } else {
      for (var k = 0; k < run.length; k++) {
        segments.add(ToolSegment(i + k, [run[k]]));
      }
    }
    i = end;
  }
  return segments;
}

/// "4 steps done" or "4 steps done · made 2 things": the outcome of a folded run.
String foldedStepsSummary(List<ToolStep> steps) {
  final made = steps.fold<int>(0, (sum, step) => sum + step.refs.length);
  final base = steps.length == 1 ? '1 step done' : '${steps.length} steps done';
  return made == 0
      ? base
      : '$base · made $made ${made == 1 ? 'thing' : 'things'}';
}

/// A run of finished steps: one summary chip that opens to every step.
class _FoldedSteps extends StatefulWidget {
  const _FoldedSteps({required this.segment, required this.chipFor, super.key});

  final ToolSegment segment;
  final Widget Function(int index, ToolStep step) chipFor;

  @override
  State<_FoldedSteps> createState() => _FoldedStepsState();
}

class _FoldedStepsState extends State<_FoldedSteps> {
  bool _open = false;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final steps = widget.segment.steps;
    final summary = foldedStepsSummary(steps);
    if (_open) {
      return Wrap(
        spacing: 6,
        runSpacing: 6,
        children: [
          for (final (offset, step) in steps.indexed)
            widget.chipFor(widget.segment.start + offset, step),
          Semantics(
            button: true,
            label: 'Fold $summary',
            excludeSemantics: true,
            child: InkWell(
              key: const Key('tool-fold-collapse'),
              borderRadius: BorderRadius.circular(999),
              onTap: () => setState(() => _open = false),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
                child: Text(
                  'Fold',
                  style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
                ),
              ),
            ),
          ),
        ],
      );
    }
    return Semantics(
      button: true,
      label: '$summary. Show each step',
      excludeSemantics: true,
      child: InkWell(
        key: const Key('tool-fold-expand'),
        borderRadius: BorderRadius.circular(999),
        onTap: () => setState(() => _open = true),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
          decoration: BoxDecoration(
            color: colors.surfaceMuted,
            borderRadius: BorderRadius.circular(999),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                PhosphorIconsRegular.checkCircle,
                size: 14,
                color: colors.success,
              ),
              const SizedBox(width: 6),
              Text(
                summary,
                style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
              ),
              const SizedBox(width: 4),
              Icon(
                PhosphorIconsRegular.caretDown,
                size: 12,
                color: colors.muted,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
