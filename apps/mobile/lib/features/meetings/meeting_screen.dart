import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../search/context_commands.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../entities/entity_ref.dart';
import '../entities/entity_screen.dart';
import 'meetings_screen.dart' show meetingDay, meetingTemplates;

String _dueLabel(DateTime date) {
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return '${date.day} ${months[date.month - 1]} ${date.year}';
}

String _iso(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

/// One meeting: the prep pack from your own records, an agenda you own, a debrief that proposes decisions and promises
/// for you to review, and a follow-up draft. Nothing is accepted, created or sent until you say so.
class MeetingScreen extends StatefulWidget {
  const MeetingScreen({required this.http, required this.meetingId, super.key});

  final Dio http;
  final String meetingId;

  @override
  State<MeetingScreen> createState() => _MeetingScreenState();
}

class _MeetingScreenState extends State<MeetingScreen> with PaletteContextMixin<MeetingScreen> {
  @override
  String get paletteContextValue => 'meeting:${widget.meetingId}';

  Map<String, dynamic>? _meeting;
  bool _loading = true;
  bool _missing = false;
  bool _busy = false;
  bool _editingAgenda = false;
  String? _error;
  final _debrief = TextEditingController();
  final _notes = TextEditingController();
  final List<TextEditingController> _agenda = [];
  final List<bool> _shared = [];

  String get _base => '/api/v1/meetings/${widget.meetingId}';
  Map<String, dynamic> get _document =>
      jsonObject(_meeting?['document']) ?? const {};
  List<Map<String, dynamic>> get _candidates =>
      jsonMaps(_document['candidates']);

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _debrief.dispose();
    _notes.dispose();
    for (final controller in _agenda) {
      controller.dispose();
    }
    super.dispose();
  }

  void _adopt(Map<String, dynamic>? meeting) {
    _meeting = meeting;
    final notes = asJsonString(_document['privateNotes']) ?? '';
    if (_notes.text != notes && !_busy) _notes.text = notes;
    if (_debrief.text.isEmpty) {
      _debrief.text = asJsonString(_document['debriefText']) ?? '';
    }
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(_base);
      if (!mounted) return;
      setState(() {
        _adopt(jsonObject(response.data));
        _loading = false;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _missing = error.response?.statusCode == 404;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this meeting.';
      });
    }
  }

  void _say(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  /// Runs one request and shows the meeting the server sends back.
  Future<bool> _call(
    Future<Response<dynamic>> Function() request,
    String failure,
  ) async {
    if (_busy) return false;
    setState(() => _busy = true);
    try {
      final response = await request();
      if (mounted) setState(() => _adopt(jsonObject(response.data)));
      return true;
    } on DioException catch (error) {
      if (mounted) _say(firstProblemMessage(error.response?.data) ?? failure);
      return false;
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _prep() async {
    final ok = await _call(
      () => widget.http.post<dynamic>('$_base/prep'),
      'Could not build the prep pack.',
    );
    if (ok && mounted) _say('Prep pack updated from your records.');
  }

  void _startEditingAgenda() {
    for (final controller in _agenda) {
      controller.dispose();
    }
    _agenda.clear();
    _shared.clear();
    for (final item in jsonMaps(_document['agenda'])) {
      _agenda.add(
        TextEditingController(text: asJsonString(item['text']) ?? ''),
      );
      _shared.add(item['shared'] != false);
    }
    setState(() => _editingAgenda = true);
  }

  Future<void> _saveAgenda() async {
    final ok = await _call(
      () => widget.http.put<dynamic>(
        _base,
        data: {
          'agenda': [
            for (var i = 0; i < _agenda.length; i++)
              {
                'id': 'a${i + 1}',
                'text': _agenda[i].text,
                'shared': _shared[i],
              },
          ],
        },
      ),
      'Could not save the agenda.',
    );
    if (ok && mounted) setState(() => _editingAgenda = false);
  }

  Future<void> _saveNotes() => _call(
    () => widget.http.put<dynamic>(_base, data: {'privateNotes': _notes.text}),
    'Could not save your notes.',
  );

  Future<void> _readDebrief() async {
    if (_debrief.text.trim().isEmpty) return;
    final ok = await _call(
      () => widget.http.post<dynamic>(
        '$_base/debrief',
        data: {'text': _debrief.text},
      ),
      'Could not read that.',
    );
    if (ok && mounted && _candidates.isEmpty) {
      _say('Nothing that looks like a decision, a promise or a question.');
    }
  }

  Future<void> _accept(Map<String, dynamic> candidate) async {
    final choice = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _AcceptSheet(candidate: candidate),
    );
    if (choice == null || !mounted) return;
    final ok = await _call(
      () => widget.http.post<dynamic>(
        '$_base/candidates/${candidate['id']}/accept',
        data: choice,
      ),
      'Could not add that.',
    );
    if (ok && mounted) {
      _say(
        candidate['kind'] == 'decision'
            ? 'Logged as a decision.'
            : 'Added to your promises.',
      );
    }
  }

  Future<void> _dismiss(Map<String, dynamic> candidate) => _call(
    () => widget.http.post<dynamic>(
      '$_base/candidates/${candidate['id']}/dismiss',
    ),
    'Could not dismiss that.',
  );

  Future<void> _followUp() async {
    try {
      final response = await widget.http.post<dynamic>('$_base/followup');
      final draft = jsonObject(response.data);
      if (draft == null || !mounted) return;
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (_) => _FollowUpSheet(draft: draft),
      );
    } on DioException catch (error) {
      if (mounted) {
        _say(
          firstProblemMessage(error.response?.data) ?? 'Could not draft that.',
        );
      }
    }
  }

  Future<void> _openRef(String ref) async {
    if (ref.startsWith('meeting:')) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute(
          builder: (_) => MeetingScreen(
            http: widget.http,
            meetingId: ref.substring('meeting:'.length),
          ),
        ),
      );
      return;
    }
    final entity = EntityRef.tryParse(ref);
    if (entity != null) await openEntity(context, widget.http, entity);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: PageTitle(
        _meeting == null
            ? 'Meeting'
            : (asJsonString(_meeting!['title']) ?? 'Meeting'),
        overflow: TextOverflow.ellipsis,
      ),
    ),
    body: _body(),
  );

  Widget _body() {
    if (_loading && _meeting == null) return const SkeletonList();
    if (_missing) {
      return const EmptyState(
        icon: PhosphorIconsRegular.calendarCheck,
        title: 'This meeting is gone',
      );
    }
    if (_meeting == null) {
      return ErrorState(message: _error ?? 'Could not load.', onRetry: _load);
    }
    final colors = JarvisColors.of(context);
    final prep = jsonObject(_document['prep']);
    final items = jsonMaps(prep?['items']);
    final sections = <String, List<Map<String, dynamic>>>{};
    for (final item in items) {
      sections
          .putIfAbsent(asJsonString(item['section']) ?? 'Notes', () => [])
          .add(item);
    }
    final participants = [
      for (final person in jsonMaps(_document['participants']))
        if (asJsonString(person['name']) != null) asJsonString(person['name'])!,
    ];
    final agenda = jsonMaps(_document['agenda']);
    final pending = [
      for (final c in _candidates)
        if (c['state'] == 'pending') c,
    ];
    final settled = [
      for (final c in _candidates)
        if (c['state'] != 'pending') c,
    ];
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 48),
      children: [
        ContentWidth(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                [
                  if (_meeting!['startsAt'] != null)
                    meetingDay(_meeting!['startsAt']),
                  meetingTemplates[asJsonString(_document['template'])] ??
                      'General',
                  if (participants.isNotEmpty) participants.join(', '),
                ].join(' · '),
                style: TextStyle(fontSize: 14, color: colors.inkSoft),
              ),
              if (asJsonString(_document['purpose']) case final purpose?)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(
                    purpose,
                    style: TextStyle(fontSize: 15, color: colors.ink),
                  ),
                ),
              const SizedBox(height: 20),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      'Prep pack',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                  TextButton(
                    key: const Key('meeting-prep'),
                    onPressed: _busy ? null : _prep,
                    child: Text(prep == null ? 'Build it' : 'Refresh'),
                  ),
                ],
              ),
              if (prep == null)
                Text(
                  'Jarvis gathers what is open with the people involved, '
                  'decisions still waiting and what the project is for.',
                  style: TextStyle(color: colors.inkSoft),
                ),
              for (final entry in sections.entries) ...[
                Padding(
                  padding: const EdgeInsets.only(top: 10, bottom: 4),
                  child: Text(
                    entry.key,
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                      color: colors.muted,
                    ),
                  ),
                ),
                for (final item in entry.value)
                  _PrepRow(
                    item: item,
                    onOpen: item['ref'] is String
                        ? () => unawaited(_openRef(item['ref'] as String))
                        : null,
                  ),
              ],
              const SizedBox(height: 20),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      'Agenda',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                  if (_editingAgenda)
                    TextButton(
                      key: const Key('meeting-agenda-save'),
                      onPressed: _busy ? null : _saveAgenda,
                      child: const Text('Save'),
                    )
                  else
                    TextButton(
                      key: const Key('meeting-agenda-edit'),
                      onPressed: _startEditingAgenda,
                      child: const Text('Edit'),
                    ),
                ],
              ),
              if (_editingAgenda) ...[
                for (var i = 0; i < _agenda.length; i++)
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          key: Key('meeting-agenda-line-$i'),
                          controller: _agenda[i],
                          decoration: const InputDecoration(
                            hintText: 'Agenda point',
                          ),
                        ),
                      ),
                      IconButton(
                        key: Key('meeting-agenda-share-$i'),
                        tooltip: _shared[i]
                            ? 'Shared in the follow-up. Tap to keep private'
                            : 'Private. Tap to share in the follow-up',
                        onPressed: () =>
                            setState(() => _shared[i] = !_shared[i]),
                        icon: Icon(
                          _shared[i]
                              ? PhosphorIconsRegular.users
                              : PhosphorIconsRegular.lockSimple,
                          size: 20,
                        ),
                      ),
                      IconButton(
                        tooltip: 'Remove this point',
                        onPressed: () => setState(() {
                          _agenda.removeAt(i).dispose();
                          _shared.removeAt(i);
                        }),
                        icon: const Icon(PhosphorIconsRegular.x, size: 18),
                      ),
                    ],
                  ),
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton.icon(
                    key: const Key('meeting-agenda-add'),
                    onPressed: () => setState(() {
                      _agenda.add(TextEditingController());
                      _shared.add(true);
                    }),
                    icon: const Icon(PhosphorIconsRegular.plus, size: 18),
                    label: const Text('Add a point'),
                  ),
                ),
              ] else if (agenda.isEmpty)
                Text('No agenda yet.', style: TextStyle(color: colors.inkSoft))
              else
                for (final line in agenda)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 3),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(
                          line['shared'] == false
                              ? PhosphorIconsRegular.lockSimple
                              : PhosphorIconsRegular.circle,
                          size: 14,
                          color: colors.muted,
                        ),
                        const SizedBox(width: 8),
                        Expanded(child: Text(asJsonString(line['text']) ?? '')),
                      ],
                    ),
                  ),
              const SizedBox(height: 20),
              Text(
                'Your private notes',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 4),
              Text(
                'Only you see these. They never appear in a follow-up.',
                style: TextStyle(fontSize: 13, color: colors.inkSoft),
              ),
              TextField(
                key: const Key('meeting-notes'),
                controller: _notes,
                minLines: 2,
                maxLines: 6,
                maxLength: 4000,
                onEditingComplete: () => unawaited(_saveNotes()),
                decoration: const InputDecoration(
                  hintText: 'Notes to yourself',
                ),
              ),
              Align(
                alignment: Alignment.centerRight,
                child: TextButton(
                  key: const Key('meeting-notes-save'),
                  onPressed: _busy ? null : _saveNotes,
                  child: const Text('Save notes'),
                ),
              ),
              const SizedBox(height: 12),
              Text(
                'After the meeting',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 4),
              Text(
                'Tell Jarvis what happened. It proposes decisions and promises '
                'for you to review. Nothing is saved until you accept it.',
                style: TextStyle(fontSize: 13, color: colors.inkSoft),
              ),
              TextField(
                key: const Key('meeting-debrief'),
                controller: _debrief,
                minLines: 3,
                maxLines: 8,
                maxLength: 8000,
                decoration: const InputDecoration(
                  hintText: 'We decided to… I’ll send… Rosa will…',
                ),
              ),
              Align(
                alignment: Alignment.centerRight,
                child: FilledButton(
                  key: const Key('meeting-debrief-read'),
                  onPressed: _busy ? null : _readDebrief,
                  child: const Text('Find follow-through'),
                ),
              ),
              if (pending.isNotEmpty) ...[
                const SizedBox(height: 12),
                Text('Review', style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 6),
                for (final candidate in pending)
                  _CandidateCard(
                    key: Key('candidate-${candidate['id']}'),
                    candidate: candidate,
                    busy: _busy,
                    onAccept: () => unawaited(_accept(candidate)),
                    onDismiss: () => unawaited(_dismiss(candidate)),
                    onOpen: null,
                  ),
              ],
              if (settled.isNotEmpty) ...[
                const SizedBox(height: 12),
                Text(
                  'Reviewed',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(height: 6),
                for (final candidate in settled)
                  _CandidateCard(
                    key: Key('candidate-${candidate['id']}'),
                    candidate: candidate,
                    busy: _busy,
                    onAccept: null,
                    onDismiss: null,
                    onOpen: candidate['resultRef'] is String
                        ? () => unawaited(
                            _openRef(candidate['resultRef'] as String),
                          )
                        : null,
                  ),
              ],
              const SizedBox(height: 20),
              OutlinedButton.icon(
                key: const Key('meeting-followup'),
                onPressed: _followUp,
                icon: const Icon(PhosphorIconsRegular.paperPlaneTilt, size: 18),
                label: const Text('Draft a follow-up'),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _PrepRow extends StatelessWidget {
  const _PrepRow({required this.item, required this.onOpen});

  final Map<String, dynamic> item;
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final question = item['kind'] == 'question';
    return InkWell(
      onTap: onOpen,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 40),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 5),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                question
                    ? PhosphorIconsRegular.chatText
                    : PhosphorIconsRegular.checkCircle,
                size: 18,
                color: question ? colors.warning : colors.accent,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  asJsonString(item['text']) ?? '',
                  style: TextStyle(
                    fontSize: 14.5,
                    height: 1.35,
                    color: colors.ink,
                  ),
                ),
              ),
              if (onOpen != null)
                Icon(
                  PhosphorIconsRegular.arrowUpRight,
                  size: 16,
                  color: colors.muted,
                ),
            ],
          ),
        ),
      ),
    );
  }
}

const _kindLabels = {
  'decision': 'Decision',
  'i_owe': 'You will',
  'they_owe': 'Someone else will',
  'question': 'Open question',
};

class _CandidateCard extends StatelessWidget {
  const _CandidateCard({
    required this.candidate,
    required this.busy,
    required this.onAccept,
    required this.onDismiss,
    required this.onOpen,
    super.key,
  });

  final Map<String, dynamic> candidate;
  final bool busy;
  final VoidCallback? onAccept;
  final VoidCallback? onDismiss;
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final kind = asJsonString(candidate['kind']) ?? '';
    final state = asJsonString(candidate['state']) ?? 'pending';
    final due = jsonDate(candidate['dueOn'], local: true);
    final dueText = asJsonString(candidate['dueText']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: SurfaceCard(
        padding: const EdgeInsets.fromLTRB(14, 10, 8, 6),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Wrap(
              spacing: 6,
              children: [
                StatusPill(
                  label: _kindLabels[kind] ?? kind,
                  color: colors.info,
                ),
                if (state == 'accepted')
                  StatusPill(label: 'Accepted', color: colors.accent),
                if (state == 'dismissed')
                  StatusPill(label: 'Dismissed', color: colors.muted),
              ],
            ),
            const SizedBox(height: 6),
            Text(
              asJsonString(candidate['text']) ?? '',
              style: TextStyle(
                fontSize: 15,
                height: 1.35,
                color: state == 'dismissed' ? colors.muted : colors.ink,
              ),
            ),
            if (kind != 'question' && state == 'pending')
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  [
                    if (asJsonString(candidate['counterparty']) case final who?)
                      'With $who',
                    if (due != null)
                      'Due ${_dueLabel(due)}'
                    else if (dueText != null)
                      'Date unclear (“$dueText”). Pick a day or leave it open',
                  ].join(' · '),
                  style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
                ),
              ),
            Wrap(
              children: [
                if (onAccept != null && kind != 'question')
                  TextButton(
                    key: Key('candidate-accept-${candidate['id']}'),
                    onPressed: busy ? null : onAccept,
                    child: Text(
                      kind == 'decision' ? 'Log decision' : 'Add promise',
                    ),
                  ),
                if (onDismiss != null)
                  TextButton(
                    key: Key('candidate-dismiss-${candidate['id']}'),
                    onPressed: busy ? null : onDismiss,
                    child: Text(kind == 'question' ? 'Answered' : 'Not this'),
                  ),
                if (onOpen != null)
                  TextButton(onPressed: onOpen, child: const Text('Open')),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _AcceptSheet extends StatefulWidget {
  const _AcceptSheet({required this.candidate});

  final Map<String, dynamic> candidate;

  @override
  State<_AcceptSheet> createState() => _AcceptSheetState();
}

class _AcceptSheetState extends State<_AcceptSheet> {
  late final _text = TextEditingController(
    text: asJsonString(widget.candidate['text']) ?? '',
  );
  late final _who = TextEditingController(
    text: asJsonString(widget.candidate['counterparty']) ?? '',
  );
  late DateTime? _due = jsonDate(widget.candidate['dueOn'], local: true);
  double _percent = 70;
  DateTime _review = DateTime.now().add(const Duration(days: 30));

  bool get _decision => widget.candidate['kind'] == 'decision';

  @override
  void dispose() {
    _text.dispose();
    _who.dispose();
    super.dispose();
  }

  Future<void> _pick(bool review) async {
    final picked = await showDatePicker(
      context: context,
      initialDate: (review ? _review : _due) ?? DateTime.now(),
      firstDate: DateTime.now().subtract(const Duration(days: 1)),
      lastDate: DateTime.now().add(const Duration(days: 700)),
    );
    if (picked == null) return;
    setState(() => review ? _review = picked : _due = picked);
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
            _decision ? 'Log this decision' : 'Add this promise',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('accept-text'),
            controller: _text,
            maxLength: 300,
            minLines: 1,
            maxLines: 4,
            decoration: InputDecoration(
              labelText: _decision ? 'What was decided' : 'What was promised',
            ),
          ),
          if (!_decision) ...[
            TextField(
              key: const Key('accept-who'),
              controller: _who,
              decoration: const InputDecoration(labelText: 'With whom'),
            ),
            ListTile(
              key: const Key('accept-due'),
              contentPadding: EdgeInsets.zero,
              title: const Text('Due'),
              subtitle: Text(_due == null ? 'No date' : _dueLabel(_due!)),
              trailing: _due == null
                  ? const Icon(PhosphorIconsRegular.calendarBlank)
                  : IconButton(
                      tooltip: 'No date',
                      onPressed: () => setState(() => _due = null),
                      icon: const Icon(PhosphorIconsRegular.x, size: 18),
                    ),
              onTap: () => unawaited(_pick(false)),
            ),
          ] else ...[
            Text('How sure are you it works out? ${_percent.round()}%'),
            Slider(
              key: const Key('accept-percent'),
              value: _percent,
              min: 1,
              max: 99,
              divisions: 98,
              label: '${_percent.round()}%',
              onChanged: (value) => setState(() => _percent = value),
            ),
            ListTile(
              key: const Key('accept-review'),
              contentPadding: EdgeInsets.zero,
              title: const Text('Check how it went on'),
              subtitle: Text(_dueLabel(_review)),
              trailing: const Icon(PhosphorIconsRegular.calendarBlank),
              onTap: () => unawaited(_pick(true)),
            ),
          ],
          const SizedBox(height: 8),
          FilledButton(
            key: const Key('accept-save'),
            onPressed: _text.text.trim().isEmpty
                ? null
                : () => Navigator.pop<Map<String, Object?>>(context, {
                    'description': _text.text.trim(),
                    if (!_decision) ...{
                      'counterparty': _who.text.trim().isEmpty
                          ? null
                          : _who.text.trim(),
                      'dueOn': _due == null ? null : _iso(_due!),
                      'clearDue': _due == null,
                    } else ...{
                      'probability': _percent.round() / 100,
                      'reviewOn': _iso(_review),
                    },
                  }),
            child: Text(_decision ? 'Log decision' : 'Add promise'),
          ),
        ],
      ),
    ),
  );
}

class _FollowUpSheet extends StatelessWidget {
  const _FollowUpSheet({required this.draft});

  final Map<String, dynamic> draft;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final body = asJsonString(draft['body']) ?? '';
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              asJsonString(draft['subject']) ?? 'Follow-up',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 4),
            Text(
              'A draft. Nothing has been sent. Private notes and private agenda points are left out.',
              key: const Key('followup-unsent'),
              style: TextStyle(fontSize: 13, color: colors.inkSoft),
            ),
            const SizedBox(height: 12),
            SelectableText(
              body,
              style: const TextStyle(fontSize: 15, height: 1.45),
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              key: const Key('followup-copy'),
              onPressed: () async {
                await Clipboard.setData(ClipboardData(text: body));
                if (context.mounted) {
                  ScaffoldMessenger.maybeOf(context)
                    ?..hideCurrentSnackBar()
                    ..showSnackBar(
                      const SnackBar(
                        content: Text(
                          'Copied. Send it yourself when you are ready.',
                        ),
                      ),
                    );
                }
              },
              icon: const Icon(PhosphorIconsRegular.copy, size: 18),
              label: const Text('Copy'),
            ),
          ],
        ),
      ),
    );
  }
}
