import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Workspaces: a saved profile (and optionally a project) for a job you repeat.
/// Starting one opens an ordinary chat with that profile, so the profile's own
/// limits and switch confirmations apply; a workspace holds no access of its own.
class WorkspacesScreen extends StatefulWidget {
  const WorkspacesScreen({
    required this.http,
    this.onOpenConversation,
    super.key,
  });

  final Dio http;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<WorkspacesScreen> createState() => _WorkspacesScreenState();
}

class _WorkspacesScreenState extends State<WorkspacesScreen> {
  List<Map<String, dynamic>> _items = const [];
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/workspaces');
      if (!mounted) return;
      setState(() {
        _items = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load workspaces.';
      });
    }
  }

  Future<void> _create() async {
    List<Map<String, dynamic>> profiles = const [];
    List<Map<String, dynamic>> projects = const [];
    try {
      profiles = jsonMaps(
        (await widget.http.get<dynamic>('/api/v1/profiles')).data,
      );
      projects = jsonMaps(
        (await widget.http.get<dynamic>('/api/v1/projects')).data,
      );
    } on DioException {
      // The sheet still opens; saving explains what is missing.
    }
    if (!mounted) return;
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _WorkspaceSheet(profiles: profiles, projects: projects),
    );
    if (draft == null) return;
    await _run(
      () => widget.http.post<dynamic>('/api/v1/workspaces', data: draft),
    );
  }

  Future<void> _run(Future<Response<dynamic>> Function() call) async {
    setState(() => _busy = true);
    try {
      await call();
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _launch(Map<String, dynamic> workspace) async {
    final open = widget.onOpenConversation;
    final id = jsonString(workspace, 'id');
    if (open == null || id == null) return;
    setState(() => _busy = true);
    try {
      final launch = jsonObject(
        (await widget.http.post<dynamic>('/api/v1/workspaces/$id/launch')).data,
      );
      final created = await widget.http.post<dynamic>(
        '/api/v1/conversations',
        data: {
          'title': asJsonString(launch?['title']) ?? 'Workspace',
          'profileId': launch?['profileId'],
          'projectId': ?launch?['projectId'],
        },
      );
      final conversationId = jsonId(jsonObject(created.data));
      if (conversationId == null) {
        throw const FormatException('Missing conversation ID.');
      }
      final opening = asJsonString(launch?['openingMessage']);
      if (mounted && opening != null) {
        _toast('Suggested first message: $opening');
      }
      if (mounted) setState(() => _busy = false);
      await open(conversationId);
      if (mounted) unawaited(_load());
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } catch (_) {
      if (mounted) _toast('Jarvis could not start this workspace.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _delete(Map<String, dynamic> workspace) async {
    final id = jsonString(workspace, 'id');
    if (id == null) return;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove workspace?',
      message: 'Chats you already started from it stay as they are.',
      cancelLabel: 'Keep it',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    await _run(() => widget.http.delete<dynamic>('/api/v1/workspaces/$id'));
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Workspaces'),
        actions: [
          HeaderAction(
            key: const Key('workspace-new'),
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => unawaited(_create()),
          ),
        ],
      ),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: _items.isEmpty
                      ? Padding(
                          padding: const EdgeInsets.only(top: 24),
                          child: EmptyState(
                            icon: PhosphorIconsRegular.appWindow,
                            title: 'Start a job the same way every time',
                            message: 'Pick a profile and a project once. Starting it opens a chat with that profile and its limits.',
                            action: PillAction(
                              key: const Key('workspace-start'),
                              primary: true,
                              icon: PhosphorIconsRegular.plus,
                              label: 'New workspace',
                              onPressed: () => unawaited(_create()),
                            ),
                          ),
                        )
                      : Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            for (final (i, view) in _items.indexed)
                              _row(view, i, colors),
                          ],
                        ),
                ),
              ],
            ),
    );
  }

  Widget _row(Map<String, dynamic> view, int index, JarvisColors colors) {
    final workspace = jsonObject(view['workspace']) ?? const {};
    final id = asJsonString(workspace['id']) ?? '';
    final problem = asJsonString(view['problem']);
    final scope = jsonObject(view['scope']);
    final limits = jsonStrings(scope?['limits']);
    return FeatureRow(
      key: Key('workspace-$id'),
      index: index,
      icon: PhosphorIconsRegular.appWindow,
      caption: [
        asJsonString(view['profileName']),
        asJsonString(view['projectName']),
      ].whereType<String>().join(' · ').toUpperCase(),
      title: asJsonString(workspace['name']) ?? '',
      subtitle:
          problem ??
          [
            asJsonString(workspace['instruction']),
            if (scope != null)
              '${asJsonString(scope['memory'])} · ${asJsonString(scope['files'])}',
            if (limits.isNotEmpty) limits.first,
          ].whereType<String>().join('\n'),
      trailing: problem == null
          ? null
          : StatusPill(label: 'Needs a fix', color: colors.warning),
      actions: [
        PillAction(
          key: Key('workspace-launch-$id'),
          compact: true,
          primary: problem == null,
          icon: PhosphorIconsRegular.play,
          label: 'Start',
          onPressed:
              problem != null || _busy || widget.onOpenConversation == null
              ? null
              : () => unawaited(_launch(workspace)),
        ),
        PillAction(
          key: Key('workspace-delete-$id'),
          compact: true,
          label: 'Remove',
          onPressed: _busy ? null : () => unawaited(_delete(workspace)),
        ),
      ],
    );
  }
}

class _WorkspaceSheet extends StatefulWidget {
  const _WorkspaceSheet({required this.profiles, required this.projects});

  final List<Map<String, dynamic>> profiles;
  final List<Map<String, dynamic>> projects;

  @override
  State<_WorkspaceSheet> createState() => _WorkspaceSheetState();
}

class _WorkspaceSheetState extends State<_WorkspaceSheet> {
  final _name = TextEditingController();
  final _instruction = TextEditingController();
  String? _profile;
  String? _project;

  @override
  void dispose() {
    _name.dispose();
    _instruction.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => SafeArea(
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
            'New workspace',
            style: JarvisType.displayOf(context).copyWith(fontSize: 26),
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('workspace-name'),
            controller: _name,
            maxLength: 60,
            decoration: const InputDecoration(
              labelText: 'Name',
              hintText: 'Weekly research',
            ),
          ),
          const Text('Profile', style: TextStyle(fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          Wrap(
            spacing: 8,
            children: [
              for (final profile in widget.profiles)
                ChoiceChip(
                  key: Key('workspace-profile-${profile['id']}'),
                  label: Text(asJsonString(profile['name']) ?? ''),
                  selected: _profile == asJsonString(profile['id']),
                  onSelected: (_) =>
                      setState(() => _profile = asJsonString(profile['id'])),
                ),
            ],
          ),
          if (widget.projects.isNotEmpty) ...[
            const SizedBox(height: 12),
            const Text(
              'Project (optional)',
              style: TextStyle(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 6),
            Wrap(
              spacing: 8,
              children: [
                for (final project in widget.projects)
                  ChoiceChip(
                    key: Key('workspace-project-${project['id']}'),
                    label: Text(asJsonString(project['name']) ?? ''),
                    selected: _project == asJsonString(project['id']),
                    onSelected: (selected) => setState(
                      () => _project = selected
                          ? asJsonString(project['id'])
                          : null,
                    ),
                  ),
              ],
            ),
          ],
          const SizedBox(height: 12),
          TextField(
            key: const Key('workspace-instruction'),
            controller: _instruction,
            maxLength: 400,
            maxLines: 2,
            decoration: const InputDecoration(
              labelText: 'First message (optional)',
              hintText: 'Summarise what changed since last week',
            ),
          ),
          const Text(
            'A workspace gives no extra access. The profile decides what Jarvis may use.',
          ),
          const SizedBox(height: 12),
          PillAction(
            key: const Key('workspace-save'),
            primary: true,
            label: 'Save workspace',
            onPressed: () => Navigator.pop<Map<String, Object?>>(context, {
              'name': _name.text.trim(),
              'profileId': _profile,
              'projectId': ?_project,
              if (_instruction.text.trim().isNotEmpty)
                'instruction': _instruction.text.trim(),
            }),
          ),
        ],
      ),
    ),
  );
}
