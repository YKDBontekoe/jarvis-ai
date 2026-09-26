import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

class NotificationDetailsScreen extends StatefulWidget {
  const NotificationDetailsScreen({
    required this.http,
    required this.notificationType,
    required this.sourceId,
    super.key,
  });

  final Dio http;
  final String notificationType;
  final String sourceId;

  @override
  State<NotificationDetailsScreen> createState() =>
      _NotificationDetailsScreenState();
}

class _NotificationDetailsScreenState extends State<NotificationDetailsScreen> {
  Map<String, dynamic>? _item;
  bool _loading = true;
  String? _error;

  bool get _isReminder => widget.notificationType == 'reminder.due';
  bool get _isWatch => widget.notificationType.startsWith('watch.');
  bool get _isTask => widget.notificationType == 'task.completed';

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final path = switch (widget.notificationType) {
        'reminder.due' => '/api/v1/reminders/${widget.sourceId}',
        'task.completed' => '/api/v1/tasks/${widget.sourceId}',
        'watch.triggered' ||
        'watch.failed' => '/api/v1/watches/${widget.sourceId}',
        _ => null,
      };
      if (path == null) {
        setState(() => _error = 'This notification has no linked item.');
        return;
      }
      final response = await widget.http.get<Map<String, dynamic>>(path);
      if (mounted) setState(() => _item = response.data);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error = error.response?.statusCode == 404
            ? 'This item is no longer available.'
            : 'Jarvis could not load this item.',
      );
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  String _date(dynamic raw) {
    if (raw is! String) return '';
    final date = DateTime.tryParse(raw)?.toLocal();
    if (date == null) return '';
    final dateText = MaterialLocalizations.of(context).formatMediumDate(date);
    final timeText = MaterialLocalizations.of(
      context,
    ).formatTimeOfDay(TimeOfDay.fromDateTime(date));
    return '$dateText · $timeText';
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        _isReminder
            ? 'Reminder'
            : _isWatch
            ? 'Condition watch'
            : 'Task',
      ),
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Text(_error!, textAlign: TextAlign.center),
            ),
          )
        : _buildDetails(),
  );

  Widget _buildDetails() {
    final item = _item!;
    final title =
        item['title'] as String? ??
        (_isReminder
            ? 'Reminder'
            : _isWatch
            ? 'Condition watch'
            : 'Task');
    final status = item['status'] as String? ?? '';
    final detail = switch (widget.notificationType) {
      'reminder.due' => _date(item['dueAt']),
      'task.completed' => item['summary'] as String? ?? '',
      'watch.triggered' || 'watch.failed' => _watchCondition(item),
      _ => '',
    };
    return ListView(
      padding: const EdgeInsets.all(20),
      children: [
        Text(title, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 12),
        Align(
          alignment: Alignment.centerLeft,
          child: Chip(label: Text(status.replaceAll('_', ' '))),
        ),
        if (detail.isNotEmpty) ...[
          const SizedBox(height: 12),
          Text(detail, style: Theme.of(context).textTheme.bodyLarge),
        ],
        if (_isWatch && item['lastValue'] != null) ...[
          const SizedBox(height: 8),
          Text('Latest value: ${item['lastValue']}'),
          if ((item['lastCheckedAt'] as String?) != null)
            Text('Checked ${_date(item['lastCheckedAt'])}'),
        ],
        if ((_isTask || _isWatch) &&
            (item['createdAt'] as String?) != null) ...[
          const SizedBox(height: 20),
          Text('Created ${_date(item['createdAt'])}'),
        ],
      ],
    );
  }

  String _watchCondition(Map<String, dynamic> item) {
    final comparison = item['comparison'] == 'below' ? '≤' : '≥';
    return 'Alert when ${item['jsonPath']} $comparison ${item['threshold']}';
  }
}
