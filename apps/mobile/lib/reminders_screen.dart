import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'approvals_screen.dart';
import 'notification_details_screen.dart';
import 'task_details_screen.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

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
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final responses = await Future.wait([
        widget.http.get<List<dynamic>>('/api/v1/reminders'),
        widget.http.get<List<dynamic>>('/api/v1/notifications'),
      ]);
      if (mounted) {
        setState(() {
          _reminders = (responses[0].data ?? []).cast<Map<String, dynamic>>();
          _notifications = (responses[1].data ?? [])
              .cast<Map<String, dynamic>>();
        });
      }
    } on DioException {
      if (mounted) setState(() => _error = 'Jarvis could not load reminders.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _createReminder() async {
    final titleController = TextEditingController();
    DateTime selectedDate = DateTime.now().add(const Duration(days: 1));
    TimeOfDay selectedTime = const TimeOfDay(hour: 9, minute: 0);
    final formKey = GlobalKey<FormState>();
    final created = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('Create a reminder'),
          content: Form(
            key: formKey,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextFormField(
                  controller: titleController,
                  autofocus: true,
                  maxLength: 300,
                  decoration: const InputDecoration(
                    labelText: 'Remind me about',
                  ),
                  validator: (value) => value == null || value.trim().isEmpty
                      ? 'Enter a reminder.'
                      : null,
                ),
                const SizedBox(height: 10),
                ListTile(
                  tileColor: JarvisColors.surfaceMuted,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(JarvisRadii.md),
                  ),
                  leading: const Icon(Icons.calendar_today_outlined),
                  trailing: const Icon(Icons.expand_more_rounded),
                  title: Text(
                    MaterialLocalizations.of(
                      context,
                    ).formatFullDate(selectedDate),
                  ),
                  onTap: () async {
                    final value = await showDatePicker(
                      context: context,
                      initialDate: selectedDate,
                      firstDate: DateTime.now(),
                      lastDate: DateTime.now().add(const Duration(days: 730)),
                    );
                    if (value != null) {
                      setDialogState(() => selectedDate = value);
                    }
                  },
                ),
                const SizedBox(height: 8),
                ListTile(
                  tileColor: JarvisColors.surfaceMuted,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(JarvisRadii.md),
                  ),
                  leading: const Icon(Icons.schedule_rounded),
                  trailing: const Icon(Icons.expand_more_rounded),
                  title: Text(selectedTime.format(context)),
                  onTap: () async {
                    final value = await showTimePicker(
                      context: context,
                      initialTime: selectedTime,
                    );
                    if (value != null) {
                      setDialogState(() => selectedTime = value);
                    }
                  },
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                if (formKey.currentState?.validate() ?? false) {
                  Navigator.pop(dialogContext, true);
                }
              },
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
    if (created != true) {
      titleController.dispose();
      return;
    }

    final localDueAt = DateTime(
      selectedDate.year,
      selectedDate.month,
      selectedDate.day,
      selectedTime.hour,
      selectedTime.minute,
    );
    try {
      await widget.http.post(
        '/api/v1/reminders',
        data: {
          'title': titleController.text.trim(),
          'dueAt': localDueAt.toUtc().toIso8601String(),
        },
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The reminder service is unavailable. Try again shortly.'
            : 'Jarvis could not create that reminder.';
        _showError(message);
      }
    } finally {
      titleController.dispose();
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
      icon: Icons.notifications_off_outlined,
    );
    if (!confirmed) return;
    try {
      await widget.http.delete('/api/v1/reminders/${reminder['id']}');
      await _load();
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
    final type = notification['type'];
    final sourceId = notification['sourceId'] as String?;
    if (type == 'approval.required') {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => ApprovalsScreen(http: widget.http),
        ),
      );
      return;
    }
    if (type == 'task.completed' && sourceId != null) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) =>
              TaskDetailsScreen(http: widget.http, taskId: sourceId),
        ),
      );
      return;
    }
    if (sourceId == null ||
        type is! String ||
        (type != 'reminder.due' && type != 'task.completed')) {
      return;
    }
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => NotificationDetailsScreen(
          http: widget.http,
          notificationType: type,
          sourceId: sourceId,
        ),
      ),
    );
  }

  void _showError(String message) {
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  String _formatDate(dynamic raw) {
    final date = DateTime.tryParse(raw as String? ?? '')?.toLocal();
    if (date == null) return '';
    final dateLabel = MaterialLocalizations.of(context).formatMediumDate(date);
    final timeLabel = MaterialLocalizations.of(
      context,
    ).formatTimeOfDay(TimeOfDay.fromDateTime(date));
    return '$dateLabel · $timeLabel';
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
          icon: const Icon(Icons.refresh_rounded),
        ),
        const SizedBox(width: 8),
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
                            color: JarvisColors.accent,
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
    floatingActionButton: FloatingActionButton.extended(
      onPressed: _createReminder,
      icon: const Icon(Icons.add_alert_outlined),
      label: const Text('New reminder'),
    ),
    body: _loading
        ? const LoadingState()
        : _error != null
        ? ErrorState(message: _error!, onRetry: _load)
        : TabBarView(
            controller: _tabs,
            children: [_buildReminders(), _buildNotifications()],
          ),
  );

  int get _unreadCount =>
      _notifications.where((item) => item['readAt'] == null).length;

  Widget _buildReminders() => _reminders.isEmpty
      ? const EmptyState(
          icon: Icons.alarm_rounded,
          title: 'No reminders yet.',
          message: 'Ask Jarvis to remind you, or create one here.',
        )
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 104),
          itemCount: _reminders.length,
          itemBuilder: (context, index) {
            final reminder = _reminders[index];
            final status = reminder['status'] as String? ?? 'pending';
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
                          ? Icons.alarm_rounded
                          : Icons.alarm_off_rounded,
                      color: pending ? JarvisColors.rose : JarvisColors.muted,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            reminder['title'] as String? ?? '',
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
                                _formatDate(reminder['dueAt']),
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
                        icon: const Icon(Icons.close_rounded, size: 20),
                      ),
                  ],
                ),
              ),
            );
          },
        );

  IconData _notificationIcon(Object? type) => switch (type) {
    'reminder.due' => Icons.alarm_rounded,
    'task.completed' => Icons.task_alt_rounded,
    'approval.required' => Icons.shield_outlined,
    'watch.triggered' || 'watch.failed' => Icons.monitor_heart_outlined,
    _ => Icons.notifications_none_rounded,
  };

  Widget _buildNotifications() => _notifications.isEmpty
      ? const EmptyState(
          icon: Icons.notifications_none_rounded,
          title: 'No notifications yet.',
          message: 'Alerts from reminders, tasks, and watches will show here.',
        )
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 104),
          itemCount: _notifications.length,
          itemBuilder: (context, index) {
            final notification = _notifications[index];
            final unread = notification['readAt'] == null;
            final body = notification['body'] as String? ?? '';
            return ContentWidth(
              child: SurfaceCard(
                margin: const EdgeInsets.only(bottom: 10),
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
                color: unread ? JarvisColors.surface : JarvisColors.canvas,
                borderColor: unread
                    ? JarvisColors.accent.withValues(alpha: .25)
                    : JarvisColors.outline,
                onTap: () => _openNotification(notification),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    IconBadge(
                      icon: _notificationIcon(notification['type']),
                      color: unread ? JarvisColors.accent : JarvisColors.muted,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            notification['title'] as String? ?? '',
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
