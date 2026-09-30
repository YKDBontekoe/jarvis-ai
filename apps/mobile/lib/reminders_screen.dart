import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import 'ui/phosphor_icons.dart';

import 'approvals_screen.dart';
import 'features/chat/tool_catalog.dart';
import 'api/api_config.dart';
import 'features/coding/coding_run_detail_screen.dart';
import 'daily_briefing_screen.dart';
import 'notification_details_screen.dart';
import 'notification_routing.dart';
import 'json_maps.dart';
import 'task_details_screen.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

part 'reminder_editor.dart';

const _weekdayChoices = [
  (1, 'Mon'),
  (2, 'Tue'),
  (4, 'Wed'),
  (8, 'Thu'),
  (16, 'Fri'),
  (32, 'Sat'),
  (64, 'Sun'),
];

enum RemindersTab { reminders, notifications }

class RemindersScreen extends StatefulWidget {
  const RemindersScreen({
    required this.http,
    this.onOpenConversation,
    this.initialTab = RemindersTab.reminders,
    super.key,
  });

  final Dio http;
  final RemindersTab initialTab;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<RemindersScreen> createState() => _RemindersScreenState();
}

class _RemindersScreenState extends State<RemindersScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabs = TabController(
    length: 2,
    vsync: this,
    initialIndex: widget.initialTab.index,
  );
  List<Map<String, dynamic>> _reminders = [];
  List<Map<String, dynamic>> _notifications = [];
  // Pending approvals keyed by id, so an approval notification can be decided in place.
  Map<String, Map<String, dynamic>> _pendingApprovals = {};
  final Set<String> _decidingApprovals = {};
  bool _loading = true;
  bool _remindersFailed = false;
  bool _notificationsFailed = false;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
      _remindersFailed = false;
      _notificationsFailed = false;
    });
    try {
      var remindersFailed = false;
      var notificationsFailed = false;
      try {
        final reminders = await widget.http.get<dynamic>('/api/v1/reminders');
        if (mounted && revision == _requestRevision) {
          setState(() => _reminders = jsonMaps(reminders.data));
        }
      } on DioException {
        remindersFailed = true;
      } catch (_) {
        remindersFailed = true;
      }
      try {
        final notifications = await widget.http.get<dynamic>(
          '/api/v1/notifications',
        );
        if (mounted && revision == _requestRevision) {
          setState(() => _notifications = jsonMaps(notifications.data));
        }
      } on DioException {
        notificationsFailed = true;
      } catch (_) {
        notificationsFailed = true;
      }
      try {
        final approvals = await widget.http.get<dynamic>('/api/v1/approvals');
        if (mounted && revision == _requestRevision) {
          setState(
            () => _pendingApprovals = {
              for (final approval in jsonMaps(approvals.data))
                if (approval['status'] == 'pending' && jsonId(approval) != null)
                  jsonId(approval)!: approval,
            },
          );
        }
      } on DioException {
        // Approvals still open from the notification; this only hides the shortcut.
      } catch (_) {
        // Same: a malformed list never blocks the inbox.
      }
      if (mounted && revision == _requestRevision) {
        setState(() {
          _remindersFailed = remindersFailed;
          _notificationsFailed = notificationsFailed;
          _error = remindersFailed
              ? 'Jarvis could not load reminders.'
              : notificationsFailed
              ? 'Jarvis could not load notifications.'
              : null;
        });
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _createReminder() async {
    var timeZoneId = 'UTC';
    try {
      final briefing = await widget.http.get<dynamic>(
        '/api/v1/briefings/daily',
      );
      timeZoneId =
          asJsonString(jsonObject(briefing.data)?['timeZoneId']) ?? 'UTC';
    } on DioException {
      // Keep UTC when briefing settings are unavailable.
    } catch (_) {
      // Keep UTC when briefing settings are malformed.
    }
    if (!mounted) return;
    final created = await showDialog<_NewReminder>(
      context: context,
      builder: (_) => const _NewReminderDialog(),
    );
    if (created == null || !mounted) return;

    final localDueAt = DateTime(
      created.date.year,
      created.date.month,
      created.date.day,
      created.time.hour,
      created.time.minute,
    );
    if (!localDueAt.isAfter(DateTime.now()) && created.recurrence == 'once') {
      _showError('Choose a time in the future.');
      return;
    }
    final localTime =
        '${created.time.hour.toString().padLeft(2, '0')}:${created.time.minute.toString().padLeft(2, '0')}:00';
    try {
      await widget.http.post(
        '/api/v1/reminders',
        data: {
          'title': created.title,
          'dueAt': localDueAt.toUtc().toIso8601String(),
          if (created.recurrence != 'once') 'recurrence': created.recurrence,
          if (created.recurrence == 'weekly') 'weekdays': created.weekdays,
          'timeZoneId': timeZoneId,
          'localTime': localTime,
        },
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The reminder service is unavailable. Try again shortly.'
            : firstProblemMessage(error.response?.data) ??
                  'Jarvis could not create that reminder.';
        _showError(message);
      }
    } catch (_) {
      if (mounted) _showError('Jarvis could not create that reminder.');
    }
  }

  Future<void> _cancelReminder(Map<String, dynamic> reminder) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Cancel reminder?',
      message: '“${reminder['title']}” will no longer notify you.',
      cancelLabel: 'Keep',
      confirmLabel: 'Cancel reminder',
      destructive: true,
      icon: PhosphorIconsRegular.bellSlash,
    );
    if (!confirmed) return;
    if (!mounted) return;
    final id = jsonId(reminder);
    if (id == null) return;
    try {
      await widget.http.delete('/api/v1/reminders/$id');
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not cancel that reminder.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not cancel that reminder.');
    }
  }

  Future<void> _openChat(Map<String, dynamic> reminder) async {
    final opener = widget.onOpenConversation;
    if (opener == null) return;
    var conversationId = asJsonString(reminder['conversationId']);
    if (conversationId == null) {
      final id = jsonId(reminder);
      if (id == null) return;
      try {
        final response = await widget.http.get<dynamic>('/api/v1/reminders/$id');
        conversationId = asJsonString(jsonObject(response.data)?['conversationId']);
      } on DioException {
        if (mounted) _showError('Jarvis could not open that reminder chat.');
        return;
      } catch (_) {
        if (mounted) _showError('Jarvis could not open that reminder chat.');
        return;
      }
    }
    if (conversationId == null) {
      if (mounted) _showError('This reminder does not have a chat yet.');
      return;
    }
    await opener(conversationId);
  }

  Future<void> _markRead(Map<String, dynamic> notification) async {
    if (notification['readAt'] != null) return;
    final id = jsonId(notification);
    if (id == null) return;
    try {
      await widget.http.post('/api/v1/notifications/$id/read');
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not update that notification.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not update that notification.');
    }
  }

  Future<void> _decideApproval(String approvalId, bool approved) async {
    setState(() => _decidingApprovals.add(approvalId));
    try {
      // Resuming the paused agent run can take a while, so wait for it.
      await widget.http.post<dynamic>(
        '/api/v1/approvals/$approvalId/decision',
        data: {'approved': approved},
        options: longRunningOptions(),
      );
    } on DioException catch (error) {
      if (mounted) {
        _showError(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not record that decision.',
        );
      }
    } catch (_) {
      if (mounted) _showError('Jarvis could not record that decision.');
    }
    if (!mounted) return;
    setState(() => _decidingApprovals.remove(approvalId));
    await _load();
  }

  Widget _approvalActions(Map<String, dynamic> approval) {
    final id = jsonId(approval)!;
    final busy = _decidingApprovals.contains(id);
    final tool = describeTool(asJsonString(approval['toolName']) ?? '');
    return Padding(
      padding: const EdgeInsets.only(top: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Wants to: ${tool.active.toLowerCase()}',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: JarvisColors.of(context).ink,
              fontWeight: FontWeight.w500,
            ),
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              OutlinedButton(
                onPressed: busy ? null : () => _decideApproval(id, false),
                style: OutlinedButton.styleFrom(minimumSize: const Size(0, 38)),
                child: const Text('Decline'),
              ),
              const SizedBox(width: 8),
              FilledButton.icon(
                onPressed: busy ? null : () => _decideApproval(id, true),
                style: FilledButton.styleFrom(minimumSize: const Size(0, 38)),
                icon: busy
                    ? const SizedBox.square(
                        dimension: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(PhosphorIconsRegular.check, size: 18),
                label: Text(busy ? 'Working…' : 'Approve'),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Future<void> _markAllRead() async {
    final unread = _notifications.where((item) => item['readAt'] == null);
    final ids = [for (final item in unread) ?jsonId(item)];
    if (ids.isEmpty) return;
    try {
      await Future.wait([
        for (final id in ids) widget.http.post('/api/v1/notifications/$id/read'),
      ]);
    } on DioException {
      if (mounted) _showError('Jarvis could not mark everything as read.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not mark everything as read.');
    }
    if (mounted) await _load();
  }

  Future<void> _openNotification(Map<String, dynamic> notification) async {
    await _markRead(notification);
    if (!mounted) return;
    final type = asJsonString(notification['type']);
    final sourceId = asJsonString(notification['sourceId']);
    if (opensApprovalScreen(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => ApprovalsScreen(http: widget.http),
        ),
      );
    } else if (opensCodingRun(type) && sourceId != null) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => CodingRunDetailScreen(http: widget.http, runId: sourceId),
        ),
      );
    } else if (opensDailyBriefing(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => DailyBriefingScreen(http: widget.http),
        ),
      );
    } else if (opensTaskDetails(type) && sourceId != null) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) =>
              TaskDetailsScreen(http: widget.http, taskId: sourceId),
        ),
      );
    } else if ((type == 'reminder.due' || type == 'reminder.failed') &&
        widget.onOpenConversation != null &&
        sourceId != null) {
      try {
        final response = await widget.http.get<dynamic>('/api/v1/reminders/$sourceId');
        final conversationId =
            asJsonString(jsonObject(response.data)?['conversationId']);
        if (conversationId != null) {
          await widget.onOpenConversation!(conversationId);
        } else if (mounted && opensNotificationDetails(type)) {
          await Navigator.of(context).push<void>(
            MaterialPageRoute<void>(
              builder: (_) => NotificationDetailsScreen(
                http: widget.http,
                notificationType: type!,
                sourceId: sourceId,
              ),
            ),
          );
        }
      } on DioException {
        if (mounted && opensNotificationDetails(type)) {
          await Navigator.of(context).push<void>(
            MaterialPageRoute<void>(
              builder: (_) => NotificationDetailsScreen(
                http: widget.http,
                notificationType: type!,
                sourceId: sourceId,
              ),
            ),
          );
        }
      }
    } else if (sourceId != null && opensNotificationDetails(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => NotificationDetailsScreen(
            http: widget.http,
            notificationType: type!,
            sourceId: sourceId,
          ),
        ),
      );
    } else {
      return;
    }
    if (mounted) await _load();
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  String _formatDate(dynamic raw) {
    final date = jsonDate(raw, local: true);
    if (date == null) return '';
    final dateLabel = MaterialLocalizations.of(context).formatMediumDate(date);
    final timeLabel = MaterialLocalizations.of(context)
        .formatTimeOfDay(TimeOfDay.fromDateTime(date));
    return '$dateLabel · $timeLabel';
  }

  String _scheduleLabel(Map<String, dynamic> reminder) {
    final next = _formatDate(reminder['dueAt']);
    return switch (asJsonString(reminder['recurrence'])) {
      'daily' => 'Every day · next $next',
      'weekdays' => 'Weekdays · next $next',
      'weekly' => 'Weekly · next $next',
      _ => next,
    };
  }

  @override
  void dispose() {
    _tabs.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Reminders'),
      actions: [
        if (_unreadCount > 0)
          IconButton(
            onPressed: _markAllRead,
            tooltip: 'Mark all as read',
            icon: const Icon(PhosphorIconsRegular.checkCircle),
          ),
        IconButton(
          onPressed: _load,
          tooltip: 'Refresh',
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        HeaderAction(
          label: 'New',
          icon: PhosphorIconsRegular.plus,
          onPressed: _createReminder,
        ),
      ],
      bottom: PreferredSize(
        preferredSize: const Size.fromHeight(56),
        child: ContentWidth(
          child: Container(
            constraints: const BoxConstraints(minHeight: 44),
            margin: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            padding: const EdgeInsets.all(4),
            decoration: BoxDecoration(
              color: JarvisColors.of(context).surfaceRaised,
              borderRadius: BorderRadius.circular(JarvisRadii.md),
            ),
            child: TabBar(
              controller: _tabs,
              tabs: [
                const Tab(
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: Text('Reminders'),
                  ),
                ),
                Tab(
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Text('Notifications'),
                      if (_unreadCount > 0) ...[
                        const SizedBox(width: 6),
                        Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 6,
                            vertical: 1,
                          ),
                          decoration: BoxDecoration(
                            color: JarvisColors.of(context).ink,
                            borderRadius: BorderRadius.circular(20),
                          ),
                          child: Text(
                            '$_unreadCount',
                            style: TextStyle(
                              color: JarvisColors.of(context).onInk,
                              fontSize: 11,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                      ],
                    ],
                  ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
    body: ListScreenBody(
      loading: _loading,
      error: (_reminders.isNotEmpty || _notifications.isNotEmpty)
          ? _error
          : null,
      isEmpty: false,
      onRetry: _load,
      empty: const SizedBox.shrink(),
      child: TabBarView(
        controller: _tabs,
        children: [_buildReminders(), _buildNotifications()],
      ),
    ),
  );

  /// Pending reminders first (overdue, today, upcoming) then finished ones,
  /// each under a small heading so the list reads as a timeline.
  List<Object> get _reminderRows {
    final now = DateTime.now();
    final startOfToday = DateTime(now.year, now.month, now.day);
    final endOfToday = startOfToday.add(const Duration(days: 1));
    final buckets = <String, List<Map<String, dynamic>>>{
      'Overdue': [],
      'Today': [],
      'Upcoming': [],
      'Finished': [],
    };
    final sorted = [..._reminders]
      ..sort((a, b) {
        final left = jsonDate(a['dueAt']);
        final right = jsonDate(b['dueAt']);
        if (left == null || right == null) return 0;
        return left.compareTo(right);
      });
    for (final reminder in sorted) {
      final due = jsonDate(reminder['dueAt'], local: true);
      final pending = (asJsonString(reminder['status']) ?? 'pending') == 'pending';
      final key = !pending
          ? 'Finished'
          : due == null || !due.isBefore(endOfToday)
          ? 'Upcoming'
          : due.isBefore(now)
          ? 'Overdue'
          : 'Today';
      buckets[key]!.add(reminder);
    }
    return [
      for (final entry in buckets.entries)
        if (entry.value.isNotEmpty) ...[entry.key, ...entry.value],
    ];
  }

  /// Notifications grouped by the day they arrived, newest first.
  List<Object> get _notificationRows {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    String label(DateTime? date) {
      if (date == null) return 'Earlier';
      final day = DateTime(date.year, date.month, date.day);
      final days = today.difference(day).inDays;
      if (days <= 0) return 'Today';
      if (days == 1) return 'Yesterday';
      if (days < 7) return 'This week';
      return 'Earlier';
    }

    final sorted = [..._notifications]
      ..sort((a, b) {
        final left = jsonDate(a['createdAt']);
        final right = jsonDate(b['createdAt']);
        if (left == null || right == null) return 0;
        return right.compareTo(left);
      });
    final rows = <Object>[];
    String? current;
    for (final notification in sorted) {
      final heading = label(jsonDate(notification['createdAt'], local: true));
      if (heading != current) {
        rows.add(heading);
        current = heading;
      }
      rows.add(notification);
    }
    return rows;
  }

  int get _unreadCount =>
      _notifications.where((item) => item['readAt'] == null).length;

  Widget _buildReminders() => _remindersFailed && _reminders.isEmpty
      ? ErrorState(message: 'Jarvis could not load reminders.', onRetry: _load)
      : _reminders.isEmpty
      ? const EmptyState(
          icon: PhosphorIconsRegular.alarm,
          title: 'No reminders yet.',
          message: 'Ask Jarvis to remind you, or create one here.',
        )
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          itemCount: _reminderRows.length,
          itemBuilder: (context, index) {
            final row = _reminderRows[index];
            if (row is String) return _GroupHeader(row);
            final reminder = row as Map<String, dynamic>;
            final status = asJsonString(reminder['status']) ?? 'pending';
            final pending = status == 'pending';
            final style = statusStyle(status);
            return FadeSlideIn(
              index: index,
              child: ContentWidth(
              child: SurfaceCard(
                margin: const EdgeInsets.only(bottom: 10),
                padding: const EdgeInsets.fromLTRB(16, 14, 6, 14),
                onTap: widget.onOpenConversation == null
                    ? null
                    : () => _openChat(reminder),
                child: Row(
                  children: [
                    IconBadge(
                      icon: pending
                          ? PhosphorIconsRegular.alarm
                          : PhosphorIconsRegular.bellSlash,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            asJsonString(reminder['title']) ?? '',
                            style: Theme.of(context).textTheme.titleSmall
                                ?.copyWith(fontSize: 15),
                          ),
                          const SizedBox(height: 6),
                          Wrap(
                            spacing: 8,
                            runSpacing: 6,
                            crossAxisAlignment: WrapCrossAlignment.center,
                            children: [
                              StatusPill(
                                label: style.label,
                                color: style.color,
                              ),
                              Text(
                                _scheduleLabel(reminder),
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                            ],
                          ),
                        ],
                      ),
                    ),
                    if (widget.onOpenConversation != null)
                      IconButton(
                        tooltip: 'Open chat',
                        onPressed: () => _openChat(reminder),
                        icon: const Icon(PhosphorIconsRegular.chatCircle, size: 20),
                      ),
                    if (pending)
                      IconButton(
                        tooltip: 'Cancel reminder',
                        onPressed: () => _cancelReminder(reminder),
                        icon: const Icon(PhosphorIconsRegular.x, size: 20),
                      ),
                  ],
                ),
              ),
              ),
            );
          },
        );

  IconData _notificationIcon(Object? type) => switch (type) {
    'reminder.due' || 'reminder.failed' => PhosphorIconsRegular.alarm,
    'task.completed' => PhosphorIconsRegular.checkCircle,
    'task.failed' => PhosphorIconsRegular.warningCircle,
    'approval.required' => PhosphorIconsRegular.shieldCheck,
    'coding.pr.ready' || 'coding.run.ready' => PhosphorIconsRegular.code,
    'watch.triggered' || 'watch.failed' => PhosphorIconsRegular.pulse,
    _ => PhosphorIconsRegular.bell,
  };

  Widget _buildNotifications() => _notificationsFailed && _notifications.isEmpty
      ? ErrorState(
          message: 'Jarvis could not load notifications.',
          onRetry: _load,
        )
      : _notifications.isEmpty
      ? const EmptyState(
          icon: PhosphorIconsRegular.bell,
          title: 'No notifications yet.',
          message: 'Alerts from reminders, tasks, and watches will show here.',
        )
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          itemCount: _notificationRows.length,
          itemBuilder: (context, index) {
            final row = _notificationRows[index];
            if (row is String) return _GroupHeader(row);
            final notification = row as Map<String, dynamic>;
            final unread = notification['readAt'] == null;
            final body = asJsonString(notification['body']) ?? '';
            return FadeSlideIn(
              index: index,
              child: ContentWidth(
              child: SurfaceCard(
                margin: const EdgeInsets.only(bottom: 10),
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
                color: unread
                    ? JarvisColors.of(context).surface
                    : JarvisColors.of(context).canvas,
                borderColor: JarvisColors.of(context).outline,
                onTap: () => _openNotification(notification),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    IconBadge(icon: _notificationIcon(notification['type'])),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            asJsonString(notification['title']) ?? '',
                            style: Theme.of(context).textTheme.titleSmall
                                ?.copyWith(
                                  fontSize: 15,
                                  fontWeight: unread
                                      ? FontWeight.w700
                                      : FontWeight.w500,
                                ),
                          ),
                          if (body.isNotEmpty) ...[
                            const SizedBox(height: 4),
                            Text(
                              body,
                              style: TextStyle(
                                fontSize: 13.5,
                                height: 1.4,
                                color: JarvisColors.of(context).inkSoft,
                              ),
                            ),
                          ],
                          if (asJsonString(notification['type']) == 'approval.required' &&
                              _pendingApprovals[asJsonString(notification['sourceId'])] != null)
                            _approvalActions(
                              _pendingApprovals[asJsonString(notification['sourceId'])]!,
                            ),
                          const SizedBox(height: 6),
                          Text(
                            _formatDate(notification['createdAt']),
                            style: Theme.of(context).textTheme.bodySmall
                                ?.copyWith(
                                  color: JarvisColors.of(context).muted,
                                ),
                          ),
                        ],
                      ),
                    ),
                    if (unread)
                      Container(
                        margin: const EdgeInsets.only(top: 6, left: 8),
                        width: 9,
                        height: 9,
                        decoration: BoxDecoration(
                          color: JarvisColors.of(context).accent,
                          shape: BoxShape.circle,
                        ),
                      ),
                  ],
                ),
              ),
              ),
            );
          },
        );
}

/// Small caption above a run of rows in a grouped list.
class _GroupHeader extends StatelessWidget {
  const _GroupHeader(this.label);

  final String label;

  @override
  Widget build(BuildContext context) => ContentWidth(
    child: Padding(
      padding: const EdgeInsets.fromLTRB(4, 10, 0, 8),
      child: SizedBox(
        width: double.infinity,
        child: Text(
        label,
        style: Theme.of(context).textTheme.labelMedium?.copyWith(
          color: label == 'Overdue'
              ? JarvisColors.of(context).danger
              : JarvisColors.of(context).muted,
          letterSpacing: .3,
        ),
      ),
      ),
    ),
  );
}
