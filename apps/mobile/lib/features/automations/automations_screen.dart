import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../approvals/approvals_screen.dart';
import 'automation_studio_screen.dart';
import 'routine_suggestions.dart';
import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

class AutomationsScreen extends StatefulWidget {
  const AutomationsScreen({
    required this.http,
    this.onOpenConversation,
    super.key,
  });

  final Dio http;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<AutomationsScreen> createState() => _AutomationsScreenState();
}

class _AutomationsScreenState extends State<AutomationsScreen> {
  List<Map<String, dynamic>> _rules = [];
  List<Map<String, dynamic>> _suggestions = [];
  final Set<String> _suggestionBusy = {};
  final Set<String> _busy = {};
  bool _loading = true;
  String? _error;
  String? _deviceZone;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
    deviceTimeZoneLookup().then((zone) {
      if (mounted && zone != null) setState(() => _deviceZone = zone);
    });
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>('/api/v1/automations');
      if (mounted && revision == _requestRevision) {
        setState(() => _rules = jsonMaps(response.data));
      }
      await _loadSuggestions(revision);
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load automations.');
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load automations.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  /// Suggestions are a bonus: when they cannot be loaded the list still works.
  Future<void> _loadSuggestions(int revision) async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/routines/suggestions',
      );
      if (mounted && revision == _requestRevision) {
        setState(() => _suggestions = jsonMaps(response.data));
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() => _suggestions = []);
      }
    }
  }

  Future<void> _createFromSuggestion(String id) async {
    setState(() => _suggestionBusy.add(id));
    try {
      await widget.http.post<dynamic>(
        '/api/v1/routines/suggestions/$id/accept',
      );
      await _load();
      if (mounted) _show('Draft created. Switch it on when you are ready.');
    } on DioException catch (error) {
      if (mounted) {
        _show(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not create that automation.',
        );
      }
    } catch (_) {
      if (mounted) _show('Jarvis could not create that automation.');
    } finally {
      if (mounted) setState(() => _suggestionBusy.remove(id));
    }
  }

  Future<void> _dismissSuggestion(String id) async {
    setState(() => _suggestionBusy.add(id));
    try {
      await widget.http.post<dynamic>(
        '/api/v1/routines/suggestions/$id/dismiss',
      );
      if (mounted) {
        setState(
          () => _suggestions = [
            for (final s in _suggestions)
              if (jsonId(s) != id) s,
          ],
        );
      }
    } catch (_) {
      if (mounted) _show('Jarvis could not dismiss that suggestion.');
    } finally {
      if (mounted) setState(() => _suggestionBusy.remove(id));
    }
  }

  Future<void> _create() async {
    final zone = await deviceTimeZoneLookup() ?? await _savedTimeZone();
    if (!mounted) return;
    final created = await showJarvisDialog<_NewAutomation>(
      context: context,
      builder: (_) => _NewAutomationDialog(timeZoneId: zone),
    );
    if (created == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/automations',
        data: {
          'name': created.name,
          'definition': {
            'schemaVersion': 1,
            'trigger': {
              'kind': 'schedule',
              'localTime': created.localTime,
              'timeZoneId': zone,
              if (created.weekdays != 0 && created.weekdays != 127)
                'weekdays': created.weekdays,
            },
            'actions': [
              {
                'kind': 'notification',
                'title': created.notificationTitle,
                'body': created.notificationBody,
              },
            ],
            'limits': {'cooldownMinutes': 5, 'maxActionsPerRun': 3},
          },
        },
      );
      // Created from here means "do this": switch it on straight away.
      final id = jsonId(jsonObject(response.data));
      if (id != null) {
        await widget.http.post<dynamic>('/api/v1/automations/$id/enable');
      }
      await _load();
      if (mounted) _show('Automation is on.');
    } on DioException catch (error) {
      if (mounted) {
        _show(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not save that automation.',
        );
      }
    } catch (_) {
      if (mounted) _show('Jarvis could not save that automation.');
    }
  }

  Future<String> _savedTimeZone() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/briefings/daily',
      );
      return asJsonString(jsonObject(response.data)?['timeZoneId']) ?? 'UTC';
    } catch (_) {
      return 'UTC';
    }
  }

  Future<void> _act(
    String id,
    Future<void> Function() action, {
    required String failure,
    String? success,
  }) async {
    setState(() => _busy.add(id));
    try {
      await action();
      if (mounted && success != null) _show(success);
    } on DioException catch (error) {
      if (mounted) {
        _show(firstProblemMessage(error.response?.data) ?? failure);
      }
    } catch (_) {
      if (mounted) _show(failure);
    } finally {
      if (mounted) setState(() => _busy.remove(id));
    }
    await _load();
  }

  Future<void> _setEnabled(String id, bool enabled) => _act(
    id,
    () => widget.http.post<dynamic>(
      '/api/v1/automations/$id/${enabled ? 'enable' : 'disable'}',
    ),
    failure: enabled
        ? 'Jarvis could not switch that automation on.'
        : 'Jarvis could not switch that automation off.',
  );

  Future<void> _run(String id, {required bool test}) => _act(
    id,
    () => widget.http.post<dynamic>(
      '/api/v1/automations/$id/${test ? 'test-run' : 'run'}',
    ),
    failure: 'Jarvis could not start that run.',
    success: test
        ? 'Test run started. Messages and agent tasks are skipped.'
        : 'Running now. The result appears here and in its chat.',
  );

  Future<void> _openChat(Map<String, dynamic> rule) async {
    final opener = widget.onOpenConversation;
    if (opener == null) return;
    var conversationId = asJsonString(rule['conversationId']);
    if (conversationId == null) {
      final id = jsonId(rule);
      if (id == null) return;
      try {
        final response = await widget.http.get<dynamic>(
          '/api/v1/automations/$id',
        );
        conversationId = asJsonString(
          jsonObject(response.data)?['conversationId'],
        );
      } catch (_) {
        if (mounted) _show('Jarvis could not open that automation chat.');
        return;
      }
    }
    if (conversationId == null) {
      if (mounted) _show('This automation does not have a chat yet.');
      return;
    }
    await opener(conversationId);
  }

  Future<void> _openApprovals() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => ApprovalsScreen(http: widget.http),
      ),
    );
    await _load();
  }

  Future<void> _showRuns(String id, String name) async {
    List<Map<String, dynamic>> runs;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/automations/$id/runs',
      );
      runs = jsonMaps(response.data);
    } catch (_) {
      if (mounted) _show('Jarvis could not load the run history.');
      return;
    }
    if (!mounted) return;
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (_) => _RunHistorySheet(title: name, runs: runs),
    );
  }

  void _show(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Automations'),
      actions: [
        HeaderAction(
          key: const Key('automations-studio'),
          label: 'Studio',
          icon: PhosphorIconsRegular.magicWand,
          collapsesWhenNarrow: true,
          onPressed: () async {
            await Navigator.of(context).push<void>(
              MaterialPageRoute(
                builder: (_) => AutomationStudioScreen(http: widget.http),
              ),
            );
            if (mounted) await _load();
          },
        ),
        const SizedBox(width: 8),
        HeaderAction(
          label: 'New',
          icon: PhosphorIconsRegular.plus,
          onPressed: _loading && _rules.isEmpty ? null : _create,
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _rules.isEmpty && _suggestions.isEmpty,
      onRetry: _load,
      onRefresh: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.lightning,
        title: 'No automations yet.',
        message:
            'Let Jarvis act on a schedule or when a reminder fires. Create one here, or describe it in chat.',
      ),
      child: ListView.builder(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
        itemCount: _rules.length + 1,
        itemBuilder: (context, index) {
          if (index == 0) {
            return ContentWidth(
              child: RoutineSuggestionsSection(
                suggestions: _suggestions,
                busy: _suggestionBusy,
                onCreate: _createFromSuggestion,
                onDismiss: _dismissSuggestion,
              ),
            );
          }
          return FadeSlideIn(
            index: index - 1,
            child: ContentWidth(child: _ruleCard(_rules[index - 1])),
          );
        },
      ),
    ),
  );

  Widget _ruleCard(Map<String, dynamic> rule) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final id = jsonId(rule) ?? '';
    final name = asJsonString(rule['name']) ?? 'Automation';
    final status = asJsonString(rule['status']) ?? 'draft';
    final enabled = status == 'enabled';
    final definition = jsonObject(rule['definition']) ?? const {};
    final trigger = jsonObject(definition['trigger']) ?? const {};
    final actions = jsonMaps(definition['actions']);
    final busy = _busy.contains(id);
    final lastRun = jsonObject(rule['lastRun']);
    final waiting = asJsonString(lastRun?['status']) == 'waiting_approval';

    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 8, 14),
      onTap: widget.onOpenConversation == null ? null : () => _openChat(rule),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(
                icon: triggerIcon(asJsonString(trigger['kind'])),
                color: enabled ? null : colors.muted,
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      name,
                      style: text.titleSmall?.copyWith(
                        fontSize: 15,
                        color: enabled ? null : colors.inkSoft,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      enabled
                          ? 'On'
                          : (status == 'draft' ? 'Draft · off' : 'Off'),
                      style: text.bodySmall?.copyWith(
                        color: enabled ? colors.success : colors.muted,
                      ),
                    ),
                  ],
                ),
              ),
              Switch(
                value: enabled,
                onChanged: busy || id.isEmpty
                    ? null
                    : (value) => _setEnabled(id, value),
              ),
              _menu(rule, id: id, name: name, enabled: enabled, busy: busy),
            ],
          ),
          const SizedBox(height: 10),
          _DetailLine(
            label: 'When',
            value: describeTrigger(trigger, deviceZone: _deviceZone),
          ),
          const SizedBox(height: 4),
          _DetailLine(
            label: 'Then',
            value: actions.isEmpty
                ? 'Nothing yet'
                : actions.map(describeAction).join(', then '),
          ),
          if (actions.any(actionNeedsApproval))
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Row(
                children: [
                  Icon(
                    PhosphorIconsRegular.shieldCheck,
                    size: 14,
                    color: colors.muted,
                  ),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text(
                      'Asks for your approval before sending or starting anything.',
                      style: text.bodySmall?.copyWith(color: colors.muted),
                    ),
                  ),
                ],
              ),
            ),
          const SizedBox(height: 10),
          _statusLine(rule, enabled: enabled, trigger: trigger),
          if (waiting)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: FilledButton.tonalIcon(
                onPressed: _openApprovals,
                style: FilledButton.styleFrom(minimumSize: const Size(0, 36)),
                icon: const Icon(PhosphorIconsRegular.shieldCheck, size: 16),
                label: const Text('Review approval'),
              ),
            ),
        ],
      ),
    );
  }

  /// Next planned run and what the latest run did, in one quiet line each.
  Widget _statusLine(
    Map<String, dynamic> rule, {
    required bool enabled,
    required Map<String, dynamic> trigger,
  }) {
    final colors = JarvisColors.of(context);
    final style = Theme.of(context).textTheme.bodySmall;
    final next = jsonDate(rule['nextRunAt'], local: true);
    final lastRun = jsonObject(rule['lastRun']);
    final kind = asJsonString(trigger['kind']);
    final String upcoming;
    if (!enabled) {
      upcoming = 'Switch it on to let it run.';
    } else if (next != null && next.isAfter(DateTime.now())) {
      upcoming =
          'Next run ${friendlyWhen(context, next)} · ${relativeFromNow(next)}';
    } else if (kind == 'schedule') {
      upcoming = 'Scheduling the next run…';
    } else if (kind == 'manual') {
      upcoming = 'Runs when you start it.';
    } else if (kind == 'event') {
      upcoming = 'Runs as soon as it happens.';
    } else {
      upcoming = 'Watching for its trigger.';
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(upcoming, style: style?.copyWith(color: colors.inkSoft)),
        if (lastRun != null) ...[
          const SizedBox(height: 4),
          Row(
            children: [
              Container(
                width: 6,
                height: 6,
                decoration: BoxDecoration(
                  color: runStatusColor(
                    context,
                    asJsonString(lastRun['status']),
                  ),
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  describeLastRun(context, lastRun),
                  style: style?.copyWith(color: colors.muted),
                ),
              ),
            ],
          ),
        ],
      ],
    );
  }

  Widget _menu(
    Map<String, dynamic> rule, {
    required String id,
    required String name,
    required bool enabled,
    required bool busy,
  }) => PopupMenuButton<String>(
    tooltip: 'Automation actions',
    enabled: !busy && id.isNotEmpty,
    icon: const Icon(PhosphorIconsRegular.dotsThree, size: 20),
    onSelected: (value) => switch (value) {
      'run' => _run(id, test: false),
      'test' => _run(id, test: true),
      'runs' => _showRuns(id, name),
      'chat' => _openChat(rule),
      _ => Future<void>.value(),
    },
    itemBuilder: (_) => [
      if (enabled) const PopupMenuItem(value: 'run', child: Text('Run now')),
      const PopupMenuItem(value: 'test', child: Text('Test run')),
      const PopupMenuItem(value: 'runs', child: Text('Run history')),
      if (widget.onOpenConversation != null)
        const PopupMenuItem(value: 'chat', child: Text('Open chat')),
    ],
  );
}

class _DetailLine extends StatelessWidget {
  const _DetailLine({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final style = Theme.of(context).textTheme.bodySmall;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 44,
          child: Text(label, style: style?.copyWith(color: colors.muted)),
        ),
        Expanded(
          child: Text(value, style: style?.copyWith(color: colors.ink)),
        ),
      ],
    );
  }
}

IconData triggerIcon(String? kind) => switch (kind) {
  'schedule' => PhosphorIconsRegular.clock,
  'reminder_due' => PhosphorIconsRegular.alarm,
  'device_battery' => PhosphorIconsRegular.batteryFull,
  'device_location' => PhosphorIconsRegular.mapPin,
  'calendar_window' => PhosphorIconsRegular.calendarBlank,
  'public_json_threshold' => PhosphorIconsRegular.pulse,
  'manual' => PhosphorIconsRegular.handTap,
  'event' => PhosphorIconsRegular.broadcast,
  _ => PhosphorIconsRegular.lightning,
};

/// The trigger in one plain sentence fragment, e.g. "Every weekday at 09:00".
String describeTrigger(Map<String, dynamic> trigger, {String? deviceZone}) {
  String time(dynamic raw) {
    final text = asJsonString(raw) ?? '';
    return text.length >= 5 ? text.substring(0, 5) : text;
  }

  switch (asJsonString(trigger['kind'])) {
    case 'schedule':
      final mask = asJsonInt(trigger['weekdays']);
      final days = mask == 0 || mask == 127
          ? 'Every day'
          : mask == 31
          ? 'Every weekday'
          : repeatLabel('weekly', mask) ?? 'Every week';
      final zone = asJsonString(trigger['timeZoneId']);
      final zoneNote = zone != null && deviceZone != null && zone != deviceZone
          ? ' (${zone.replaceAll('_', ' ')} time)'
          : '';
      return '$days at ${time(trigger['localTime'])}$zoneNote';
    case 'manual':
      return 'Only when you run it';
    case 'event':
      final filter = asJsonString(trigger['contains']);
      final base = switch (asJsonString(trigger['eventKind'])) {
        'webhook' => 'A webhook is called',
        'message_received' => 'A message arrives in a chat you read along with',
        'file_uploaded' => 'A file is uploaded',
        'task_completed' => 'A task finishes',
        'journal_saved' => 'A journal entry is saved',
        'expense_logged' => 'An expense is logged',
        'inbox_needs_reply' => 'A conversation needs a reply',
        _ => 'Something happens',
      };
      return filter == null || filter.isEmpty
          ? base
          : '$base and mentions “$filter”';
    case 'reminder_due':
      return trigger['reminderId'] == null
          ? 'Whenever a reminder goes off'
          : 'When a specific reminder goes off';
    case 'device_battery':
      final below = asJsonString(trigger['comparison']) != 'above';
      return 'Battery ${below ? 'drops below' : 'rises above'} '
          '${trigger['thresholdPercent']}%';
    case 'device_location':
      return asJsonString(trigger['comparison']) == 'outside'
          ? 'You leave a place'
          : 'You arrive at a place';
    case 'calendar_window':
      return '${trigger['minutesBefore']} min before a calendar event';
    case 'public_json_threshold':
      final above = asJsonString(trigger['comparison']) == 'above';
      return 'A web value goes ${above ? 'above' : 'below'} ${trigger['threshold']}';
    default:
      return 'Unknown trigger';
  }
}

String describeAction(Map<String, dynamic> action) {
  String quoted(String key) {
    final value = asJsonString(action[key])?.trim() ?? '';
    return value.isEmpty ? '' : ' “$value”';
  }

  return switch (asJsonString(action['kind'])) {
    'notification' => 'notify you${quoted('title')}',
    'task' => 'create a task${quoted('title')}',
    'agent_run' => 'start an agent task${quoted('title')}',
    'set_mode' =>
      asJsonString(action['mode']) == 'auto'
          ? 'let Jarvis choose the mode'
          : 'switch to ${asJsonString(action['mode']) ?? 'a'} mode',
    'channel_message' =>
      'send a message to ${asJsonString(action['recipient']) ?? 'a contact'}',
    _ => 'do something',
  }.replaceFirstMapped(RegExp('^.'), (match) => match[0]!.toUpperCase());
}

bool actionNeedsApproval(Map<String, dynamic> action) => const {
  'agent_run',
  'channel_message',
}.contains(asJsonString(action['kind']));

Color runStatusColor(BuildContext context, String? status) {
  final colors = JarvisColors.of(context);
  return switch (status) {
    'completed' => colors.success,
    'failed' => colors.danger,
    'waiting_approval' => colors.warning,
    'running' => colors.info,
    _ => colors.muted,
  };
}

/// "Last run Today 09:00: done", "Skipped Today 09:00: its conditions were
/// not met", "Waiting for your approval since …".
String describeLastRun(BuildContext context, Map<String, dynamic> run) {
  final started = jsonDate(run['startedAt'], local: true);
  final when = started == null ? '' : ' ${friendlyWhen(context, started)}';
  final test = run['testRun'] == true ? 'Test run' : 'Last run';
  final summary = asJsonString(run['failureSummary']);
  return switch (asJsonString(run['status'])) {
    'completed' => '$test$when: done',
    'running' => '$test$when: running',
    'waiting_approval' => 'Waiting for your approval since$when',
    'skipped' =>
      'Skipped$when${summary == null ? '' : ': ${_lowerFirst(summary)}'}',
    'failed' =>
      'Failed$when${summary == null ? '' : ': ${_lowerFirst(summary)}'}',
    'cancelled' => 'Cancelled$when',
    final other => '$test$when: ${other ?? 'unknown'}',
  };
}

String _lowerFirst(String value) =>
    value.isEmpty ? value : value[0].toLowerCase() + value.substring(1);

class _RunHistorySheet extends StatelessWidget {
  const _RunHistorySheet({required this.title, required this.runs});

  final String title;
  final List<Map<String, dynamic>> runs;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    return SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          minWidth: double.infinity,
          maxHeight: MediaQuery.sizeOf(context).height * .75,
        ),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(title, style: text.titleLarge),
              const SizedBox(height: 4),
              Text(
                'Recent runs',
                style: text.bodySmall?.copyWith(color: colors.muted),
              ),
              const SizedBox(height: 12),
              if (runs.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 24),
                  child: Text('No runs yet.', style: text.bodyMedium),
                )
              else
                Flexible(
                  child: ListView.separated(
                    shrinkWrap: true,
                    itemCount: runs.length,
                    separatorBuilder: (_, _) =>
                        Divider(height: 20, color: colors.outline),
                    itemBuilder: (_, index) => _RunRow(runs[index]),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _RunRow extends StatelessWidget {
  const _RunRow(this.run);

  final Map<String, dynamic> run;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final results = jsonMaps(run['actionResults']);
    final reason = asJsonString(run['triggerReason']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Container(
              width: 8,
              height: 8,
              decoration: BoxDecoration(
                color: runStatusColor(context, asJsonString(run['status'])),
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                describeLastRun(context, run),
                style: text.bodyMedium,
              ),
            ),
          ],
        ),
        if (reason != null && reason.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(left: 18, top: 2),
            child: Text(
              reason,
              style: text.bodySmall?.copyWith(color: colors.muted),
            ),
          ),
        for (final result in results)
          if (asJsonString(result['kind'] ?? result['Kind']) != 'run')
            Padding(
              padding: const EdgeInsets.only(left: 18, top: 2),
              child: Text(
                _describeResult(result),
                style: text.bodySmall?.copyWith(color: colors.inkSoft),
              ),
            ),
      ],
    );
  }

  static String _describeResult(Map<String, dynamic> result) {
    final kind = asJsonString(result['kind'] ?? result['Kind']) ?? 'step';
    final status = asJsonString(result['status'] ?? result['Status']) ?? '';
    final detail = asJsonString(result['detail'] ?? result['Detail']);
    final label = switch (kind) {
      'notification' => 'Notification',
      'task' => 'Task',
      'agent_run' => 'Agent task',
      'channel_message' => 'Message',
      'conditions' => 'Conditions',
      _ => kind,
    };
    final outcome = status.replaceAll('_', ' ');
    return detail == null || detail.isEmpty
        ? '$label: $outcome'
        : '$label: $outcome, $detail';
  }
}

class _NewAutomation {
  const _NewAutomation({
    required this.name,
    required this.localTime,
    required this.weekdays,
    required this.notificationTitle,
    required this.notificationBody,
  });

  final String name;
  final String localTime;
  final int weekdays;
  final String notificationTitle;
  final String notificationBody;
}

class _NewAutomationDialog extends StatefulWidget {
  const _NewAutomationDialog({required this.timeZoneId});

  final String timeZoneId;

  @override
  State<_NewAutomationDialog> createState() => _NewAutomationDialogState();
}

class _NewAutomationDialogState extends State<_NewAutomationDialog> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _title = TextEditingController();
  final _body = TextEditingController();
  TimeOfDay _time = const TimeOfDay(hour: 9, minute: 0);
  int _weekdays = 127;

  @override
  void dispose() {
    _name.dispose();
    _title.dispose();
    _body.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_weekdays == 0) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('Choose at least one day.')));
      return;
    }
    final name = _name.text.trim();
    final title = _title.text.trim();
    Navigator.pop(
      context,
      _NewAutomation(
        name: name,
        localTime:
            '${_time.hour.toString().padLeft(2, '0')}:${_time.minute.toString().padLeft(2, '0')}:00',
        weekdays: _weekdays,
        notificationTitle: title.isEmpty ? name : title,
        notificationBody: _body.text.trim(),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    return AlertDialog(
      title: const Text('New automation'),
      content: Form(
        key: _formKey,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextFormField(
                key: const Key('automation-name'),
                controller: _name,
                autofocus: true,
                maxLength: 120,
                decoration: const InputDecoration(labelText: 'Name'),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Give it a name.'
                    : null,
              ),
              const SizedBox(height: 8),
              Text(
                'When',
                style: text.labelMedium?.copyWith(color: colors.muted),
              ),
              const SizedBox(height: 8),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final day in weekdayNames)
                    FilterChip(
                      key: Key('automation-day-${day.$1}'),
                      label: Text(day.$2),
                      selected: _weekdays & day.$1 != 0,
                      onSelected: (selected) => setState(() {
                        _weekdays = selected
                            ? _weekdays | day.$1
                            : _weekdays & ~day.$1;
                      }),
                    ),
                ],
              ),
              const SizedBox(height: 10),
              ListTile(
                tileColor: colors.surfaceMuted,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                ),
                leading: const Icon(PhosphorIconsRegular.clock),
                trailing: const Icon(PhosphorIconsRegular.caretDown),
                title: Text(_time.format(context)),
                onTap: () async {
                  final value = await showTimePicker(
                    context: context,
                    initialTime: _time,
                  );
                  if (value != null && mounted) setState(() => _time = value);
                },
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 8, 4, 0),
                child: Text(
                  'Time zone: ${widget.timeZoneId.replaceAll('_', ' ')}',
                  style: text.bodySmall?.copyWith(color: colors.muted),
                ),
              ),
              const SizedBox(height: 16),
              Text(
                'Then notify me',
                style: text.labelMedium?.copyWith(color: colors.muted),
              ),
              TextFormField(
                key: const Key('automation-title'),
                controller: _title,
                maxLength: 120,
                decoration: const InputDecoration(
                  labelText: 'Title',
                  hintText: 'Defaults to the name',
                ),
              ),
              TextFormField(
                key: const Key('automation-body'),
                controller: _body,
                maxLength: 500,
                minLines: 1,
                maxLines: 3,
                decoration: const InputDecoration(labelText: 'Message'),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Write the message.'
                    : null,
              ),
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Cancel'),
        ),
        FilledButton(onPressed: _save, child: const Text('Create')),
      ],
    );
  }
}
