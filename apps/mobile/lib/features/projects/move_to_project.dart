import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'project_editor.dart';
import 'project_style.dart';

/// Lets the owner put a chat, file or task in a project, move it to another
/// one, or take it out. [kind] is the API collection: `conversations`, `files`
/// or `tasks`. Returns true when something changed.
Future<bool> showMoveToProjectSheet(
  BuildContext context, {
  required Dio http,
  required String kind,
  required String id,
  String? currentProjectId,
}) async {
  final List<Map<String, dynamic>> projects;
  try {
    final response = await http.get<dynamic>('/api/v1/projects');
    projects = jsonMaps(
      response.data,
    ).where((item) => jsonString(item, 'id') != null).toList();
  } catch (_) {
    if (context.mounted) {
      _snack(context, 'Jarvis could not load your projects.');
    }
    return false;
  }
  if (!context.mounted) return false;

  final choice = await showModalBottomSheet<_Choice>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) =>
        _MoveSheet(projects: projects, currentProjectId: currentProjectId),
  );
  if (choice == null || !context.mounted) return false;

  var target = choice.projectId;
  if (choice.create) {
    final created = await Navigator.of(context).push<Map<String, dynamic>>(
      MaterialPageRoute(builder: (_) => ProjectEditorScreen(http: http)),
    );
    target = jsonId(created);
    if (target == null || !context.mounted) return false;
  }
  if (target == currentProjectId) return false;

  try {
    await http.put<void>(
      '/api/v1/$kind/$id/project',
      data: {'projectId': target},
    );
    if (context.mounted) {
      final name = target == null
          ? null
          : asJsonString(
                  projects.firstWhere(
                    (item) => item['id'] == target,
                    orElse: () => const {},
                  )['name'],
                ) ??
                'the new project';
      _snack(
        context,
        name == null ? 'Removed from the project' : 'Moved to $name',
      );
    }
    return true;
  } catch (_) {
    if (context.mounted) _snack(context, 'Jarvis could not move it.');
    return false;
  }
}

void _snack(BuildContext context, String message) =>
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));

typedef _Choice = ({String? projectId, bool create});

class _MoveSheet extends StatelessWidget {
  const _MoveSheet({required this.projects, required this.currentProjectId});

  final List<Map<String, dynamic>> projects;
  final String? currentProjectId;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget check(bool selected) => selected
        ? Icon(PhosphorIconsFill.checkCircle, size: 20, color: colors.ink)
        : const SizedBox(width: 20);
    return SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * .75,
        ),
        child: ListView(
          shrinkWrap: true,
          padding: const EdgeInsets.fromLTRB(8, 0, 8, 16),
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 10),
              child: Text(
                'Move to project',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            for (final project in projects)
              ListTile(
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                ),
                leading: ProjectBadge(
                  color: asJsonString(project['color']),
                  size: 34,
                ),
                title: Text(
                  asJsonString(project['name']) ?? 'Untitled project',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                subtitle: Text(projectCountsLabel(project)),
                trailing: check(project['id'] == currentProjectId),
                onTap: () => Navigator.pop(context, (
                  projectId: jsonString(project, 'id'),
                  create: false,
                )),
              ),
            if (currentProjectId != null)
              ListTile(
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                ),
                leading: const IconBadge(
                  icon: PhosphorIconsRegular.minusCircle,
                  size: 34,
                ),
                title: const Text('Remove from project'),
                onTap: () =>
                    Navigator.pop(context, (projectId: null, create: false)),
              ),
            ListTile(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(JarvisRadii.md),
              ),
              leading: const IconBadge(
                icon: PhosphorIconsRegular.plus,
                size: 34,
              ),
              title: const Text('New project'),
              onTap: () =>
                  Navigator.pop(context, (projectId: null, create: true)),
            ),
          ],
        ),
      ),
    );
  }
}
