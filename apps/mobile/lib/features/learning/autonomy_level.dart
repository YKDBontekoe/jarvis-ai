import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// The autonomy levels, from most careful to most independent.
const autonomyLevels = [
  ('ask_everything', 'Ask'),
  ('standard', 'Standard'),
  ('full', 'Full'),
  ('autonomous', 'Autonomous'),
];

String autonomyLevelExplanation(String level) => switch (level) {
  'ask_everything' => 'Every gated action shows you a card first.',
  'standard' =>
    'Looking things up runs on its own; every change asks first.',
  'autonomous' =>
    'Changes to your own Jarvis data run on their own, also in chat. In the '
        'background Jarvis may also act outside Jarvis, but only in the '
        'categories you allow below. Deleting, private reads and anything new '
        'always ask.',
  _ =>
    'Looking things up runs on its own; background tasks may make '
        'reversible changes. Anything that sends, spends or deletes asks.',
};

/// How far Jarvis may go, with the opt-in autonomous level and the outside
/// actions it may take unattended. Changes go through [onUpdate], which saves
/// the autonomy settings.
class AutonomyLevelSection extends StatefulWidget {
  const AutonomyLevelSection({
    required this.http,
    required this.autonomy,
    required this.saving,
    required this.onUpdate,
    super.key,
  });

  final Dio http;
  final Map<String, dynamic> autonomy;
  final bool saving;
  final Future<void> Function(String key, Object value) onUpdate;

  @override
  State<AutonomyLevelSection> createState() => _AutonomyLevelSectionState();
}

class _AutonomyLevelSectionState extends State<AutonomyLevelSection> {
  List<(String, String)> _categories = const [];

  @override
  void initState() {
    super.initState();
    unawaited(_loadCategories());
  }

  Future<void> _loadCategories() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/settings/autonomy/outbound-categories',
      );
      if (!mounted) return;
      setState(
        () => _categories = [
          for (final item in jsonMaps(response.data))
            if (asJsonString(item['key']) case final key?)
              (key, asJsonString(item['label']) ?? key),
        ],
      );
    } on DioException {
      // An older server has no autonomous level; the switches stay hidden.
    }
  }

  Future<void> _chooseLevel(String level) async {
    if (level == 'autonomous') {
      final confirmed = await showJarvisConfirm(
        context,
        title: 'Let Jarvis act on its own?',
        message:
            'Jarvis will make changes to your own data without asking, and in '
            'the background it may act in the categories you allow, within a '
            'daily limit. Everything it does shows up in Activity. Deleting '
            'and private reads still ask.',
        confirmLabel: 'Go autonomous',
        icon: PhosphorIconsRegular.robot,
      );
      if (!confirmed || !mounted) return;
    }
    await widget.onUpdate('level', level);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final on = asJsonBool(widget.autonomy['enabled'], true);
    final level = asJsonString(widget.autonomy['level']) ?? 'full';
    final allowed = jsonStrings(widget.autonomy['autonomousOutboundCategories']);
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'How far Jarvis may go',
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: 10),
          SegmentedPills<String>(
            keyPrefix: 'autonomy-level',
            options: autonomyLevels,
            selected: level,
            onSelected: widget.saving || !on
                ? null
                : (value) {
                    if (value != level) unawaited(_chooseLevel(value));
                  },
          ),
          const SizedBox(height: 8),
          Text(
            autonomyLevelExplanation(level),
            style: TextStyle(fontSize: 12.5, color: colors.muted),
          ),
          if (level == 'autonomous' && _categories.isNotEmpty) ...[
            const SizedBox(height: 12),
            Text(
              'Allowed to do unattended',
              style: Theme.of(context).textTheme.titleSmall,
            ),
            for (final (key, label) in _categories)
              CheckboxListTile(
                key: Key('autonomy-outbound-$key'),
                contentPadding: EdgeInsets.zero,
                dense: true,
                title: Text(label),
                value: allowed.contains(key),
                onChanged: widget.saving || !on
                    ? null
                    : (checked) => unawaited(
                        widget.onUpdate('autonomousOutboundCategories', [
                          for (final item in allowed)
                            if (item != key) item,
                          if (checked == true) key,
                        ]),
                      ),
              ),
            Text(
              'At most ${asJsonInt(widget.autonomy['maxAutonomousOutboundPerDay'], 10)} '
              'outside actions a day.',
              style: TextStyle(fontSize: 12.5, color: colors.muted),
            ),
          ],
        ],
      ),
    );
  }
}
