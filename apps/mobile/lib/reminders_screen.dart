import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'approvals_screen.dart';
import 'daily_briefing_screen.dart';
import 'notification_details_screen.dart';
import 'notification_routing.dart';
import 'json_maps.dart';
import 'task_details_screen.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

const _weekdayChoices = [
  (1, 'Mon'),
  (2, 'Tue'),
  (4, 'Wed'),
  (8, 'Thu'),
  (16, 'Fri'),
  (32, 'Sat'),
  (64, 'Sun'),
];

class RemindersScreen extends StatefulWidget {
  const RemindersScreen({required this.http, super.key});

  final Dio http;

  @override
  State<RemindersScreen> createState() => _RemindersScreenState();
}

class _RemindersScreenState extends State<RemindersScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabs = TabController(length: 2, vsync: this);
  List<Map<String, dynamic>> _reminders = [];
  List<Map<String, dynamic>> _notifications = [];
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
        final reminders = await widget.http.get<List<dynamic>>('/api/v1/reminders');
        if (mounted && revision == _requestRevision) {
          setState(() => _reminders = jsonMaps(reminders.data));
        }
      } on DioException {
        remindersFailed = true;
      }
      try {
        final notifications = await widget.http.get<List<dynamic>>(
          '/api/v1/notifications',
        );
        if (mounted && revision == _requestRevision) {
          setState(() => _notifications = jsonMaps(notifications.data));
        }
      } on DioException {
        notificationsFailed = true;
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
      final briefing = await widget.http.get<Map<String, dynamic>>(
        '/api/v1/briefings/daily',
      );
      timeZoneId = asJsonString(briefing.data?['timeZoneId']) ?? 'UTC';
    } on DioException {
      // Keep UTC when briefing settings are unavailable.
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
    try {
      await widget.http.delete('/api/v1/reminders/${reminder['id']}');
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not cancel that reminder.');
    }
  }

  Future<void> _markRead(Map<String, dynamic> notification) async {
    if (notification['readAt'] != null) return;
    try {
      await widget.http.post(
        '/api/v1/notifications/${notification['id']}/read',
      );
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not update that notification.');
    }
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
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  String _formatDate(dynamic raw) {
    final date = DateTime.tryParse(asJsonString(raw) ?? '')?.toLocal();
    if (date == null) return '';
    final dateLabel = MaterialLocalizations.of(context).formatMediumDate(date);
    final timeLabel = MaterialLocalizations.of(
      context,
    ).formatTimeOfDay(TimeOfDay.fromDateTime(date));
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
            height: 44,
            margin: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            padding: const EdgeInsets.all(4),
            decoration: BoxDecoration(
              color: JarvisColors.surfaceRaised,
              borderRadius: BorderRadius.circular(JarvisRadii.md),
            ),
            child: TabBar(
              controller: _tabs,
              tabs: [
                const Tab(text: 'Reminders'),
                Tab(
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
                            color: JarvisColors.ink,
                            borderRadius: BorderRadius.circular(20),
                          ),
                          child: Text(
                            '$_unreadCount',
                            style: const TextStyle(
                              color: Colors.white,
                              fontSize: 11,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                      ],
                    ],
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

  int get _unreadCount =>
      _notifications.where((item) => item['readAt'] == null).length;

  Widget _buildReminders() => _remindersFailed && _reminders.isEmpty
      ? ErrorState(
          message: 'Jarvis could not load reminders.',
          onRetry: _load,
        )
      : _reminders.isEmpty
      ? const EmptyState(
          icon: PhosphorIconsRegular.alarm,
          title: 'No reminders yet.',
          message: 'Ask Jarvis to remind you, or create one here.',
        )
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          itemCount: _reminders.length,
          itemBuilder: (context, index) {
            final reminder = _reminders[index];
            final status = asJsonString(reminder['status']) ?? 'pending';
            final pending = status == 'pending';
            final style = statusStyle(status);
            return ContentWidth(
              child: SurfaceCard(
                margin: const EdgeInsets.only(bottom: 10),
                padding: const EdgeInsets.fromLTRB(16, 14, 6, 14),
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
                            style: Theme.of(
                              context,
                            ).textTheme.titleSmall?.copyWith(fontSize: 15),
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
                    if (pending)
                      IconButton(
                        tooltip: 'Cancel reminder',
                        onPressed: () => _cancelReminder(reminder),
                        icon: const Icon(PhosphorIconsRegular.x, size: 20),
                      ),
                  ],
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
          itemCount: _notifications.length,
          itemBuilder: (context, index) {
            final notification = _notifications[index];
            final unread = notification['readAt'] == null;
            final body = asJsonString(notification['body']) ?? '';
            return ContentWidth(
              child: SurfaceCard(
                margin: const EdgeInsets.only(bottom: 10),
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
                color: unread ? JarvisColors.surface : JarvisColors.canvas,
                borderColor: JarvisColors.outline,
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
                              style: const TextStyle(
                                fontSize: 13.5,
                                height: 1.4,
                                color: JarvisColors.inkSoft,
                              ),
                            ),
                          ],
                          const SizedBox(height: 6),
                          Text(
                            _formatDate(notification['createdAt']),
                            style: Theme.of(context).textTheme.bodySmall
                                ?.copyWith(color: JarvisColors.muted),
                          ),
                        ],
                      ),
                    ),
                    if (unread)
                      Container(
                        margin: const EdgeInsets.only(top: 6, left: 8),
                        width: 9,
                        height: 9,
                        decoration: const BoxDecoration(
                          color: JarvisColors.accent,
                          shape: BoxShape.circle,
                        ),
                      ),
                  ],
                ),
              ),
            );
          },
        );
}

class _NewReminder {
  const _NewReminder({
    required this.title,
    required this.date,
    required this.time,
    required this.recurrence,
    required this.weekdays,
  });

  final String title;
  final DateTime date;
  final TimeOfDay time;
  final String recurrence;
  final int weekdays;
}

class _NewReminderDialog extends StatefulWidget {
  const _NewReminderDialog();

  @override
  State<_NewReminderDialog> createState() => _NewReminderDialogState();
}

class _NewReminderDialogState extends State<_NewReminderDialog> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  DateTime _date = DateTime.now().add(const Duration(days: 1));
  TimeOfDay _time = const TimeOfDay(hour: 9, minute: 0);
  String _recurrence = 'once';
  int _weekdays = 0;

  @override
  void dispose() {
    _title.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_recurrence == 'weekly' && _weekdays == 0) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Choose at least one weekday.')),
      );
      return;
    }
    Navigator.pop(
      context,
      _NewReminder(
        title: _title.text.trim(),
        date: _date,
        time: _time,
        recurrence: _recurrence,
        weekdays: _weekdays,
      ),
    );
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Create a reminder'),
    content: Form(
      key: _formKey,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextFormField(
              controller: _title,
              autofocus: true,
              maxLength: 300,
              decoration: const InputDecoration(labelText: 'Remind me about'),
              validator: (value) =>
                  value == null || value.trim().isEmpty ? 'Enter a reminder.' : null,
            ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final option in const [
                  ('once', 'Once'),
                  ('daily', 'Daily'),
                  ('weekdays', 'Weekdays'),
                  ('weekly', 'Weekly'),
                ])
                  ChoiceChip(
                    key: Key('recurrence-${option.$1}'),
                    label: Text(option.$2),
                    selected: _recurrence == option.$1,
                    onSelected: (_) => setState(() => _recurrence = option.$1),
                  ),
              ],
            ),
            if (_recurrence == 'weekly') ...[
              const SizedBox(height: 10),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final day in _weekdayChoices)
                    FilterChip(
                      key: Key('weekday-${day.$1}'),
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
            ],
            const SizedBox(height: 10),
            ListTile(
              tileColor: JarvisColors.surfaceMuted,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(JarvisRadii.md),
              ),
              leading: const Icon(PhosphorIconsRegular.calendarBlank),
              trailing: const Icon(PhosphorIconsRegular.caretDown),
              title: Text(
                MaterialLocalizations.of(context).formatFullDate(_date),
              ),
              onTap: () async {
                final value = await showDatePicker(
                  context: context,
                  initialDate: _date,
                  firstDate: DateTime.now(),
                  lastDate: DateTime.now().add(const Duration(days: 730)),
                );
                if (value != null) setState(() => _date = value);
              },
            ),
            const SizedBox(height: 8),
            ListTile(
              tileColor: JarvisColors.surfaceMuted,
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
                if (value != null) setState(() => _time = value);
              },
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
      FilledButton(onPressed: _save, child: const Text('Save')),
    ],
  );
}

