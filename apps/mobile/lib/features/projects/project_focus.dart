part of 'project_screen.dart';

/// What the owner picked in the focus sheet. [linkType] and [linkId] are both set or both null.
typedef _FocusChoice = ({
  String outcome,
  String status,
  String? linkType,
  String? linkId,
  String? linkTitle,
  String note,
});

const _projectStatuses = {
  'active': 'Active',
  'completed': 'Completed',
  'archived': 'Archived',
};

/// The project's point: what it should achieve and the one step to take next. Tapping it edits both.
class _FocusCard extends StatelessWidget {
  const _FocusCard({
    required this.outcome,
    required this.status,
    required this.nextAction,
    required this.onEdit,
    this.onOpenNext,
  });

  final String? outcome;
  final String status;

  /// The server's view of the next action: `type`, `id`, `note`, `title` and `state` (open, done or gone).
  final Map<String, dynamic>? nextAction;
  final VoidCallback onEdit;
  final VoidCallback? onOpenNext;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = outcome?.trim() ?? '';
    final next = nextAction;
    final state = asJsonString(next?['state']);
    final title = asJsonString(next?['title']) ?? asJsonString(next?['note']);
    final note = asJsonString(next?['title']) == null
        ? null
        : asJsonString(next?['note']);
    return SurfaceCard(
      key: const Key('project-focus'),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
      onTap: onEdit,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(PhosphorIconsRegular.target, size: 16, color: colors.ink),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Outcome and next step',
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: colors.ink,
                  ),
                ),
              ),
              if (status != 'active')
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: StatusPill(
                    label: _projectStatuses[status] ?? status,
                    color: status == 'completed' ? colors.accent : colors.muted,
                  ),
                ),
              Text(
                text.isEmpty && next == null ? 'Set' : 'Edit',
                style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w500,
                  color: colors.accent,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            text.isEmpty ? 'What should this project achieve?' : text,
            style: TextStyle(
              fontSize: 15,
              height: 1.4,
              fontWeight: text.isEmpty ? FontWeight.w400 : FontWeight.w500,
              color: text.isEmpty ? colors.muted : colors.ink,
            ),
          ),
          const SizedBox(height: 12),
          Container(
            key: const Key('project-next-action'),
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: colors.surfaceMuted,
              borderRadius: BorderRadius.circular(JarvisRadii.md),
            ),
            child: Row(
              children: [
                Icon(
                  state == 'done'
                      ? PhosphorIconsRegular.checkCircle
                      : PhosphorIconsRegular.arrowUpRight,
                  size: 20,
                  color: state == 'done' ? colors.accent : colors.inkSoft,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Next step',
                        style: TextStyle(fontSize: 12, color: colors.muted),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        title ?? 'Nothing chosen yet',
                        style: TextStyle(
                          fontSize: 14.5,
                          fontWeight: FontWeight.w600,
                          color: title == null ? colors.muted : colors.ink,
                          decoration: state == 'done'
                              ? TextDecoration.lineThrough
                              : null,
                        ),
                      ),
                      if (note != null) ...[
                        const SizedBox(height: 2),
                        Text(
                          note,
                          style: TextStyle(fontSize: 13, color: colors.inkSoft),
                        ),
                      ],
                      if (state == 'done')
                        Padding(
                          padding: const EdgeInsets.only(top: 4),
                          child: Text(
                            'Done. Choose what comes next.',
                            style: TextStyle(
                              fontSize: 13,
                              color: colors.accent,
                            ),
                          ),
                        ),
                      if (state == 'gone')
                        Padding(
                          padding: const EdgeInsets.only(top: 4),
                          child: Text(
                            'That item no longer exists. Choose a new next step.',
                            style: TextStyle(
                              fontSize: 13,
                              color: colors.warning,
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
                if (onOpenNext != null && state != 'gone')
                  TextButton(
                    key: const Key('project-open-next'),
                    onPressed: onOpenNext,
                    child: const Text('Open'),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Edits the outcome, status and next step in one place.
class _FocusSheet extends StatefulWidget {
  const _FocusSheet({
    required this.outcome,
    required this.status,
    required this.nextAction,
    required this.loadPromises,
  });

  final String? outcome;
  final String status;
  final Map<String, dynamic>? nextAction;

  /// The promises that can be linked, loaded when the owner asks to link one.
  final Future<List<_PickItem>> Function() loadPromises;

  @override
  State<_FocusSheet> createState() => _FocusSheetState();
}

class _FocusSheetState extends State<_FocusSheet> {
  late final _outcome = TextEditingController(text: widget.outcome ?? '');
  late final _note = TextEditingController(
    text: asJsonString(widget.nextAction?['note']) ?? '',
  );
  late String _status = _projectStatuses.containsKey(widget.status)
      ? widget.status
      : 'active';
  String? _linkType;
  String? _linkId;
  String? _linkTitle;
  bool _loadingPromises = false;
  String? _problem;

  @override
  void initState() {
    super.initState();
    _linkType = asJsonString(widget.nextAction?['type']);
    _linkId = asJsonString(widget.nextAction?['id']);
    _linkTitle = asJsonString(widget.nextAction?['title']);
  }

  @override
  void dispose() {
    _outcome.dispose();
    _note.dispose();
    super.dispose();
  }

  Future<void> _linkPromise() async {
    setState(() {
      _loadingPromises = true;
      _problem = null;
    });
    final List<_PickItem> items;
    try {
      items = await widget.loadPromises();
    } catch (_) {
      if (mounted) {
        setState(() {
          _loadingPromises = false;
          _problem = 'Jarvis could not load your promises.';
        });
      }
      return;
    }
    if (!mounted) return;
    setState(() => _loadingPromises = false);
    final id = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _PickerSheet(
        title: 'Link a promise',
        empty: 'You have no open promises. Add one in Inbox first.',
        items: items,
      ),
    );
    if (id == null || !mounted) return;
    final picked = items.firstWhere((item) => item.id == id);
    setState(() {
      _linkType = 'commitment';
      _linkId = id;
      _linkTitle = picked.title;
    });
  }

  void _save() {
    Navigator.pop<_FocusChoice>(context, (
      outcome: _outcome.text.trim(),
      status: _status,
      linkType: _linkType,
      linkId: _linkId,
      linkTitle: _linkTitle,
      note: _note.text.trim(),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SafeArea(
      child: SingleChildScrollView(
        padding: EdgeInsets.fromLTRB(
          20,
          0,
          20,
          20 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Outcome and next step',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 14),
            TextField(
              key: const Key('project-outcome-field'),
              controller: _outcome,
              maxLength: 300,
              maxLines: 3,
              minLines: 2,
              decoration: const InputDecoration(
                labelText: 'Outcome',
                hintText: 'What does success look like?',
              ),
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final entry in _projectStatuses.entries)
                  ChoiceChip(
                    key: Key('project-status-${entry.key}'),
                    label: Text(entry.value),
                    selected: _status == entry.key,
                    onSelected: (_) => setState(() => _status = entry.key),
                  ),
              ],
            ),
            const SizedBox(height: 18),
            TextField(
              key: const Key('project-next-note-field'),
              controller: _note,
              maxLength: 200,
              decoration: const InputDecoration(
                labelText: 'Next step',
                hintText: 'The one thing to do next',
              ),
            ),
            if (_linkId != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  children: [
                    Icon(PhosphorIconsRegular.hand, size: 18, color: colors.ink),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        'Linked: ${_linkTitle ?? 'a promise'}',
                        key: const Key('project-link-title'),
                        style: TextStyle(fontSize: 14, color: colors.inkSoft),
                      ),
                    ),
                    TextButton(
                      onPressed: () => setState(() {
                        _linkType = null;
                        _linkId = null;
                        _linkTitle = null;
                      }),
                      child: const Text('Unlink'),
                    ),
                  ],
                ),
              )
            else
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  key: const Key('project-link-promise'),
                  onPressed: _loadingPromises ? null : _linkPromise,
                  icon: const Icon(PhosphorIconsRegular.link, size: 18),
                  label: Text(
                    _loadingPromises ? 'Loading…' : 'Link one of my promises',
                  ),
                ),
              ),
            if (_problem != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  _problem!,
                  style: TextStyle(fontSize: 13, color: colors.danger),
                ),
              ),
            const SizedBox(height: 14),
            FilledButton(
              key: const Key('project-focus-save'),
              onPressed: _save,
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
  }
}
