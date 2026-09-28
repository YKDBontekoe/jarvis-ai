part of 'home_overview.dart';

/// Fades and lifts the home content in once when it first appears.
class _Entrance extends StatelessWidget {
  const _Entrance({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) => TweenAnimationBuilder<double>(
    tween: Tween(begin: 0, end: 1),
    duration: const Duration(milliseconds: 450),
    curve: Curves.easeOutCubic,
    builder: (context, value, child) => Opacity(
      opacity: value,
      child: Transform.translate(
        offset: Offset(0, 12 * (1 - value)),
        child: child,
      ),
    ),
    child: child,
  );
}

class _TasksPlaceholder extends StatelessWidget {
  const _TasksPlaceholder({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
    decoration: BoxDecoration(
      borderRadius: BorderRadius.circular(JarvisRadii.lg),
      border: Border.all(color: JarvisColors.outline),
      color: JarvisColors.surface,
    ),
    child: Row(
      children: [
        Icon(icon, size: 20, color: JarvisColors.muted),
        const SizedBox(width: 12),
        Expanded(
          child: Text(
            text,
            style: const TextStyle(color: JarvisColors.inkSoft),
          ),
        ),
      ],
    ),
  );
}

class _TaskRow extends StatelessWidget {
  const _TaskRow({required this.task, required this.onTap});

  final _ActiveTask task;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
    onTap: onTap,
    child: Padding(
      padding: const EdgeInsets.fromLTRB(14, 12, 10, 12),
      child: Row(
        children: [
          IconBadge(icon: task.icon),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  task.title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 5),
                StatusPill(label: task.statusLabel, color: task.color),
              ],
            ),
          ),
          const Icon(
            PhosphorIconsRegular.caretRight,
            size: 16,
            color: JarvisColors.muted,
          ),
        ],
      ),
    ),
  );
}

class _ActiveTask {
  const _ActiveTask({
    required this.id,
    required this.title,
    required this.status,
    required this.createdAt,
  });

  final String id;
  final String title;
  final String status;
  final DateTime createdAt;

  static _ActiveTask? fromJson(dynamic value) {
    final map = jsonObject(value);
    if (map == null) return null;
    final id = asJsonString(map['id']);
    final title = asJsonString(map['title']);
    final status = asJsonString(map['status']);
    if (id == null ||
        id.isEmpty ||
        title == null ||
        status == null ||
        !const {
          'queued',
          'running',
          'waiting',
          'needs_approval',
        }.contains(status)) {
      return null;
    }
    final date = jsonDate(map['createdAt']);
    if (date == null) return null;
    return _ActiveTask(id: id, title: title, status: status, createdAt: date);
  }

  int get priority => switch (status) {
    'needs_approval' => 0,
    'running' => 1,
    'waiting' => 2,
    _ => 3,
  };

  String get statusLabel => switch (status) {
    'needs_approval' => 'Needs your approval',
    'running' => 'In progress',
    'waiting' => 'Waiting',
    _ => 'Queued',
  };

  IconData get icon => switch (status) {
    'needs_approval' => PhosphorIconsRegular.shieldWarning,
    'running' => PhosphorIconsRegular.hourglassMedium,
    'waiting' => PhosphorIconsRegular.pauseCircle,
    _ => PhosphorIconsRegular.clock,
  };

  Color get color => statusStyle(status).color;
}
