part of 'project_screen.dart';

typedef _PickItem = ({String id, String title, String subtitle, IconData icon});

class _ProjectHeader extends StatelessWidget {
  const _ProjectHeader({required this.project});

  final Map<String, dynamic> project;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final description = asJsonString(project['description']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        ProjectBadge(color: asJsonString(project['color']), size: 52),
        const SizedBox(height: 14),
        Text(
          asJsonString(project['name']) ?? 'Untitled project',
          style: JarvisType.displayOf(context).copyWith(fontSize: 32),
        ),
        if (description != null && description.isNotEmpty) ...[
          const SizedBox(height: 6),
          Text(
            description,
            style: TextStyle(fontSize: 15, height: 1.45, color: colors.inkSoft),
          ),
        ],
        const SizedBox(height: 8),
        Text(
          projectCountsLabel(project),
          style: TextStyle(fontSize: 13, color: colors.muted),
        ),
      ],
    );
  }
}

/// The main action: a new chat that carries the project's instructions.
class _NewChatCard extends StatelessWidget {
  const _NewChatCard({
    required this.color,
    required this.busy,
    required this.onTap,
  });

  final String? color;
  final bool busy;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final tone = projectColor(context, color);
    return SurfaceCard(
      elevated: true,
      padding: const EdgeInsets.fromLTRB(16, 14, 14, 14),
      onTap: busy ? null : onTap,
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(color: tone, shape: BoxShape.circle),
            child: busy
                ? const Padding(
                    padding: EdgeInsets.all(11),
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : const Icon(
                    PhosphorIconsRegular.notePencil,
                    size: 19,
                    color: Colors.white,
                  ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'New chat in this project',
                  style: TextStyle(
                    fontSize: 15.5,
                    fontWeight: FontWeight.w600,
                    letterSpacing: -.2,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  onTap == null
                      ? 'Open this project from the sidebar to start a chat.'
                      : 'Jarvis knows the instructions and files.',
                  style: TextStyle(fontSize: 13, color: colors.inkSoft),
                ),
              ],
            ),
          ),
          Icon(
            PhosphorIconsRegular.arrowUpRight,
            size: 18,
            color: colors.muted,
          ),
        ],
      ),
    );
  }
}

class _InstructionsCard extends StatelessWidget {
  const _InstructionsCard({required this.instructions, required this.onEdit});

  final String? instructions;
  final VoidCallback onEdit;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = instructions?.trim() ?? '';
    return SurfaceCard(
      color: colors.surfaceMuted,
      borderColor: Colors.transparent,
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
      onTap: onEdit,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(PhosphorIconsRegular.sparkle, size: 16, color: colors.ink),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Instructions',
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: colors.ink,
                  ),
                ),
              ),
              Text(
                text.isEmpty ? 'Add' : 'Edit',
                style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w500,
                  color: colors.accent,
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            text.isEmpty
                ? 'Tell Jarvis how to work in this project: the goal, the '
                      'tone, or facts to keep in mind.'
                : text,
            maxLines: 6,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 14,
              height: 1.45,
              color: text.isEmpty ? colors.muted : colors.inkSoft,
            ),
          ),
        ],
      ),
    );
  }
}

class _SectionAction {
  const _SectionAction({
    required this.icon,
    required this.label,
    required this.onPressed,
    this.primary = false,
  });

  final IconData icon;
  final String label;
  final VoidCallback? onPressed;
  final bool primary;
}

/// A tab's rows, or a friendly empty card, with its actions underneath.
class _Section extends StatelessWidget {
  const _Section({
    required this.emptyIcon,
    required this.emptyTitle,
    required this.emptyMessage,
    required this.actions,
    required this.rows,
  });

  final IconData emptyIcon;
  final String emptyTitle;
  final String emptyMessage;
  final List<_SectionAction> actions;
  final List<Widget> rows;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final buttons = Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final action in actions)
          action.primary
              ? FilledButton.tonalIcon(
                  onPressed: action.onPressed,
                  icon: Icon(action.icon, size: 17),
                  label: Text(action.label),
                )
              : OutlinedButton.icon(
                  onPressed: action.onPressed,
                  icon: Icon(action.icon, size: 17),
                  label: Text(action.label),
                ),
      ],
    );
    if (rows.isEmpty) {
      return SurfaceCard(
        padding: const EdgeInsets.fromLTRB(18, 20, 18, 18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            IconBadge(icon: emptyIcon, color: colors.inkSoft),
            const SizedBox(height: 12),
            Text(emptyTitle, style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 4),
            Text(
              emptyMessage,
              style: TextStyle(
                fontSize: 13.5,
                height: 1.45,
                color: colors.inkSoft,
              ),
            ),
            const SizedBox(height: 16),
            buttons,
          ],
        ),
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        GroupedSection(dividerIndent: 62, children: rows),
        const SizedBox(height: 12),
        buttons,
      ],
    );
  }
}

class _ItemRow extends StatelessWidget {
  const _ItemRow({
    required this.icon,
    required this.title,
    required this.subtitle,
    this.trailing,
    this.onTap,
    this.onRemove,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final Widget? trailing;
  final VoidCallback? onTap;
  final VoidCallback? onRemove;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 10, 2, 10),
        child: Row(
          children: [
            IconBadge(icon: icon, size: 34, color: colors.inkSoft),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 14.5,
                      fontWeight: FontWeight.w500,
                      color: colors.ink,
                    ),
                  ),
                  if (subtitle.isNotEmpty) ...[
                    const SizedBox(height: 2),
                    Text(
                      subtitle,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontSize: 12.5, color: colors.muted),
                    ),
                  ],
                ],
              ),
            ),
            if (trailing != null) ...[const SizedBox(width: 8), trailing!],
            PopupMenuButton<String>(
              tooltip: 'Actions for $title',
              enabled: onRemove != null,
              icon: Icon(
                PhosphorIconsRegular.dotsThree,
                size: 20,
                color: colors.inkSoft,
              ),
              onSelected: (_) => onRemove?.call(),
              itemBuilder: (_) => const [
                PopupMenuItem(
                  value: 'remove',
                  child: Row(
                    children: [
                      Icon(PhosphorIconsRegular.minusCircle, size: 18),
                      SizedBox(width: 12),
                      Flexible(child: Text('Remove from project')),
                    ],
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Picks one chat or file to bring into the project.
class _PickerSheet extends StatefulWidget {
  const _PickerSheet({
    required this.title,
    required this.empty,
    required this.items,
  });

  final String title;
  final String empty;
  final List<_PickItem> items;

  @override
  State<_PickerSheet> createState() => _PickerSheetState();
}

class _PickerSheetState extends State<_PickerSheet> {
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final query = _query.trim().toLowerCase();
    final visible = query.isEmpty
        ? widget.items
        : widget.items
              .where((item) => item.title.toLowerCase().contains(query))
              .toList();
    return SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * .75,
        ),
        child: Padding(
          padding: EdgeInsets.only(
            bottom: MediaQuery.viewInsetsOf(context).bottom,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
                child: Text(
                  widget.title,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              if (widget.items.length > 6)
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                  child: TextField(
                    onChanged: (value) => setState(() => _query = value),
                    textInputAction: TextInputAction.search,
                    decoration: const InputDecoration(
                      hintText: 'Search',
                      prefixIcon: Icon(
                        PhosphorIconsRegular.magnifyingGlass,
                        size: 20,
                      ),
                    ),
                  ),
                ),
              if (visible.isEmpty)
                Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 28),
                  child: Text(
                    widget.items.isEmpty ? widget.empty : 'Nothing matches.',
                    style: TextStyle(fontSize: 14, color: colors.inkSoft),
                  ),
                )
              else
                Flexible(
                  child: ListView.builder(
                    shrinkWrap: true,
                    padding: const EdgeInsets.fromLTRB(8, 0, 8, 16),
                    itemCount: visible.length,
                    itemBuilder: (context, index) {
                      final item = visible[index];
                      return ListTile(
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(JarvisRadii.md),
                        ),
                        leading: IconBadge(icon: item.icon, size: 34),
                        title: Text(
                          item.title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        subtitle: item.subtitle.isEmpty
                            ? null
                            : Text(
                                item.subtitle,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                              ),
                        onTap: () => Navigator.pop(context, item.id),
                      );
                    },
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _NewProjectTaskDialog extends StatefulWidget {
  const _NewProjectTaskDialog();

  @override
  State<_NewProjectTaskDialog> createState() => _NewProjectTaskDialogState();
}

class _NewProjectTaskDialogState extends State<_NewProjectTaskDialog> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _prompt = TextEditingController();

  @override
  void dispose() {
    _title.dispose();
    _prompt.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    Navigator.pop(context, (
      title: _title.text.trim(),
      prompt: _prompt.text.trim(),
    ));
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('New project task'),
    content: Form(
      key: _formKey,
      child: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextFormField(
              controller: _title,
              autofocus: true,
              maxLength: 200,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Title'),
              validator: (value) => (value ?? '').trim().isEmpty
                  ? 'Give the task a title.'
                  : null,
            ),
            const SizedBox(height: 8),
            TextFormField(
              controller: _prompt,
              minLines: 3,
              maxLines: 8,
              maxLength: 32000,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'What should Jarvis do?',
                alignLabelWithHint: true,
              ),
              validator: (value) => (value ?? '').trim().isEmpty
                  ? 'Describe what Jarvis should do.'
                  : null,
            ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _save, child: const Text('Start task')),
    ],
  );
}
