import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'meeting_screen.dart';

const meetingTemplates = {
  'general': 'General',
  'one_on_one': 'One-on-one',
  'project_update': 'Project update',
  'interview': 'Interview',
  'decision': 'Decision',
};

String meetingDay(Object? value) {
  final date = jsonDate(value, local: true);
  if (date == null) return '';
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
  final time =
      '${date.hour.toString().padLeft(2, '0')}:${date.minute.toString().padLeft(2, '0')}';
  return '${date.day} ${months[date.month - 1]} · $time';
}

/// Meetings: prepare from your own records, capture what happened, and turn it into reviewed follow-through.
class MeetingsScreen extends StatefulWidget {
  const MeetingsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<MeetingsScreen> createState() => _MeetingsScreenState();
}

class _MeetingsScreenState extends State<MeetingsScreen> {
  List<Map<String, dynamic>> _meetings = const [];
  List<Map<String, dynamic>> _events = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/meetings');
      var events = <Map<String, dynamic>>[];
      try {
        final calendar = await widget.http.get<dynamic>(
          '/api/v1/meetings/events',
          queryParameters: {'days': 7},
        );
        events = jsonMaps(calendar.data);
      } on DioException {
        // No calendar is fine: meetings can be started by hand.
      }
      if (!mounted) return;
      setState(() {
        _meetings = [
          for (final item in jsonMaps(response.data))
            if (jsonString(item, 'id') != null) item,
        ];
        _events = events;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your meetings.';
      });
    }
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => MeetingScreen(http: widget.http, meetingId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create({String? title, String? startsAt}) async {
    List<Map<String, dynamic>> projects = const [];
    try {
      final response = await widget.http.get<dynamic>('/api/v1/projects');
      projects = jsonMaps(response.data);
    } on DioException {
      // A project is optional.
    }
    if (!mounted) return;
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _NewMeetingSheet(initialTitle: title, projects: projects),
    );
    if (draft == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/meetings',
        data: {...draft, 'startsAt': startsAt},
      );
      final id = jsonString(jsonObject(response.data) ?? const {}, 'id');
      if (id == null || !mounted) return;
      // Build the prep pack straight away: that is why the meeting was created.
      try {
        await widget.http.post<dynamic>('/api/v1/meetings/$id/prep');
      } on DioException {
        // The pack can be built from the meeting page.
      }
      if (mounted) await _open(id);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'Could not create the meeting.',
            ),
          ),
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Meetings'),
        actions: [
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => _create(),
          ),
        ],
      ),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
                children: [
                  ContentWidth(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (_events.isNotEmpty) ...[
                          Text(
                            'Coming up on your calendar',
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                          const SizedBox(height: 8),
                          for (final event in _events.take(5))
                            Padding(
                              padding: const EdgeInsets.only(bottom: 8),
                              child: SurfaceCard(
                                key: Key('meeting-event-${event['title']}'),
                                padding: const EdgeInsets.fromLTRB(
                                  16,
                                  10,
                                  8,
                                  10,
                                ),
                                child: Row(
                                  children: [
                                    Expanded(
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        children: [
                                          Text(
                                            asJsonString(event['title']) ?? '',
                                            style: TextStyle(
                                              fontSize: 15,
                                              fontWeight: FontWeight.w600,
                                              color: colors.ink,
                                            ),
                                          ),
                                          Text(
                                            meetingDay(event['startAt']),
                                            style: TextStyle(
                                              fontSize: 13,
                                              color: colors.inkSoft,
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                    TextButton(
                                      onPressed: () => unawaited(
                                        _create(
                                          title: asJsonString(event['title']),
                                          startsAt: asJsonString(
                                            event['startAt'],
                                          ),
                                        ),
                                      ),
                                      child: const Text('Prepare'),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          const SizedBox(height: 12),
                        ],
                        if (_meetings.isEmpty)
                          Padding(
                            padding: const EdgeInsets.only(top: 24),
                            child: EmptyState(
                              icon: PhosphorIconsRegular.calendarCheck,
                              title:
                                  'Walk in prepared, leave with follow-through',
                              message:
                                  'Jarvis gathers what is open with the people '
                                  'involved, and afterwards turns what you tell '
                                  'it into promises and decisions you review.',
                              action: FilledButton(
                                key: const Key('meeting-start'),
                                onPressed: () => _create(),
                                child: const Text('Prepare a meeting'),
                              ),
                            ),
                          )
                        else
                          for (final meeting in _meetings)
                            Padding(
                              padding: const EdgeInsets.only(bottom: 10),
                              child: SurfaceCard(
                                key: Key('meeting-${meeting['id']}'),
                                onTap: () =>
                                    unawaited(_open(meeting['id'] as String)),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      asJsonString(meeting['title']) ??
                                          'Meeting',
                                      style: TextStyle(
                                        fontSize: 16,
                                        fontWeight: FontWeight.w600,
                                        color: colors.ink,
                                      ),
                                    ),
                                    const SizedBox(height: 4),
                                    Text(
                                      [
                                        if (meeting['startsAt'] != null)
                                          meetingDay(meeting['startsAt']),
                                        if (asJsonInt(meeting['open']) > 0)
                                          '${asJsonInt(meeting['open'])} to review',
                                        if (asJsonInt(meeting['accepted']) > 0)
                                          '${asJsonInt(meeting['accepted'])} accepted',
                                      ].join(' · '),
                                      style: TextStyle(
                                        fontSize: 13,
                                        color: colors.inkSoft,
                                      ),
                                    ),
                                  ],
                                ),
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

class _NewMeetingSheet extends StatefulWidget {
  const _NewMeetingSheet({required this.projects, this.initialTitle});

  final String? initialTitle;
  final List<Map<String, dynamic>> projects;

  @override
  State<_NewMeetingSheet> createState() => _NewMeetingSheetState();
}

class _NewMeetingSheetState extends State<_NewMeetingSheet> {
  late final _title = TextEditingController(text: widget.initialTitle ?? '');
  final _people = TextEditingController();
  final _purpose = TextEditingController();
  String _template = 'general';
  String? _project;

  @override
  void dispose() {
    _title.dispose();
    _people.dispose();
    _purpose.dispose();
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
            'Prepare a meeting',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('meeting-title-field'),
            controller: _title,
            maxLength: 120,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(labelText: 'Meeting'),
          ),
          TextField(
            key: const Key('meeting-people-field'),
            controller: _people,
            decoration: const InputDecoration(
              labelText: 'Who is there? (names, comma-separated)',
              hintText: 'Rosa, Tom',
            ),
          ),
          TextField(
            key: const Key('meeting-purpose-field'),
            controller: _purpose,
            maxLength: 300,
            decoration: const InputDecoration(labelText: 'What is it for?'),
          ),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final entry in meetingTemplates.entries)
                ChoiceChip(
                  key: Key('meeting-template-${entry.key}'),
                  label: Text(entry.value),
                  selected: _template == entry.key,
                  onSelected: (_) => setState(() => _template = entry.key),
                ),
            ],
          ),
          if (widget.projects.isNotEmpty) ...[
            const SizedBox(height: 12),
            DropdownButtonFormField<String?>(
              key: const Key('meeting-project-field'),
              initialValue: _project,
              decoration: const InputDecoration(labelText: 'Project'),
              items: [
                const DropdownMenuItem(value: null, child: Text('No project')),
                for (final project in widget.projects)
                  if (jsonString(project, 'id') != null)
                    DropdownMenuItem(
                      value: jsonString(project, 'id'),
                      child: Text(asJsonString(project['name']) ?? 'Project'),
                    ),
              ],
              onChanged: (value) => setState(() => _project = value),
            ),
          ],
          const SizedBox(height: 14),
          FilledButton(
            key: const Key('meeting-create'),
            onPressed: _title.text.trim().isEmpty
                ? null
                : () => Navigator.pop<Map<String, Object?>>(context, {
                    'title': _title.text.trim(),
                    'purpose': _purpose.text.trim().isEmpty
                        ? null
                        : _purpose.text.trim(),
                    'template': _template,
                    'projectId': _project,
                    'participants': [
                      for (final name in _people.text.split(','))
                        if (name.trim().isNotEmpty) {'name': name.trim()},
                    ],
                  }),
            child: const Text('Prepare'),
          ),
        ],
      ),
    ),
  );
}
