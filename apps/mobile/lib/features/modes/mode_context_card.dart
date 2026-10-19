import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/phosphor_icons.dart';

/// What the current mode means right now: how long, what is quiet, what still
/// gets through, how to leave or correct a wrong guess, and one focus session
/// with a chosen next action and a reflection at the end. A mode never grants
/// a permission; the card says so.
class ModeContextCard extends StatefulWidget {
  const ModeContextCard({
    required this.http,
    required this.context,
    required this.onChanged,
    required this.onLeave,
    super.key,
  });

  final Dio http;
  final Map<String, dynamic> context;

  /// Called after the server changed the mode so the screen reloads.
  final Future<void> Function() onChanged;

  /// Switches a mode the owner chose back to automatic.
  final Future<void> Function() onLeave;

  @override
  State<ModeContextCard> createState() => _ModeContextCardState();
}

class _ModeContextCardState extends State<ModeContextCard> {
  bool _busy = false;

  Map<String, dynamic> get _card => widget.context;

  Future<void> _post(String path, [Map<String, Object?>? body]) async {
    setState(() => _busy = true);
    try {
      await widget.http.post<dynamic>(path, data: body ?? const {});
      await widget.onChanged();
    } on DioException catch (error) {
      if (mounted) {
        ScaffoldMessenger.maybeOf(context)
          ?..hideCurrentSnackBar()
          ..showSnackBar(
            SnackBar(
              content: Text(
                firstProblemMessage(error.response?.data) ??
                    'That did not work.',
              ),
            ),
          );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _startFocus() async {
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _FocusSheet(),
    );
    if (draft != null) await _post('/api/v1/modes/focus', draft);
  }

  Future<void> _endFocus() async {
    final note = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _ReflectionSheet(),
    );
    if (note != null) {
      await _post('/api/v1/modes/focus/end', {
        if (note.isNotEmpty) 'reflection': note,
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final running = asJsonBool(_card['focusRunning']);
    final due = asJsonBool(_card['reflectionDue']);
    final focus = jsonObject(_card['focus']);
    final intention = asJsonString(focus?['intention']) ?? '';
    final left = asJsonInt(_card['minutesLeft']);
    final reaches = jsonStrings(_card['stillReaches']);
    final canCorrect = asJsonBool(_card['canCorrect']);
    final exit = asJsonString(_card['exitLabel']);
    final manual = asJsonString(_card['source']) == 'manual';
    final children = <Widget>[
      if (running)
        HeroCard(
          key: const Key('focus-hero'),
          label: 'FOCUS · $left MIN LEFT',
          title: intention.isEmpty ? 'Focus is on' : intention,
          subtitle: [
            if (intention.isEmpty) 'No next action chosen.',
            if (reaches.isNotEmpty) 'Still reaches you: ${reaches.join(', ')}.',
          ].join(' '),
          footer: PillAction(
            key: const Key('focus-end'),
            onDark: true,
            icon: PhosphorIconsRegular.checkCircle,
            label: 'End focus',
            onPressed: _busy ? null : () => unawaited(_endFocus()),
          ),
        ),
      if (running) const SizedBox(height: 12),
      if (due)
        FeatureRow(
          key: const Key('focus-reflection-due'),
          index: 0,
          icon: PhosphorIconsRegular.notePencil,
          caption: 'FOCUS ENDED',
          title: intention.isEmpty ? 'Your focus session' : intention,
          subtitle: 'How did it go? One line is enough, or skip it.',
          actions: [
            PillAction(
              key: const Key('focus-reflect'),
              compact: true,
              primary: true,
              label: 'Add a note',
              onPressed: _busy ? null : () => unawaited(_endFocus()),
            ),
          ],
        ),
      FeatureRow(
        key: const Key('mode-context'),
        index: 1,
        icon: PhosphorIconsRegular.bellSlash,
        caption: 'WHAT THIS CHANGES',
        title: asJsonString(_card['muted']) ?? '',
        subtitle: reaches.isEmpty
            ? 'Still reaches you: nothing by push.'
            : 'Still reaches you: ${reaches.join(', ')}.',
        actions: [
          if (!running && !manual)
            PillAction(
              key: const Key('focus-start'),
              compact: true,
              primary: true,
              icon: PhosphorIconsRegular.target,
              label: 'Start focus',
              onPressed: _busy ? null : () => unawaited(_startFocus()),
            ),
          if (canCorrect)
            PillAction(
              key: const Key('mode-correct'),
              compact: true,
              label: exit ?? 'Not right now',
              onPressed: _busy
                  ? null
                  : () => unawaited(_post('/api/v1/modes/correct')),
            )
          else if (manual && exit != null)
            PillAction(
              key: const Key('mode-exit'),
              compact: true,
              label: exit,
              onPressed: _busy ? null : () => unawaited(widget.onLeave()),
            ),
        ],
      ),
      Padding(
        padding: const EdgeInsets.fromLTRB(6, 2, 6, 16),
        child: Text(
          asJsonString(_card['effects']) ?? '',
          key: const Key('mode-effects'),
          style: TextStyle(color: colors.muted, fontSize: 12.5),
        ),
      ),
    ];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: children,
    );
  }
}

class _FocusSheet extends StatefulWidget {
  const _FocusSheet();

  @override
  State<_FocusSheet> createState() => _FocusSheetState();
}

class _FocusSheetState extends State<_FocusSheet> {
  final _intention = TextEditingController();
  int _minutes = 45;

  @override
  void dispose() {
    _intention.dispose();
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
            'Start focus',
            style: JarvisType.displayOf(context).copyWith(fontSize: 26),
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('focus-intention'),
            controller: _intention,
            maxLength: 200,
            decoration: const InputDecoration(
              labelText: 'What will you work on?',
              hintText: 'Finish the report outline',
            ),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            children: [
              for (final minutes in const [25, 45, 90])
                ChoiceChip(
                  key: Key('focus-minutes-$minutes'),
                  label: Text('$minutes min'),
                  selected: _minutes == minutes,
                  onSelected: (_) => setState(() => _minutes = minutes),
                ),
            ],
          ),
          const Padding(
            padding: EdgeInsets.only(top: 12),
            child: Text(
              'Only reminders and approvals will buzz your phone. You can end it any time.',
            ),
          ),
          const SizedBox(height: 14),
          PillAction(
            key: const Key('focus-go'),
            primary: true,
            icon: PhosphorIconsRegular.target,
            label: 'Start',
            onPressed: () => Navigator.pop<Map<String, Object?>>(context, {
              'minutes': _minutes,
              if (_intention.text.trim().isNotEmpty)
                'intention': _intention.text.trim(),
            }),
          ),
        ],
      ),
    ),
  );
}

class _ReflectionSheet extends StatefulWidget {
  const _ReflectionSheet();

  @override
  State<_ReflectionSheet> createState() => _ReflectionSheetState();
}

class _ReflectionSheetState extends State<_ReflectionSheet> {
  final _note = TextEditingController();

  @override
  void dispose() {
    _note.dispose();
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
            'How did it go?',
            style: JarvisType.displayOf(context).copyWith(fontSize: 26),
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('focus-note'),
            controller: _note,
            maxLength: 500,
            maxLines: 3,
            decoration: const InputDecoration(
              hintText: 'What got done, what got in the way',
            ),
          ),
          const SizedBox(height: 8),
          PillAction(
            key: const Key('focus-save'),
            primary: true,
            label: 'Save and end',
            onPressed: () => Navigator.pop<String>(context, _note.text.trim()),
          ),
          const SizedBox(height: 8),
          PillAction(
            key: const Key('focus-skip'),
            label: 'End without a note',
            onPressed: () => Navigator.pop<String>(context, ''),
          ),
        ],
      ),
    ),
  );
}
