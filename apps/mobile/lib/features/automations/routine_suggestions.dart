import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// "Suggested for you": routines Jarvis noticed, each with the automation it would create. Creating one makes a
/// draft the owner still has to switch on, so nothing starts without a second look.
class RoutineSuggestionsSection extends StatelessWidget {
  const RoutineSuggestionsSection({
    required this.suggestions,
    required this.busy,
    required this.onCreate,
    required this.onDismiss,
    super.key,
  });

  final List<Map<String, dynamic>> suggestions;
  final Set<String> busy;
  final void Function(String id) onCreate;
  final void Function(String id) onDismiss;

  @override
  Widget build(BuildContext context) {
    if (suggestions.isEmpty) return const SizedBox.shrink();
    return Column(
      key: const Key('routine-suggestions'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SectionHeader('Suggested for you'),
        for (final suggestion in suggestions)
          _SuggestionCard(
            suggestion: suggestion,
            busy: busy.contains(jsonId(suggestion) ?? ''),
            onCreate: onCreate,
            onDismiss: onDismiss,
          ),
        const SizedBox(height: 8),
      ],
    );
  }
}

class _SuggestionCard extends StatelessWidget {
  const _SuggestionCard({
    required this.suggestion,
    required this.busy,
    required this.onCreate,
    required this.onDismiss,
  });

  final Map<String, dynamic> suggestion;
  final bool busy;
  final void Function(String id) onCreate;
  final void Function(String id) onDismiss;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final id = jsonId(suggestion) ?? '';
    final title = asJsonString(suggestion['title']) ?? 'Routine';
    final evidence = asJsonString(suggestion['evidence']) ?? '';
    final simulation = jsonObject(suggestion['simulation']) ?? const {};
    final steps = jsonMaps(simulation['steps']);
    final needsApproval = asJsonInt(simulation['approvalsNeeded']) > 0;
    final then = steps
        .map((step) => asJsonString(step['label']) ?? '')
        .where((label) => label.isNotEmpty)
        .join(', then ');

    return SurfaceCard(
      key: Key('routine-suggestion-$id'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.lightbulb),
              const SizedBox(width: 14),
              Expanded(
                child: Text(
                  title,
                  style: text.titleSmall?.copyWith(fontSize: 15),
                ),
              ),
            ],
          ),
          if (evidence.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(
              evidence,
              style: text.bodySmall?.copyWith(color: colors.inkSoft),
            ),
          ],
          const SizedBox(height: 8),
          if ((asJsonString(simulation['trigger']) ?? '').isNotEmpty)
            Text(
              'When: ${asJsonString(simulation['trigger'])}',
              style: text.bodySmall?.copyWith(color: colors.muted),
            ),
          if (then.isNotEmpty)
            Text(
              'Then: $then',
              style: text.bodySmall?.copyWith(color: colors.muted),
            ),
          if (needsApproval)
            Text(
              'Asks for your approval before sending or starting anything.',
              style: text.bodySmall?.copyWith(color: colors.muted),
            ),
          const SizedBox(height: 8),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              TextButton(
                key: Key('routine-dismiss-$id'),
                onPressed: busy || id.isEmpty ? null : () => onDismiss(id),
                child: const Text('Not for me'),
              ),
              const SizedBox(width: 8),
              FilledButton.tonal(
                key: Key('routine-create-$id'),
                onPressed: busy || id.isEmpty ? null : () => onCreate(id),
                style: FilledButton.styleFrom(minimumSize: const Size(0, 36)),
                child: const Text('Create draft'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
