import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import 'habit_models.dart';

/// Creates a habit, or edits [habit]. Pops with the saved habit.
Future<HabitView?> showHabitEditor(
  BuildContext context, {
  required Dio http,
  HabitView? habit,
  ({String name, String icon, String cadence, int target})? template,
}) => showModalBottomSheet<HabitView>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  useSafeArea: true,
  builder: (_) => _HabitEditor(http: http, habit: habit, template: template),
);

class _HabitEditor extends StatefulWidget {
  const _HabitEditor({required this.http, this.habit, this.template});

  final Dio http;
  final HabitView? habit;
  final ({String name, String icon, String cadence, int target})? template;

  @override
  State<_HabitEditor> createState() => _HabitEditorState();
}

class _HabitEditorState extends State<_HabitEditor> {
  late final TextEditingController _name;
  late String _icon;
  late String _cadence;
  late int _target;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final habit = widget.habit;
    final template = widget.template;
    _name = TextEditingController(text: habit?.name ?? template?.name ?? '');
    _icon = habit?.displayIcon ?? template?.icon ?? habitIconSuggestions.first;
    _cadence = habit?.cadence ?? template?.cadence ?? 'daily';
    _target = habit?.isWeekly == true
        ? habit!.targetPerWeek
        : (template?.cadence == 'weekly' ? template!.target : 3);
  }

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final name = _name.text.trim();
    if (name.isEmpty) {
      setState(() => _error = 'Give the habit a name.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    final body = <String, dynamic>{
      'name': name,
      'icon': _icon,
      'cadence': _cadence,
      'targetPerWeek': _cadence == 'weekly' ? _target : null,
    };
    try {
      final habit = widget.habit;
      final Response<dynamic> response;
      if (habit == null) {
        body['timeZoneId'] = await deviceTimeZoneLookup();
        response = await widget.http.post<dynamic>(
          '/api/v1/habits',
          data: body,
        );
      } else {
        response = await widget.http.put<dynamic>(
          '/api/v1/habits/${habit.id}',
          data: body,
        );
      }
      if (!mounted) return;
      Navigator.of(
        context,
      ).pop(HabitView.fromJson(jsonObject(response.data) ?? const {}));
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save the habit.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final editing = widget.habit != null;
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              editing ? 'Edit habit' : 'New habit',
              style: theme.textTheme.titleLarge,
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                Container(
                  width: 52,
                  height: 52,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: colors.accentSoft,
                    borderRadius: BorderRadius.circular(16),
                  ),
                  child: Text(_icon, style: const TextStyle(fontSize: 26)),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextField(
                    key: const Key('habit-name'),
                    controller: _name,
                    autofocus: !editing && widget.template == null,
                    maxLength: 60,
                    textCapitalization: TextCapitalization.sentences,
                    textInputAction: TextInputAction.done,
                    onSubmitted: (_) => unawaited(_save()),
                    decoration: const InputDecoration(
                      labelText: 'Name',
                      hintText: 'Read 20 pages',
                      counterText: '',
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 14),
            SizedBox(
              height: 44,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: habitIconSuggestions.length,
                separatorBuilder: (_, _) => const SizedBox(width: 6),
                itemBuilder: (context, index) {
                  final icon = habitIconSuggestions[index];
                  final selected = icon == _icon;
                  return Semantics(
                    button: true,
                    selected: selected,
                    label: 'Icon $icon',
                    child: InkWell(
                      borderRadius: BorderRadius.circular(12),
                      onTap: () => setState(() => _icon = icon),
                      child: AnimatedContainer(
                        duration: const Duration(milliseconds: 160),
                        width: 44,
                        alignment: Alignment.center,
                        decoration: BoxDecoration(
                          color: selected
                              ? colors.accentSoft
                              : colors.surfaceMuted,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(
                            color: selected
                                ? colors.accent
                                : Colors.transparent,
                            width: 1.5,
                          ),
                        ),
                        child: Text(icon, style: const TextStyle(fontSize: 20)),
                      ),
                    ),
                  );
                },
              ),
            ),
            const SizedBox(height: 20),
            Text('How often', style: theme.textTheme.titleSmall),
            const SizedBox(height: 8),
            SegmentedButton<String>(
              segments: const [
                ButtonSegment(value: 'daily', label: Text('Every day')),
                ButtonSegment(value: 'weekly', label: Text('Times a week')),
              ],
              selected: {_cadence},
              showSelectedIcon: false,
              onSelectionChanged: (value) =>
                  setState(() => _cadence = value.first),
            ),
            AnimatedSize(
              duration: const Duration(milliseconds: 200),
              curve: Curves.easeOutCubic,
              child: _cadence != 'weekly'
                  ? const SizedBox(width: double.infinity)
                  : Padding(
                      padding: const EdgeInsets.only(top: 14),
                      child: Row(
                        children: [
                          Expanded(
                            child: Text(
                              '$_target× a week',
                              style: theme.textTheme.titleMedium,
                            ),
                          ),
                          IconButton.filledTonal(
                            tooltip: 'Fewer',
                            onPressed: _target > 1
                                ? () => setState(() => _target--)
                                : null,
                            icon: const Icon(Icons.remove, size: 18),
                          ),
                          const SizedBox(width: 6),
                          IconButton.filledTonal(
                            tooltip: 'More',
                            onPressed: _target < 7
                                ? () => setState(() => _target++)
                                : null,
                            icon: const Icon(Icons.add, size: 18),
                          ),
                        ],
                      ),
                    ),
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(_error!, style: TextStyle(color: colors.danger)),
            ],
            const SizedBox(height: 22),
            FilledButton(
              key: const Key('habit-save'),
              onPressed: _saving ? null : () => unawaited(_save()),
              style: FilledButton.styleFrom(minimumSize: const Size(0, 48)),
              child: Text(
                _saving
                    ? 'Saving…'
                    : editing
                    ? 'Save'
                    : 'Start habit',
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The evening question: on or off, and when. Pops with the saved settings.
Future<HabitSettingsView?> showHabitSettings(
  BuildContext context, {
  required Dio http,
  required HabitSettingsView settings,
}) => showModalBottomSheet<HabitSettingsView>(
  context: context,
  showDragHandle: true,
  useSafeArea: true,
  builder: (_) => _HabitSettingsSheet(http: http, settings: settings),
);

class _HabitSettingsSheet extends StatefulWidget {
  const _HabitSettingsSheet({required this.http, required this.settings});

  final Dio http;
  final HabitSettingsView settings;

  @override
  State<_HabitSettingsSheet> createState() => _HabitSettingsSheetState();
}

class _HabitSettingsSheetState extends State<_HabitSettingsSheet> {
  late bool _enabled = widget.settings.eveningCheckIn;
  late TimeOfDay _time = _parse(widget.settings.checkInTime);
  bool _saving = false;
  String? _error;

  static TimeOfDay _parse(String value) {
    final parts = value.split(':');
    return TimeOfDay(
      hour: int.tryParse(parts.first) ?? 20,
      minute: parts.length > 1 ? int.tryParse(parts[1]) ?? 30 : 30,
    );
  }

  String get _timeText =>
      '${_time.hour.toString().padLeft(2, '0')}:'
      '${_time.minute.toString().padLeft(2, '0')}';

  Future<void> _pickTime() async {
    final picked = await showTimePicker(context: context, initialTime: _time);
    if (picked != null && mounted) setState(() => _time = picked);
  }

  Future<void> _save() async {
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final zone = await deviceTimeZoneLookup() ?? widget.settings.timeZoneId;
      final response = await widget.http.put<dynamic>(
        '/api/v1/habits/settings',
        data: HabitSettingsView(
          eveningCheckIn: _enabled,
          checkInTime: _timeText,
          timeZoneId: zone,
        ).toJson(),
      );
      if (!mounted) return;
      Navigator.of(
        context,
      ).pop(HabitSettingsView.fromJson(jsonObject(response.data)));
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save the check-in.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Evening check-in',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 6),
          Text(
            'Jarvis asks how your habits went when some are still open. '
            'Reply in chat ("I went for a run") or tap to check them off.',
            style: TextStyle(color: colors.muted, height: 1.4),
          ),
          const SizedBox(height: 12),
          SwitchListTile.adaptive(
            key: const Key('habit-checkin-switch'),
            contentPadding: EdgeInsets.zero,
            title: const Text('Ask me in the evening'),
            value: _enabled,
            onChanged: (value) => setState(() => _enabled = value),
          ),
          ListTile(
            contentPadding: EdgeInsets.zero,
            enabled: _enabled,
            title: const Text('Time'),
            trailing: Text(
              _timeText,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            onTap: _enabled ? () => unawaited(_pickTime()) : null,
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(_error!, style: TextStyle(color: colors.danger)),
            ),
          const SizedBox(height: 14),
          FilledButton(
            onPressed: _saving ? null : () => unawaited(_save()),
            style: FilledButton.styleFrom(minimumSize: const Size(0, 48)),
            child: Text(_saving ? 'Saving…' : 'Save'),
          ),
        ],
      ),
    );
  }
}
