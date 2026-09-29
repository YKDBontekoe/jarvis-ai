part of 'skills_screen.dart';

class SkillDetailScreen extends StatefulWidget {
  const SkillDetailScreen({
    required this.http,
    required this.skillId,
    super.key,
  });

  final Dio http;
  final String skillId;

  @override
  State<SkillDetailScreen> createState() => _SkillDetailScreenState();
}

class _SkillDetailScreenState extends State<SkillDetailScreen> {
  Map<String, dynamic>? _skill;
  List<Map<String, dynamic>> _revisions = const [];
  String? _error;
  bool _busy = false;
  int _requestRevision = 0;

  String get _path => '/api/v1/skills/${widget.skillId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(_path);
      final data = jsonObject(response.data) ?? const {};
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _skill = jsonObject(data['skill']);
        _revisions = jsonMaps(data['revisions']);
        _error = _skill == null ? 'Jarvis returned an invalid skill.' : null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this skill.',
      );
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() => _error = 'Could not load this skill.');
    }
  }

  Future<void> _act(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Jarvis could not update this skill.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Jarvis could not update this skill.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _setStatus(String status) => _act(
    () => widget.http.post<void>('$_path/status', data: {'status': status}),
  );

  Future<void> _toggleLock(bool locked) => _act(
    () => widget.http.post<void>('$_path/lock', data: {'locked': locked}),
  );

  Future<void> _export() async {
    try {
      final response = await widget.http.get<dynamic>(
        '$_path/export',
        options: Options(responseType: ResponseType.plain),
      );
      final text = asJsonString(response.data) ?? '';
      await Clipboard.setData(ClipboardData(text: text));
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('SKILL.md copied to the clipboard.')),
        );
      }
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not export this skill.',
      );
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not export this skill.');
    }
  }

  Future<void> _edit() async {
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => SkillEditorSheet(http: widget.http, existing: _skill),
    );
    if (saved == true) await _load();
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this skill?',
      message: 'Jarvis stops using it immediately. Its revision history is removed too.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<void>(_path);
      if (mounted) Navigator.pop(context);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not delete this skill.',
      );
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not delete this skill.');
    }
  }

  @override
  Widget build(BuildContext context) {
    final skill = _skill;
    return Scaffold(
      appBar: AppBar(
        title: Text(asJsonString(skill?['name']) ?? 'Skill'),
        actions: [
          if (skill != null)
            PopupMenuButton<String>(
              tooltip: 'More',
              onSelected: (value) => switch (value) {
                'export' => unawaited(_export()),
                'delete' => unawaited(_delete()),
                _ => null,
              },
              itemBuilder: (_) => const [
                PopupMenuItem(value: 'export', child: Text('Copy as SKILL.md')),
                PopupMenuItem(value: 'delete', child: Text('Delete')),
              ],
            ),
        ],
      ),
      body: skill == null
          ? (_error == null
                ? const LoadingState()
                : ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  ))
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _header(skill),
                      _instructions(skill),
                      _history(),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _header(Map<String, dynamic> skill) {
    final status = asJsonString(skill['status']) ?? 'active';
    final source = skillSource(asJsonString(skill['source']) ?? 'user');
    final locked = asJsonBool(skill['isLocked']);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              StatusPill.forStatus(status),
              StatusPill(label: source.label, color: source.color),
              StatusPill(
                label: 'v${asJsonInt(skill['version'], 1)}',
                color: JarvisColors.of(context).inkSoft,
              ),
              StatusPill(
                label: 'Used ${asJsonInt(skill['useCount'])}×',
                color: JarvisColors.of(context).inkSoft,
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            asJsonString(skill['description']) ?? '',
            style: Theme.of(context).textTheme.bodyLarge,
          ),
          if (_error != null)
            InlineNotice(
              message: _error!,
              tone: NoticeTone.danger,
              margin: const EdgeInsets.only(top: 12),
            ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              if (status != 'active')
                FilledButton.icon(
                  key: const Key('activate-skill'),
                  onPressed: _busy
                      ? null
                      : () => unawaited(_setStatus('active')),
                  icon: const Icon(PhosphorIconsRegular.checkCircle, size: 18),
                  label: Text(status == 'proposed' ? 'Approve' : 'Activate'),
                )
              else
                OutlinedButton.icon(
                  onPressed: _busy
                      ? null
                      : () => unawaited(_setStatus('disabled')),
                  icon: const Icon(PhosphorIconsRegular.pauseCircle, size: 18),
                  label: const Text('Disable'),
                ),
              OutlinedButton.icon(
                onPressed: _busy ? null : () => unawaited(_edit()),
                icon: const Icon(PhosphorIconsRegular.pencilSimple, size: 18),
                label: const Text('Edit'),
              ),
              OutlinedButton.icon(
                onPressed: _busy ? null : () => unawaited(_toggleLock(!locked)),
                icon: Icon(
                  locked
                      ? PhosphorIconsRegular.lockSimple
                      : PhosphorIconsRegular.sparkle,
                  size: 18,
                ),
                label: Text(locked ? 'Locked' : 'Jarvis may improve'),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _instructions(Map<String, dynamic> skill) => Padding(
    padding: const EdgeInsets.only(top: 20),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader('Instructions'),
        SurfaceCard(
          child: MarkdownBody(
            data: asJsonString(skill['instructions']) ?? '',
            selectable: true,
          ),
        ),
      ],
    ),
  );

  Widget _history() => Padding(
    padding: const EdgeInsets.only(top: 20),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader('History'),
        GroupedSection(
          children: [
            for (final revision in _revisions)
              ListTile(
                leading: IconBadge(
                  icon: skillSource(asJsonString(revision['source']) ?? '')
                      .icon,
                  size: 32,
                ),
                title: Text(
                  'Version ${asJsonInt(revision['version'])} · ${skillSource(asJsonString(revision['source']) ?? '').label}',
                ),
                subtitle: Text(
                  [
                    asJsonString(revision['changeNote']),
                    _date(asJsonString(revision['createdAt'])),
                  ].whereType<String>().join('\n'),
                ),
              ),
          ],
        ),
      ],
    ),
  );

  String? _date(String? iso) {
    final parsed = jsonDate(iso, local: true);
    if (parsed == null) return null;
    String two(int value) => value.toString().padLeft(2, '0');
    return '${parsed.year}-${two(parsed.month)}-${two(parsed.day)} ${two(parsed.hour)}:${two(parsed.minute)}';
  }
}
