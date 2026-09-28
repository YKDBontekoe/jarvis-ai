import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Visual label for where a skill came from.
({String label, IconData icon, Color color}) skillSource(String source) =>
    switch (source) {
      'learned' => (
        label: 'Learned',
        icon: PhosphorIconsRegular.sparkle,
        color: JarvisColors.violet,
      ),
      'imported' => (
        label: 'Imported',
        icon: PhosphorIconsRegular.downloadSimple,
        color: JarvisColors.info,
      ),
      _ => (
        label: 'Yours',
        icon: PhosphorIconsRegular.user,
        color: JarvisColors.accent,
      ),
    };

class SkillsScreen extends StatefulWidget {
  const SkillsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<SkillsScreen> createState() => _SkillsScreenState();
}

class _SkillsScreenState extends State<SkillsScreen> {
  List<Map<String, dynamic>> _skills = const [];
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/skills');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _skills = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load skills.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load skills.';
      });
    }
  }

  Future<void> _open(Map<String, dynamic> skill) async {
    final id = asJsonString(skill['id']);
    if (id == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => SkillDetailScreen(http: widget.http, skillId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create({bool import = false}) async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => SkillEditorSheet(http: widget.http, import: import),
    );
    if (created == true && mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) {
    List<Map<String, dynamic>> byStatus(String status) =>
        _skills.where((skill) => skill['status'] == status).toList();
    final proposed = byStatus('proposed');
    final active = byStatus('active');
    final disabled = byStatus('disabled');
    return Scaffold(
      appBar: AppBar(
        title: const Text('Skills'),
        actions: [
          IconButton(
            tooltip: 'Import SKILL.md',
            onPressed: () => unawaited(_create(import: true)),
            icon: const Icon(PhosphorIconsRegular.downloadSimple),
          ),
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => unawaited(_create()),
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: _skills.isEmpty,
        onRetry: () => unawaited(_load()),
        empty: EmptyState(
          icon: PhosphorIconsRegular.magicWand,
          title: 'No skills yet',
          message:
              'Jarvis writes a skill when it finishes a workflow you are likely to repeat, or when you teach it how you like something done. You can also add or import SKILL.md files.',
          action: FilledButton.icon(
            onPressed: () => unawaited(_create()),
            icon: const Icon(PhosphorIconsRegular.plus, size: 18),
            label: const Text('Write a skill'),
          ),
        ),
        child: RefreshIndicator(
          onRefresh: _load,
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            children: [
              ContentWidth(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const InlineNotice(
                      tone: NoticeTone.info,
                      message:
                          'Skills are reusable procedures Jarvis loads only when relevant. Learned skills improve over time; skills you edit are locked so Jarvis never overwrites them.',
                    ),
                    if (proposed.isNotEmpty)
                      _section('Needs your review', proposed),
                    if (active.isNotEmpty) _section('Active', active),
                    if (disabled.isNotEmpty) _section('Disabled', disabled),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _section(String title, List<Map<String, dynamic>> skills) => Padding(
    padding: const EdgeInsets.only(top: 20),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(title),
        GroupedSection(
          dividerIndent: 64,
          children: [for (final skill in skills) _tile(skill)],
        ),
      ],
    ),
  );

  Widget _tile(Map<String, dynamic> skill) {
    final source = skillSource(asJsonString(skill['source']) ?? 'user');
    final uses = asJsonInt(skill['useCount']);
    return ListTile(
      key: Key('skill-${skill['name']}'),
      leading: IconBadge(icon: source.icon, color: source.color, size: 36),
      title: Row(
        children: [
          Flexible(
            child: Text(
              asJsonString(skill['name']) ?? '',
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
          ),
          if (asJsonBool(skill['isLocked'])) ...[
            const SizedBox(width: 6),
            const Icon(
              PhosphorIconsRegular.lockSimple,
              size: 14,
              color: JarvisColors.muted,
            ),
          ],
        ],
      ),
      subtitle: Text(
        '${asJsonString(skill['description']) ?? ''}\n'
        '${source.label} · v${asJsonInt(skill['version'], 1)} · used $uses×',
        maxLines: 3,
        overflow: TextOverflow.ellipsis,
      ),
      isThreeLine: true,
      trailing: const Icon(
        PhosphorIconsRegular.caretRight,
        size: 16,
        color: JarvisColors.muted,
      ),
      onTap: () => unawaited(_open(skill)),
    );
  }
}

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

  String get _path => '/api/v1/skills/${widget.skillId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      final data = jsonObject(response.data) ?? const {};
      if (!mounted) return;
      setState(() {
        _skill = jsonObject(data['skill']);
        _revisions = jsonMaps(data['revisions']);
        _error = _skill == null ? 'Jarvis returned an invalid skill.' : null;
      });
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not load this skill.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Could not load this skill.');
      }
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
      message:
          'Jarvis stops using it immediately. Its revision history is removed too.',
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
                color: JarvisColors.inkSoft,
              ),
              StatusPill(
                label: 'Used ${asJsonInt(skill['useCount'])}×',
                color: JarvisColors.inkSoft,
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
                  icon: skillSource(
                    asJsonString(revision['source']) ?? '',
                  ).icon,
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
    final parsed = DateTime.tryParse(iso ?? '')?.toLocal();
    if (parsed == null) return null;
    String two(int value) => value.toString().padLeft(2, '0');
    return '${parsed.year}-${two(parsed.month)}-${two(parsed.day)} ${two(parsed.hour)}:${two(parsed.minute)}';
  }
}

/// Creates, edits, or imports a skill.
class SkillEditorSheet extends StatefulWidget {
  const SkillEditorSheet({
    required this.http,
    this.existing,
    this.import = false,
    super.key,
  });

  final Dio http;
  final Map<String, dynamic>? existing;
  final bool import;

  @override
  State<SkillEditorSheet> createState() => _SkillEditorSheetState();
}

class _SkillEditorSheetState extends State<SkillEditorSheet> {
  late final _name = TextEditingController(
    text: asJsonString(widget.existing?['name']) ?? '',
  );
  late final _description = TextEditingController(
    text: asJsonString(widget.existing?['description']) ?? '',
  );
  late final _instructions = TextEditingController(
    text: asJsonString(widget.existing?['instructions']) ?? '',
  );
  final _markdown = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _instructions.dispose();
    _markdown.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      if (widget.import) {
        await widget.http.post<void>(
          '/api/v1/skills/import',
          data: {'markdown': _markdown.text},
        );
      } else if (widget.existing != null) {
        await widget.http.put<void>(
          '/api/v1/skills/${widget.existing!['id']}',
          data: {
            'description': _description.text,
            'instructions': _instructions.text,
          },
        );
      } else {
        await widget.http.post<void>(
          '/api/v1/skills',
          data: {
            'name': _name.text,
            'description': _description.text,
            'instructions': _instructions.text,
          },
        );
      }
      if (mounted) Navigator.pop(context, true);
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'The skill could not be saved.',
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(
      20,
      8,
      20,
      20 + MediaQuery.of(context).viewInsets.bottom,
    ),
    child: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            widget.import
                ? 'Import SKILL.md'
                : widget.existing == null
                ? 'New skill'
                : 'Edit skill',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 6),
          Text(
            widget.import
                ? 'Paste a SKILL.md file from OpenClaw, Hermes, Claude Code, Codex, or another Jarvis.'
                : 'Saving locks the skill so Jarvis never overwrites your version.',
            style: const TextStyle(color: JarvisColors.inkSoft),
          ),
          const SizedBox(height: 16),
          if (widget.import)
            TextField(
              key: const Key('skill-markdown'),
              controller: _markdown,
              minLines: 10,
              maxLines: 18,
              decoration: const InputDecoration(
                hintText:
                    '---\nname: my-skill\ndescription: When to use it\n---\n\n1. First step…',
              ),
            )
          else ...[
            if (widget.existing == null)
              TextField(
                key: const Key('skill-name'),
                controller: _name,
                decoration: const InputDecoration(
                  labelText: 'Name',
                  helperText:
                      'Lowercase words joined by hyphens, e.g. weekly-review',
                ),
              ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('skill-description'),
              controller: _description,
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: 'When should Jarvis use it?',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('skill-instructions'),
              controller: _instructions,
              minLines: 6,
              maxLines: 14,
              decoration: const InputDecoration(
                labelText: 'Instructions (Markdown)',
                alignLabelWithHint: true,
              ),
            ),
          ],
          if (_error != null)
            InlineNotice(
              message: _error!,
              tone: NoticeTone.danger,
              margin: const EdgeInsets.only(top: 12),
            ),
          const SizedBox(height: 16),
          FilledButton(
            key: const Key('save-skill'),
            onPressed: _saving ? null : () => unawaited(_save()),
            child: Text(widget.import ? 'Import' : 'Save'),
          ),
        ],
      ),
    ),
  );
}
