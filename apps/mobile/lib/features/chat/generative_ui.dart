import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'chat_entries.dart';

/// Native renderer for A2UI-style surfaces Jarvis emits with RenderUi.
class UiSurfaceCard extends StatefulWidget {
  const UiSurfaceCard({
    required this.surface,
    this.onAction,
    super.key,
  });

  final UiSurfaceEntry surface;
  final Future<void> Function(String actionId, Map<String, String> values)?
  onAction;

  @override
  State<UiSurfaceCard> createState() => _UiSurfaceCardState();
}

class _UiSurfaceCardState extends State<UiSurfaceCard> {
  final _values = <String, String>{};
  String? _busyAction;

  Map<String, dynamic> get _schema => widget.surface.schema;

  @override
  Widget build(BuildContext context) {
    final title = asString(_schema['title']) ?? widget.surface.title;
    final body = asString(_schema['body']);
    final items = asMaps(_schema['items']);
    final fields = asMaps(_schema['fields']);
    final actions = asMaps(_schema['actions']);
    final completed = widget.surface.status != 'open';
    return Padding(
      padding: const EdgeInsets.only(bottom: 18),
      child: SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                IconBadge(
                  icon: PhosphorIconsRegular.appWindow,
                  size: 34,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    title,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
              ],
            ),
            if (body != null) ...[
              const SizedBox(height: 8),
              Text(body),
            ],
            if (items.isNotEmpty) ...[
              const SizedBox(height: 10),
              for (final item in items)
                ListTile(
                  dense: true,
                  contentPadding: EdgeInsets.zero,
                  title: Text(asString(item['title']) ?? ''),
                  subtitle: asString(item['subtitle']) == null
                      ? null
                      : Text(asString(item['subtitle'])!),
                ),
            ],
            if (fields.isNotEmpty) ...[
              const SizedBox(height: 8),
              for (final field in fields) _field(field, enabled: !completed),
            ],
            if (actions.isNotEmpty) ...[
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final action in actions)
                    _actionButton(action, completed: completed),
                ],
              ),
            ],
            if (completed)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  'Submitted',
                  style: Theme.of(
                    context,
                  ).textTheme.labelSmall?.copyWith(color: JarvisColors.muted),
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _field(Map<String, dynamic> field, {required bool enabled}) {
    final id = asString(field['id']) ?? '';
    final label = asString(field['label']) ?? id;
    final type = asString(field['type']) ?? 'text';
    if (type == 'toggle') {
      return SwitchListTile(
        contentPadding: EdgeInsets.zero,
        title: Text(label),
        value: _values[id] == 'true',
        onChanged: enabled
            ? (value) => setState(() => _values[id] = value.toString())
            : null,
      );
    }
    if (type == 'choice') {
      final options = [
        for (final option in field['options'] is List ? field['options'] as List : const [])
          if (option is String && option.isNotEmpty) option,
      ];
      final selected = options.contains(_values[id]) ? _values[id] : null;
      return Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: DropdownButtonFormField<String>(
          initialValue: selected,
          decoration: InputDecoration(labelText: label),
          items: [
            for (final option in options)
              DropdownMenuItem(value: option, child: Text(option)),
          ],
          onChanged: enabled
              ? (value) {
                  if (value != null) setState(() => _values[id] = value);
                }
              : null,
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: TextField(
        enabled: enabled,
        keyboardType: type == 'number'
            ? const TextInputType.numberWithOptions(decimal: true)
            : TextInputType.text,
        decoration: InputDecoration(
          labelText: label,
          hintText: asString(field['placeholder']),
        ),
        onChanged: (value) => _values[id] = value,
      ),
    );
  }

  Widget _actionButton(Map<String, dynamic> action, {required bool completed}) {
    final id = asString(action['id']) ?? '';
    final label = asString(action['label']) ?? id;
    final style = asString(action['style']) ?? 'secondary';
    final busy = _busyAction == id;
    final onPressed = completed || widget.onAction == null
        ? null
        : () async {
            setState(() => _busyAction = id);
            await widget.onAction!(id, Map<String, String>.from(_values));
            if (mounted) setState(() => _busyAction = null);
          };
    if (style == 'primary') {
      return FilledButton(
        onPressed: busy ? null : onPressed,
        child: Text(label),
      );
    }
    if (style == 'danger') {
      return FilledButton(
        onPressed: busy ? null : onPressed,
        style: FilledButton.styleFrom(backgroundColor: JarvisColors.danger),
        child: Text(label),
      );
    }
    return OutlinedButton(onPressed: busy ? null : onPressed, child: Text(label));
  }
}

String? asString(dynamic value) => value is String && value.isNotEmpty ? value : null;

List<Map<String, dynamic>> asMaps(dynamic value) {
  if (value is! List) return const [];
  return [
    for (final item in value)
      if (item is Map) Map<String, dynamic>.from(item),
  ];
}

class BrowserTimelineView extends StatelessWidget {
  const BrowserTimelineView({required this.session, super.key});

  final BrowserSessionEntry session;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const IconBadge(icon: PhosphorIconsRegular.browser, size: 34),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    session.goal,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            for (final step in session.steps)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  children: [
                    Icon(
                      step.success
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.warningCircle,
                      size: 14,
                      color: step.success
                          ? JarvisColors.success
                          : JarvisColors.danger,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        step.summary,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}
