import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import 'features/chat/chat_widgets.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

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
        ? const LoadingState()
        : _error != null
        ? EmptyState(
            icon: Icons.link_off_rounded,
            title: 'Nothing to show',
            message: _error,
          )
        : _buildDetails(),
  );

  IconData get _icon => _isReminder
      ? Icons.alarm_rounded
      : _isWatch
      ? Icons.monitor_heart_outlined
      : Icons.task_alt_rounded;

  Color get _color => _isReminder
      ? JarvisColors.rose
      : _isWatch
      ? JarvisColors.success
      : JarvisColors.accent;

  Widget _fact(IconData icon, String text) => Padding(
    padding: const EdgeInsets.only(top: 12),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 18, color: JarvisColors.muted),
        const SizedBox(width: 10),
        Expanded(
          child: Text(
            text,
            style: const TextStyle(color: JarvisColors.inkSoft, height: 1.4),
          ),
        ),
      ],
    ),
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
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
      children: [
        ContentWidth(
          maxWidth: 560,
          child: SurfaceCard(
            padding: const EdgeInsets.all(22),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                IconBadge(icon: _icon, color: _color, size: 52),
                const SizedBox(height: 18),
                Text(title, style: Theme.of(context).textTheme.headlineSmall),
                const SizedBox(height: 12),
                StatusPill.forStatus(status),
                if (detail.isNotEmpty) ...[
                  const SizedBox(height: 18),
                  const Divider(),
                  const SizedBox(height: 18),
                  _isTask
                      ? JarvisMarkdown(data: detail)
                      : Text(
                          detail,
                          style: Theme.of(context).textTheme.bodyLarge,
                        ),
                ],
                if (_isWatch && item['lastValue'] != null) ...[
                  _fact(
                    Icons.show_chart_rounded,
                    'Latest value: ${item['lastValue']}',
                  ),
                  if ((item['lastCheckedAt'] as String?) != null)
                    _fact(
                      Icons.update_rounded,
                      'Checked ${_date(item['lastCheckedAt'])}',
                    ),
                ],
                if ((_isTask || _isWatch) &&
                    (item['createdAt'] as String?) != null)
                  _fact(
                    Icons.calendar_today_outlined,
                    'Created ${_date(item['createdAt'])}',
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  String _watchCondition(Map<String, dynamic> item) {
    final comparison = item['comparison'] == 'below' ? '≤' : '≥';
    return 'Alert when ${item['jsonPath']} $comparison ${item['threshold']}';
  }
}
