import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/phosphor_icons.dart';

/// The things the owner is looking at, innermost last (`project:{id}`,
/// `meeting:{id}`, …). Screens add themselves while they are open, so the
/// command palette can offer what makes sense for the thing in front of them.
final List<String> _paletteContexts = [];

String? get currentPaletteContext =>
    _paletteContexts.isEmpty ? null : _paletteContexts.last;

/// Registers a screen's subject with the palette for as long as it is open.
mixin PaletteContextMixin<T extends StatefulWidget> on State<T> {
  /// For example `project:{id}`.
  String get paletteContextValue;

  @override
  void initState() {
    super.initState();
    _paletteContexts.add(paletteContextValue);
  }

  @override
  void dispose() {
    _paletteContexts.remove(paletteContextValue);
    super.dispose();
  }
}

/// An action on the thing being looked at. [run] does the work and returns the
/// destination that shows the result, so the owner lands on something real; if
/// it fails, nothing is claimed and the palette shows why.
class ContextCommand {
  const ContextCommand({
    required this.label,
    required this.icon,
    required this.keywords,
    required this.run,
  });

  final String label;
  final IconData icon;
  final List<String> keywords;
  final Future<String> Function(Dio http) run;
}

/// What the palette heading says for a context, e.g. "this project".
String contextNoun(String context) => switch (context.split(':').first) {
  'project' => 'project',
  'meeting' => 'meeting',
  'goal' => 'goal',
  'document' => 'draft',
  'subscription' => 'subscription',
  _ => 'page',
};

/// Commands for [context] (`kind:id`), best first; empty when there are none.
List<ContextCommand> contextCommandsFor(String? context) {
  if (context == null || !context.contains(':')) return const [];
  final kind = context.split(':').first;
  final id = context.substring(kind.length + 1);
  if (id.isEmpty) return const [];
  switch (kind) {
    case 'project':
      return [
        ContextCommand(
          label: 'Draft a project update',
          icon: PhosphorIconsRegular.notePencil,
          keywords: const ['update', 'draft', 'report', 'status', 'rapport', 'voortgang'],
          run: (http) async {
            final response = await http.post<dynamic>(
              '/api/v1/documents',
              data: {'kind': 'project_update', 'projectId': id},
            );
            final draft = jsonObject(jsonObject(response.data)?['draft']);
            final created = draft == null ? null : jsonString(draft, 'id');
            if (created == null) throw StateError('The draft was not created.');
            return 'document:$created';
          },
        ),
      ];
    case 'meeting':
      return [
        ContextCommand(
          label: 'Prepare this meeting',
          icon: PhosphorIconsRegular.calendarCheck,
          keywords: const ['prepare', 'prep', 'agenda', 'voorbereiden', 'vergadering'],
          run: (http) async {
            await http.post<dynamic>('/api/v1/meetings/$id/prep');
            return 'meeting:$id';
          },
        ),
      ];
    case 'goal':
      return [
        ContextCommand(
          label: "Plan this week's steps",
          icon: PhosphorIconsRegular.target,
          keywords: const ['plan', 'week', 'steps', 'next', 'plannen', 'stappen'],
          run: (http) async {
            await http.post<dynamic>('/api/v1/goals/$id/plan');
            return 'goal:$id';
          },
        ),
      ];
    case 'document':
      return [
        ContextCommand(
          label: 'Save this draft to the Library',
          icon: PhosphorIconsRegular.notePencil,
          keywords: const ['save', 'library', 'opslaan', 'bibliotheek'],
          run: (http) async {
            await http.post<dynamic>('/api/v1/documents/$id/save');
            return 'document:$id';
          },
        ),
      ];
    case 'subscription':
      return [
        ContextCommand(
          label: 'Prepare a price pack',
          icon: PhosphorIconsRegular.sliders,
          keywords: const ['pack', 'price', 'negotiate', 'prijs', 'onderhandelen'],
          run: (http) async {
            await http.post<dynamic>(
              '/api/v1/finance/subscriptions/$id/review/pack',
              data: {'goal': 'lower_price'},
            );
            return 'subscription:$id';
          },
        ),
      ];
  }
  return const [];
}

/// Context commands that match what was typed (all of them for an empty query).
List<ContextCommand> matchContextCommands(String? context, String query) {
  final text = query.trim().toLowerCase();
  final all = contextCommandsFor(context);
  if (text.isEmpty) return all;
  if (text.length < 2) return const [];
  return all
      .where(
        (command) =>
            command.label.toLowerCase().contains(text) ||
            command.keywords.any(
              (keyword) => keyword.startsWith(text) || text.startsWith(keyword),
            ),
      )
      .toList();
}
