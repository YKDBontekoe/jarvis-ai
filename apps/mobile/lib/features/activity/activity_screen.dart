import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../entities/entity_ref.dart';
import '../entities/entity_screen.dart';

enum ActivityFilter { all, jarvis }

/// Everything that happened across Jarvis, newest first: reminders that went
/// off, watches that fired, tasks, approvals, commitments, uploads, and what
/// Jarvis did on its own. Each row opens the thing it is about.
class ActivityScreen extends StatefulWidget {
  const ActivityScreen({
    required this.http,
    this.onOpenConversation,
    this.initialFilter = ActivityFilter.all,
    super.key,
  });

  final Dio http;
  final Future<void> Function(String conversationId)? onOpenConversation;
  final ActivityFilter initialFilter;

  @override
  State<ActivityScreen> createState() => _ActivityScreenState();
}

class _ActivityScreenState extends State<ActivityScreen> {
  late ActivityFilter _filter = widget.initialFilter;
  List<Map<String, dynamic>> _events = const [];
  bool _loading = true;
  String? _error;
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_revision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/events',
        queryParameters: {
          'limit': 100,
          if (_filter == ActivityFilter.jarvis)
            'origins': 'Agent,AgentReaction',
        },
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _events = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load what happened.';
      });
    }
  }

  VoidCallback? _openFor(Map<String, dynamic> event) {
    final subject = EntityRef.tryParse(asJsonString(event['subjectRef']));
    if (subject != null) {
      return () => unawaited(
        openEntity(
          context,
          widget.http,
          subject,
          onOpenConversation: widget.onOpenConversation,
        ),
      );
    }
    final conversation = asJsonString(event['conversationId']);
    if (conversation != null && widget.onOpenConversation != null) {
      return () => unawaited(widget.onOpenConversation!(conversation));
    }
    return null;
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const PageTitle('Activity')),
    body: Column(
      children: [
        ContentWidth(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
            child: SegmentedPills<ActivityFilter>(
              keyPrefix: 'activity-filter',
              options: const [
                (ActivityFilter.all, 'Everything'),
                (ActivityFilter.jarvis, 'By Jarvis'),
              ],
              selected: _filter,
              onSelected: (filter) {
                if (filter == _filter) return;
                setState(() {
                  _filter = filter;
                  _events = const [];
                });
                unawaited(_load());
              },
            ),
          ),
        ),
        Expanded(
          child: ListScreenBody(
            loading: _loading,
            error: _error,
            isEmpty: _events.isEmpty,
            onRetry: () => unawaited(_load()),
            onRefresh: _load,
            empty: EmptyState(
              icon: PhosphorIconsRegular.pulse,
              title: _filter == ActivityFilter.jarvis
                  ? 'Jarvis has not acted on its own yet'
                  : 'Nothing happened yet',
              message: _filter == ActivityFilter.jarvis
                  ? 'When a watch fires, a task fails or someone needs a '
                        'reply, Jarvis follows up here.'
                  : 'Reminders, watches, tasks and approvals show up here as '
                        'they happen.',
            ),
            child: ListView(
              padding: EdgeInsets.fromLTRB(
                16,
                0,
                16,
                32 + MediaQuery.paddingOf(context).bottom,
              ),
              children: [
                ContentWidth(
                  child: GroupedSection(
                    dividerIndent: 60,
                    children: [
                      for (final event in _events)
                        ActivityRow(event: event, onTap: _openFor(event)),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    ),
  );
}
