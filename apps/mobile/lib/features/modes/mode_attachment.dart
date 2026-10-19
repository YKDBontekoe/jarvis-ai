import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/phosphor_icons.dart';

/// What the current mode suggests: a profile and/or a home routine the owner
/// attached to it. It says plainly what is only a suggestion on screen and
/// what still needs the owner, and calls out a profile that would widen access.
class ModeSuggestionCard extends StatelessWidget {
  const ModeSuggestionCard({required this.data, super.key});

  final Map<String, dynamic> data;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final profile = jsonObject(data['profile']);
    final routine = jsonObject(data['routine']);
    final problem = asJsonString(data['problem']);
    if (profile == null && routine == null && problem == null) {
      return const SizedBox.shrink();
    }
    final details = jsonStrings(
      jsonObject(profile?['scopeChange'])?['details'],
    );
    final broadens = jsonStrings(profile?['broadens']);
    final needsYou = jsonStrings(data['needsYou']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: FeatureRow(
        key: const Key('mode-suggestion'),
        icon: PhosphorIconsRegular.sparkle,
        caption:
            'SUGGESTED FOR ${(asJsonString(data['label']) ?? '').toUpperCase()}',
        title: [
          if (profile != null) 'Profile: ${asJsonString(profile['name'])}',
          if (routine != null) 'Routine: ${asJsonString(routine['title'])}',
        ].join(' · ').ifEmpty(problem ?? ''),
        subtitle: [
          if (problem != null && profile == null) problem,
          ...details,
          ...jsonStrings(data['visualOnly']),
        ].join('\n'),
        extra: Padding(
          padding: const EdgeInsets.only(top: 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (broadens.isNotEmpty)
                Text(
                  'Wider access: ${broadens.join(' ')}',
                  key: const Key('mode-suggestion-broadens'),
                  style: TextStyle(color: colors.warning),
                ),
              for (final line in needsYou)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    line,
                    style: TextStyle(color: colors.muted, fontSize: 12.5),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

extension on String {
  String ifEmpty(String fallback) => isEmpty ? fallback : this;
}

/// Choose the profile and routine to suggest in one mode, or clear them.
class ModeAttachmentSheet extends StatefulWidget {
  const ModeAttachmentSheet({
    required this.label,
    required this.profiles,
    required this.routines,
    this.profileId,
    this.routineId,
    super.key,
  });

  final String label;
  final List<Map<String, dynamic>> profiles;
  final List<Map<String, dynamic>> routines;
  final String? profileId;
  final String? routineId;

  @override
  State<ModeAttachmentSheet> createState() => _ModeAttachmentSheetState();
}

class _ModeAttachmentSheetState extends State<ModeAttachmentSheet> {
  String? _profile;
  String? _routine;

  @override
  void initState() {
    super.initState();
    _profile = widget.profileId;
    _routine = widget.routineId;
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
            'Suggest in ${widget.label}',
            style: JarvisType.displayOf(context).copyWith(fontSize: 26),
          ),
          const SizedBox(height: 12),
          const Text('Profile', style: TextStyle(fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          Wrap(
            spacing: 8,
            children: [
              for (final profile in widget.profiles)
                ChoiceChip(
                  key: Key('attach-profile-${profile['id']}'),
                  label: Text(asJsonString(profile['name']) ?? ''),
                  selected: _profile == asJsonString(profile['id']),
                  onSelected: (selected) => setState(
                    () => _profile = selected
                        ? asJsonString(profile['id'])
                        : null,
                  ),
                ),
            ],
          ),
          const SizedBox(height: 12),
          const Text(
            'Home routine',
            style: TextStyle(fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 6),
          Wrap(
            spacing: 8,
            children: [
              for (final routine in widget.routines)
                ChoiceChip(
                  key: Key('attach-routine-${routine['id']}'),
                  label: Text(asJsonString(routine['title']) ?? ''),
                  selected: _routine == asJsonString(routine['id']),
                  onSelected: (selected) => setState(
                    () => _routine = selected
                        ? asJsonString(routine['id'])
                        : null,
                  ),
                ),
            ],
          ),
          const Padding(
            padding: EdgeInsets.only(top: 12),
            child: Text(
              'These are only suggestions. Nothing switches or runs by itself.',
            ),
          ),
          const SizedBox(height: 12),
          PillAction(
            key: const Key('attach-save'),
            primary: true,
            label: _profile == null && _routine == null ? 'Clear' : 'Save',
            onPressed: () => Navigator.pop<Map<String, Object?>>(context, {
              'profileId': _profile,
              'routineId': _routine,
            }),
          ),
        ],
      ),
    ),
  );
}
