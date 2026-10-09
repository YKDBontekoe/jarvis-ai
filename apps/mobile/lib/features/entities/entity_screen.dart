import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../shell/utility_pages.dart';
import 'entity_ref.dart';

/// Opens [ref]: a chat through [onOpenConversation] when there is one,
/// anything else on its own page.
Future<void> openEntity(
  BuildContext context,
  Dio http,
  EntityRef ref, {
  Future<void> Function(String conversationId)? onOpenConversation,
}) async {
  if (ref.type == 'conversation' && onOpenConversation != null) {
    await onOpenConversation(ref.id);
    return;
  }
  await Navigator.of(context).push<void>(
    MaterialPageRoute<void>(
      builder: (_) => EntityScreen(
        http: http,
        entity: ref,
        onOpenConversation: onOpenConversation,
      ),
    ),
  );
}

/// One thing, wherever it lives: what it is, everything related to it (the
/// chat it came from, what it reminds about, links Jarvis or you drew) and
/// what happened to it, with a way into its own feature.
class EntityScreen extends StatefulWidget {
  const EntityScreen({
    required this.http,
    required this.entity,
    this.onOpenConversation,
    super.key,
  });

  final Dio http;
  final EntityRef entity;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<EntityScreen> createState() => _EntityScreenState();
}

class _EntityScreenState extends State<EntityScreen> {
  String? _title;
  List<Map<String, dynamic>> _events = const [];
  bool _loading = true;
  bool _missing = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final ref = widget.entity;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/entities/${ref.type}/${ref.id}/related',
      );
      List<Map<String, dynamic>> events = const [];
      try {
        final history = await widget.http.get<dynamic>(
          '/api/v1/events',
          queryParameters: {'subject': '$ref', 'limit': 20},
        );
        events = jsonMaps(history.data);
      } on DioException {
        // The page still works without its history.
      }
      if (!mounted) return;
      setState(() {
        _title = asJsonString(jsonObject(response.data)?['title']);
        _events = events;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _missing = error.response?.statusCode == 404;
        _error = _missing
            ? null
            : firstProblemMessage(error.response?.data) ??
                  'Could not load this ${widget.entity.kind.label.toLowerCase()}.';
      });
    }
  }

  void _openFeature() {
    final ref = widget.entity;
    if (ref.type == 'conversation') {
      unawaited(widget.onOpenConversation?.call(ref.id));
      return;
    }
    final destination = featureDestinationFor(ref);
    if (destination == null) return;
    final page = utilityPageFor(
      destination,
      widget.http,
      onOpenConversation: widget.onOpenConversation,
    );
    if (page == null) return;
    unawaited(
      Navigator.of(
        context,
      ).push<void>(MaterialPageRoute<void>(builder: (_) => page)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final ref = widget.entity;
    final kind = ref.kind;
    final colors = JarvisColors.of(context);
    final canOpen = ref.type == 'conversation'
        ? widget.onOpenConversation != null
        : featureDestinationFor(ref) != null;
    return Scaffold(
      appBar: AppBar(title: PageTitle(kind.label)),
      body: _loading && _title == null
          ? const SkeletonList()
          : _missing
          ? EmptyState(
              icon: kind.icon,
              title: 'Not here any more',
              message:
                  'This ${kind.label.toLowerCase()} was removed or never '
                  'belonged to you.',
            )
          : _error != null && _title == null
          ? ErrorState(message: _error!, onRetry: () => unawaited(_load()))
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
                children: [
                  ContentWidth(
                    child: SurfaceCard(
                      child: Row(
                        children: [
                          IconBadge(icon: kind.icon, size: 44),
                          const SizedBox(width: 14),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  _title ?? kind.label,
                                  key: const Key('entity-title'),
                                  style: Theme.of(
                                    context,
                                  ).textTheme.titleMedium,
                                ),
                                const SizedBox(height: 2),
                                Text(
                                  kind.label,
                                  style: TextStyle(
                                    color: colors.muted,
                                    fontSize: 13,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  if (canOpen) ...[
                    const SizedBox(height: 12),
                    ContentWidth(
                      child: Align(
                        alignment: Alignment.centerLeft,
                        child: FilledButton.tonalIcon(
                          key: const Key('entity-open-feature'),
                          onPressed: _openFeature,
                          icon: const Icon(
                            PhosphorIconsRegular.arrowUpRight,
                            size: 18,
                          ),
                          label: Text('Open in ${kind.featureLabel}'),
                        ),
                      ),
                    ),
                  ],
                  const SizedBox(height: 20),
                  ContentWidth(
                    child: RelatedPanel(
                      http: widget.http,
                      entity: ref,
                      onOpenConversation: widget.onOpenConversation,
                      emptyMessage:
                          'Nothing is linked yet. Ask Jarvis to connect it '
                          'to a task, a person or a project.',
                    ),
                  ),
                  if (_events.isNotEmpty) ...[
                    const SizedBox(height: 20),
                    const ContentWidth(child: SectionHeader('History')),
                    ContentWidth(
                      child: GroupedSection(
                        children: [
                          for (final event in _events)
                            ActivityRow(event: event),
                        ],
                      ),
                    ),
                  ],
                ],
              ),
            ),
    );
  }
}

/// Everything related to [entity], as tappable rows under a "Related"
/// heading. Shows nothing while loading or when nothing is related, unless
/// [emptyMessage] is given.
class RelatedPanel extends StatefulWidget {
  const RelatedPanel({
    required this.http,
    required this.entity,
    this.onOpenConversation,
    this.emptyMessage,
    super.key,
  });

  final Dio http;
  final EntityRef entity;
  final Future<void> Function(String conversationId)? onOpenConversation;
  final String? emptyMessage;

  @override
  State<RelatedPanel> createState() => _RelatedPanelState();
}

class _RelatedPanelState extends State<RelatedPanel> {
  List<Map<String, dynamic>>? _related;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(RelatedPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.entity != widget.entity) unawaited(_load());
  }

  Future<void> _load() async {
    final ref = widget.entity;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/entities/${ref.type}/${ref.id}/related',
      );
      if (!mounted || ref != widget.entity) return;
      setState(
        () => _related = jsonMaps(jsonObject(response.data)?['related']),
      );
    } on DioException {
      if (mounted) setState(() => _related = const []);
    }
  }

  @override
  Widget build(BuildContext context) {
    final related = _related;
    if (related == null) return const SizedBox.shrink();
    final rows = [
      for (final item in related)
        if (EntityRef.tryParse(asJsonString(item['ref'])) case final ref?)
          (ref, item),
    ];
    if (rows.isEmpty && widget.emptyMessage == null) {
      return const SizedBox.shrink();
    }
    final colors = JarvisColors.of(context);
    return Column(
      key: const Key('related-panel'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader('Related'),
        if (rows.isEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(4, 0, 4, 0),
            child: Text(
              widget.emptyMessage!,
              style: TextStyle(color: colors.muted, fontSize: 13.5),
            ),
          )
        else
          GroupedSection(
            dividerIndent: 60,
            children: [
              for (final (ref, item) in rows)
                ListTile(
                  key: Key('related-$ref'),
                  leading: IconBadge(icon: ref.kind.icon, size: 32),
                  title: Text(
                    asJsonString(item['title']) ?? ref.kind.label,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  subtitle: Text(
                    '${ref.kind.label} · ${relationLabel(asJsonString(item['relation']), asJsonString(item['direction']))}',
                    style: TextStyle(color: colors.muted, fontSize: 12.5),
                  ),
                  trailing: Icon(
                    PhosphorIconsRegular.caretRight,
                    size: 16,
                    color: colors.muted,
                  ),
                  onTap: () => unawaited(
                    openEntity(
                      context,
                      widget.http,
                      ref,
                      onOpenConversation: widget.onOpenConversation,
                    ),
                  ),
                ),
            ],
          ),
      ],
    );
  }
}

/// "made this", "came from", "reminds about": how a related thing relates.
String relationLabel(String? relation, String? direction) {
  final incoming = direction == 'in';
  return switch (relation) {
    'created' => incoming ? 'where it was made' : 'made here',
    'source' => incoming ? 'where it came from' : 'came from this',
    'reminds' => incoming ? 'reminder for it' : 'reminds about this',
    'part_of' => incoming ? 'part of this' : 'belongs to',
    'follows_up' => incoming ? 'followed up by' : 'follows up',
    'blocks' => incoming ? 'blocked by' : 'blocks',
    'about' => 'about',
    'mentions' => 'mentioned',
    null || '' => 'related',
    final other => other.replaceAll('_', ' '),
  };
}

/// One event in a feed: what happened, when, and whether Jarvis did it.
class ActivityRow extends StatelessWidget {
  const ActivityRow({required this.event, this.onTap, this.now, super.key});

  final Map<String, dynamic> event;
  final VoidCallback? onTap;
  final DateTime? now;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final kind = asJsonString(event['kind']) ?? '';
    final origin = asJsonString(event['origin']) ?? '';
    final byJarvis = origin == 'Agent' || origin == 'AgentReaction';
    final at = jsonDate(event['at'], local: true);
    final subject = EntityRef.tryParse(asJsonString(event['subjectRef']));
    return ListTile(
      key: Key('activity-${asJsonString(event['id'])}'),
      onTap: onTap,
      leading: IconBadge(
        icon: byJarvis
            ? PhosphorIconsRegular.sparkle
            : eventIcon(kind, subject),
        color: byJarvis ? colors.accent : null,
        size: 32,
      ),
      title: Text(
        asJsonString(event['summary']) ?? kind,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      subtitle: Text(
        [
          if (byJarvis) 'Jarvis',
          eventKindLabel(kind),
          if (at != null) relativeFromNow(at, now: now),
        ].join(' · '),
        style: TextStyle(color: colors.muted, fontSize: 12.5),
      ),
      trailing: onTap == null
          ? null
          : Icon(
              PhosphorIconsRegular.caretRight,
              size: 16,
              color: colors.muted,
            ),
    );
  }
}

IconData eventIcon(String kind, EntityRef? subject) => switch (kind) {
  'watch.fired' => PhosphorIconsRegular.eye,
  'task.failed' => PhosphorIconsRegular.warningCircle,
  'task.completed' => PhosphorIconsRegular.checkCircle,
  'approval.requested' ||
  'approval.decided' => PhosphorIconsRegular.shieldCheck,
  'reminder.due' => PhosphorIconsRegular.alarm,
  'inbox.needs_reply' => PhosphorIconsRegular.chatText,
  'message.received' => PhosphorIconsRegular.chatCircle,
  'webhook.called' => PhosphorIconsRegular.webhooksLogo,
  'heartbeat.checkin' => PhosphorIconsRegular.bellRinging,
  _ => subject?.kind.icon ?? PhosphorIconsRegular.pulse,
};

String eventKindLabel(String kind) => switch (kind) {
  'reminder.due' => 'Reminder',
  'watch.fired' => 'Watch',
  'task.completed' => 'Task done',
  'task.failed' => 'Task failed',
  'approval.requested' => 'Needs approval',
  'approval.decided' => 'Approval',
  'inbox.needs_reply' => 'Needs a reply',
  'commitment.created' => 'Commitment',
  'journal.saved' => 'Journal',
  'expense.logged' => 'Expense',
  'file.uploaded' => 'File',
  'message.received' => 'Message',
  'webhook.called' => 'Webhook',
  'memory.learned' => 'Memory',
  'mission.step_completed' || 'mission.step_failed' => 'Mission',
  'heartbeat.checkin' => 'Check-in',
  'agent.acted' => 'Acted on its own',
  _ => kind,
};
