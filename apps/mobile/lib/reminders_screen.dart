import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'approvals_screen.dart';
import 'notification_details_screen.dart';
import 'task_details_screen.dart';

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
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.calendar_today_outlined),
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
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.schedule),
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
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Cancel reminder?'),
        content: Text('“${reminder['title']}” will no longer notify you.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Cancel reminder'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
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
          icon: const Icon(Icons.refresh),
        ),
      ],
      bottom: TabBar(
        controller: _tabs,
        tabs: const [
          Tab(text: 'Reminders'),
          Tab(text: 'Notifications'),
        ],
      ),
    ),
    floatingActionButton: FloatingActionButton.extended(
      onPressed: _createReminder,
      icon: const Icon(Icons.add_alert_outlined),
      label: const Text('New reminder'),
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_error!),
                TextButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          )
        : TabBarView(
            controller: _tabs,
            children: [_buildReminders(), _buildNotifications()],
          ),
  );

  Widget _buildReminders() => _reminders.isEmpty
      ? const Center(child: Text('No reminders yet.'))
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 92),
          itemCount: _reminders.length,
          itemBuilder: (context, index) {
            final reminder = _reminders[index];
            final pending = reminder['status'] == 'pending';
            return Card(
              margin: const EdgeInsets.only(bottom: 10),
              child: ListTile(
                leading: Icon(
                  pending
                      ? Icons.notifications_active_outlined
                      : Icons.notifications_none,
                ),
                title: Text(reminder['title'] as String? ?? ''),
                subtitle: Text(
                  '${_formatDate(reminder['dueAt'])}\n${reminder['status']}',
                ),
                isThreeLine: true,
                trailing: pending
                    ? IconButton(
                        tooltip: 'Cancel reminder',
                        onPressed: () => _cancelReminder(reminder),
                        icon: const Icon(Icons.close),
                      )
                    : null,
              ),
            );
          },
        );

  Widget _buildNotifications() => _notifications.isEmpty
      ? const Center(child: Text('No notifications yet.'))
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 92),
          itemCount: _notifications.length,
          itemBuilder: (context, index) {
            final notification = _notifications[index];
            final unread = notification['readAt'] == null;
            return Card(
              margin: const EdgeInsets.only(bottom: 10),
              child: ListTile(
                onTap: () => _openNotification(notification),
                leading: Icon(
                  unread
                      ? Icons.notifications_active
                      : Icons.notifications_none,
                ),
                title: Text(notification['title'] as String? ?? ''),
                subtitle: Text(
                  '${notification['body'] as String? ?? ''}\n${_formatDate(notification['createdAt'])}',
                ),
                isThreeLine: true,
                trailing: unread
                    ? const Icon(
                        Icons.circle,
                        size: 9,
                        color: Color(0xffa895ff),
                      )
                    : null,
              ),
            );
          },
        );
}
