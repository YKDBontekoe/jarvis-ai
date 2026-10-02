import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'project_editor.dart';
import 'project_screen.dart';
import 'project_style.dart';

/// Every project, most recently used first.
class ProjectsScreen extends StatefulWidget {
  const ProjectsScreen({
    required this.http,
    this.onOpenConversation,
    super.key,
  });

  final Dio http;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<ProjectsScreen> createState() => _ProjectsScreenState();
}

class _ProjectsScreenState extends State<ProjectsScreen> {
  List<Map<String, dynamic>> _projects = const [];
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
      final response = await widget.http.get<dynamic>('/api/v1/projects');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _projects = jsonMaps(
          response.data,
        ).where((item) => jsonString(item, 'id') != null).toList();
        _loading = false;
        _error = null;
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Jarvis could not load your projects.';
      });
    }
  }

  Future<void> _create() async {
    final created = await Navigator.of(context).push<Map<String, dynamic>>(
      MaterialPageRoute(builder: (_) => ProjectEditorScreen(http: widget.http)),
    );
    if (!mounted || created == null) return;
    final id = jsonString(created, 'id');
    if (id != null) await _open(id);
    if (mounted) unawaited(_load());
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => ProjectScreen(
          http: widget.http,
          projectId: id,
          onOpenConversation: widget.onOpenConversation,
        ),
      ),
    );
    if (mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Projects'),
      actions: [
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
      isEmpty: _projects.isEmpty,
      onRetry: _load,
      onRefresh: _load,
      empty: EmptyState(
        icon: PhosphorIconsRegular.folders,
        title: 'Start a project',
        message:
            'Keep chats, files and tasks about one goal together, with '
            'instructions Jarvis follows in every chat.',
        action: FilledButton.icon(
          onPressed: () => unawaited(_create()),
          icon: const Icon(PhosphorIconsRegular.plus, size: 18),
          label: const Text('New project'),
        ),
      ),
      child: ListView.builder(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        itemCount: _projects.length,
        itemBuilder: (context, index) {
          final project = _projects[index];
          return ContentWidth(
            child: FadeSlideIn(
              index: index,
              child: ProjectCard(
                project: project,
                onTap: () => unawaited(_open(jsonString(project, 'id')!)),
              ),
            ),
          );
        },
      ),
    ),
  );
}

/// A project row: badge, name, description and what it holds.
class ProjectCard extends StatelessWidget {
  const ProjectCard({required this.project, required this.onTap, super.key});

  final Map<String, dynamic> project;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final description = asJsonString(project['description']);
    final hasInstructions =
        (asJsonString(project['instructions']) ?? '').isNotEmpty;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(14, 14, 12, 14),
      onTap: onTap,
      child: Row(
        children: [
          ProjectBadge(color: asJsonString(project['color']), size: 44),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  asJsonString(project['name']) ?? 'Untitled project',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(
                    context,
                  ).textTheme.titleSmall?.copyWith(fontSize: 15.5),
                ),
                if (description != null && description.isNotEmpty) ...[
                  const SizedBox(height: 2),
                  Text(
                    description,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 13.5,
                      height: 1.35,
                      color: colors.inkSoft,
                    ),
                  ),
                ],
                const SizedBox(height: 6),
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        projectCountsLabel(project),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12.5, color: colors.muted),
                      ),
                    ),
                    if (hasInstructions) ...[
                      const SizedBox(width: 8),
                      Icon(
                        PhosphorIconsRegular.sparkle,
                        size: 13,
                        color: colors.muted,
                        semanticLabel: 'Has instructions',
                      ),
                    ],
                  ],
                ),
              ],
            ),
          ),
          Icon(PhosphorIconsRegular.caretRight, size: 18, color: colors.muted),
        ],
      ),
    );
  }
}
