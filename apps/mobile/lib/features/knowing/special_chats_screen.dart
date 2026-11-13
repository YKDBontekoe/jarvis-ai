import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Talk differently: chats where Jarvis plays a part. The stranger interviews
/// you from scratch, a rehearsal plays someone you need to talk to, past you
/// knows only what you had said by a date, and the council argues it out.
class SpecialChatsScreen extends StatefulWidget {
  const SpecialChatsScreen({
    required this.http,
    this.onOpenConversation,
    super.key,
  });

  final Dio http;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<SpecialChatsScreen> createState() => _SpecialChatsScreenState();
}

const defaultAdvisers = [
  'The sceptic',
  'The stoic',
  'Your accountant',
  'Your future self',
];

class _SpecialChatsScreenState extends State<SpecialChatsScreen> {
  List<Map<String, dynamic>> _recent = const [];
  bool _starting = false;

  @override
  void initState() {
    super.initState();
    unawaited(_loadRecent());
  }

  Future<void> _loadRecent() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/chat-modes');
      if (mounted) setState(() => _recent = jsonMaps(response.data));
    } catch (_) {
      // The starters work without the list of earlier chats.
    }
  }

  Future<void> _start(Map<String, Object?> body) async {
    if (_starting) return;
    setState(() => _starting = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/chat-modes',
        data: body,
      );
      final id = asJsonString(jsonObject(response.data)?['conversationId']);
      unawaited(HapticFeedback.lightImpact());
      if (id != null) await widget.onOpenConversation?.call(id);
      if (mounted) unawaited(_loadRecent());
    } on DioException catch (error) {
      _show(
        firstProblemMessage(error.response?.data) ??
            'Could not start the chat.',
      );
    } catch (_) {
      _show('Could not start the chat.');
    } finally {
      if (mounted) setState(() => _starting = false);
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _rehearse() async {
    List<Map<String, dynamic>> people;
    try {
      people = jsonMaps(
        (await widget.http.get<dynamic>('/api/v1/people')).data,
      );
    } catch (_) {
      _show('Could not load People.');
      return;
    }
    if (!mounted) return;
    if (people.isEmpty) {
      _show('Add someone to People first.');
      return;
    }
    final body = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _RehearseSheet(people: people),
    );
    if (body != null) await _start(body);
  }

  Future<void> _pastSelf() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: DateTime(now.year - 1, now.month, now.day),
      firstDate: DateTime(now.year - 10),
      lastDate: now.subtract(const Duration(days: 1)),
      helpText: 'Talk to you on…',
      builder: use24HourClock,
    );
    if (picked == null) return;
    final day =
        '${picked.year.toString().padLeft(4, '0')}-'
        '${picked.month.toString().padLeft(2, '0')}-'
        '${picked.day.toString().padLeft(2, '0')}';
    await _start({'kind': 'past_self', 'asOf': day});
  }

  Future<void> _council() async {
    final body = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _CouncilSheet(),
    );
    if (body != null) await _start(body);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const PageTitle('Talk differently')),
    body: ListView(
      key: const Key('special-chats-list'),
      padding: EdgeInsets.fromLTRB(
        16,
        8,
        16,
        32 + MediaQuery.paddingOf(context).bottom,
      ),
      children: [
        const ContentWidth(
          child: HeroCard(
            label: 'Talk differently',
            title: 'Chats where Jarvis plays a part',
            maxTitleScale: 1.6,
            subtitle:
                'Practise a hard talk, hear from past you, or let a council '
                'argue before you decide.',
            footer: HeroNote(
              'Only the stranger’s interview updates what Jarvis remembers. '
              'Role play never does.',
            ),
          ),
        ),
        const ContentWidth(child: GroupLabel('Start one')),
        ContentWidth(
          child: FeatureRow(
            key: const Key('special-stranger'),
            index: 0,
            icon: PhosphorIconsRegular.handWaving,
            title: 'The stranger',
            subtitle:
                'Jarvis forgets you for a moment and interviews you from '
                'scratch, then shows what changed since it last knew you.',
            onTap: _starting
                ? null
                : () => unawaited(_start({'kind': 'stranger'})),
          ),
        ),
        ContentWidth(
          child: FeatureRow(
            key: const Key('special-rehearse'),
            index: 1,
            icon: PhosphorIconsRegular.chatsCircle,
            title: 'Rehearse a conversation',
            subtitle:
                'Jarvis plays someone from People, the way they write, '
                'then gives you notes. Nothing is sent to them.',
            onTap: _starting ? null : () => unawaited(_rehearse()),
          ),
        ),
        ContentWidth(
          child: FeatureRow(
            key: const Key('special-past'),
            index: 2,
            icon: PhosphorIconsRegular.clockCounterClockwise,
            title: 'Talk to past you',
            subtitle:
                'You on a day you pick, knowing only what you had told Jarvis '
                'and written in your journal by then.',
            onTap: _starting ? null : () => unawaited(_pastSelf()),
          ),
        ),
        ContentWidth(
          child: FeatureRow(
            key: const Key('special-council'),
            index: 3,
            icon: PhosphorIconsRegular.users,
            title: 'The council',
            subtitle:
                'A sceptic, a stoic, your accountant and your future self '
                'argue your question. The decision stays yours.',
            onTap: _starting ? null : () => unawaited(_council()),
          ),
        ),
        if (_recent.isNotEmpty) ...[
          ContentWidth(child: GroupLabel('Earlier', count: _recent.length)),
          for (final (i, chat) in _recent.take(8).indexed)
            ContentWidth(
              child: FeatureRow(
                key: Key('special-recent-${chat['conversationId']}'),
                index: 4 + i,
                quiet: true,
                icon: switch (asJsonString(chat['kind'])) {
                  'stranger' => PhosphorIconsRegular.handWaving,
                  'rehearse' => PhosphorIconsRegular.chatsCircle,
                  'past_self' => PhosphorIconsRegular.clockCounterClockwise,
                  _ => PhosphorIconsRegular.users,
                },
                title: asJsonString(chat['title']) ?? 'Chat',
                onTap: () {
                  final id = asJsonString(chat['conversationId']);
                  if (id != null) {
                    unawaited(widget.onOpenConversation?.call(id));
                  }
                },
              ),
            ),
        ],
      ],
    ),
  );
}

class _RehearseSheet extends StatefulWidget {
  const _RehearseSheet({required this.people});

  final List<Map<String, dynamic>> people;

  @override
  State<_RehearseSheet> createState() => _RehearseSheetState();
}

class _RehearseSheetState extends State<_RehearseSheet> {
  String? _personId;
  final _scenario = TextEditingController();
  final _goal = TextEditingController();

  @override
  void dispose() {
    _scenario.dispose();
    _goal.dispose();
    super.dispose();
  }

  bool get _ready => _personId != null && _scenario.text.trim().isNotEmpty;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
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
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Rehearse with…',
              style: TextStyle(
                fontFamily: 'Geist',
                fontSize: 20,
                fontWeight: FontWeight.w600,
                letterSpacing: -.5,
                color: colors.ink,
              ),
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final person in widget.people.take(30))
                  ChoiceChip(
                    key: Key('rehearse-person-${person['id']}'),
                    label: Text(asJsonString(person['name']) ?? ''),
                    selected: _personId == person['id'],
                    onSelected: (_) =>
                        setState(() => _personId = asJsonString(person['id'])),
                  ),
              ],
            ),
            const SizedBox(height: 16),
            TextField(
              key: const Key('rehearse-scenario'),
              controller: _scenario,
              maxLength: 500,
              buildCounter: quietCounter,
              minLines: 2,
              maxLines: 4,
              textCapitalization: TextCapitalization.sentences,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: 'What is it about?',
                hintText: 'Telling her I can’t make the wedding',
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              key: const Key('rehearse-goal'),
              controller: _goal,
              maxLength: 500,
              buildCounter: quietCounter,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'What do you hope for? (optional)',
              ),
            ),
            const SizedBox(height: 12),
            SizedBox(
              width: double.infinity,
              child: PillAction(
                key: const Key('rehearse-start'),
                label: 'Start rehearsal',
                primary: true,
                onPressed: _ready
                    ? () => Navigator.of(context).pop({
                        'kind': 'rehearse',
                        'personId': _personId,
                        'scenario': _scenario.text.trim(),
                        if (_goal.text.trim().isNotEmpty)
                          'goal': _goal.text.trim(),
                      })
                    : null,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _CouncilSheet extends StatefulWidget {
  const _CouncilSheet();

  @override
  State<_CouncilSheet> createState() => _CouncilSheetState();
}

class _CouncilSheetState extends State<_CouncilSheet> {
  final _question = TextEditingController();
  final Set<String> _chosen = {...defaultAdvisers};

  @override
  void dispose() {
    _question.dispose();
    super.dispose();
  }

  bool get _ready => _question.text.trim().isNotEmpty && _chosen.length >= 2;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
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
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Ask the council',
              style: TextStyle(
                fontFamily: 'Geist',
                fontSize: 20,
                fontWeight: FontWeight.w600,
                letterSpacing: -.5,
                color: colors.ink,
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('council-question'),
              controller: _question,
              maxLength: 500,
              buildCounter: quietCounter,
              minLines: 2,
              maxLines: 4,
              textCapitalization: TextCapitalization.sentences,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: 'Your question',
                hintText: 'Should I take the job in Berlin?',
              ),
            ),
            const SizedBox(height: 8),
            Text('ADVISERS', style: JarvisType.capsOf(context)),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final adviser in [
                  ...defaultAdvisers,
                  'Your best friend',
                  'The optimist',
                ])
                  FilterChip(
                    label: Text(adviser),
                    selected: _chosen.contains(adviser),
                    // The council seats at most five.
                    onSelected: (on) {
                      if (on && _chosen.length >= 5) return;
                      setState(
                        () =>
                            on ? _chosen.add(adviser) : _chosen.remove(adviser),
                      );
                    },
                  ),
              ],
            ),
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: PillAction(
                key: const Key('council-start'),
                label: 'Seat the council',
                primary: true,
                onPressed: _ready
                    ? () => Navigator.of(context).pop({
                        'kind': 'council',
                        'question': _question.text.trim(),
                        'advisers': _chosen.toList(),
                      })
                    : null,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
