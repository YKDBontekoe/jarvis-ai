import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'features/chat/chat_widgets.dart';
import 'json_maps.dart';
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

  bool get _isReminder =>
      widget.notificationType == 'reminder.due' ||
      widget.notificationType == 'reminder.failed';
  bool get _isWatch => widget.notificationType.startsWith('watch.');
  bool get _isTask =>
      widget.notificationType == 'task.completed' ||
      widget.notificationType == 'task.failed';

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final path = switch (widget.notificationType) {
        'reminder.due' ||
        'reminder.failed' => '/api/v1/reminders/${widget.sourceId}',
        'task.completed' ||
        'task.failed' => '/api/v1/tasks/${widget.sourceId}',
        'watch.triggered' ||
        'watch.failed' => '/api/v1/watches/${widget.sourceId}',
        _ => null,
      };
      if (path == null) {
        setState(() => _error = 'This notification has no linked item.');
        return;
      }
      final response = await widget.http.get<Map<String, dynamic>>(path);
      final data = response.data;
      if (!mounted) return;
      if (data is! Map) {
        setState(() => _error = 'Jarvis returned an invalid item.');
        return;
      }
      setState(() => _item = Map<String, dynamic>.from(data));
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
        : _error != null && _item == null
        ? (_error == 'This item is no longer available.' ||
                  _error == 'This notification has no linked item.'
              ? EmptyState(
                  icon: PhosphorIconsRegular.linkBreak,
                  title: 'Nothing to show',
                  message: _error!,
                )
              : ErrorState(message: _error!, onRetry: _load))
        : _item == null
        ? EmptyState(
            icon: PhosphorIconsRegular.linkBreak,
            title: 'Nothing to show',
            message: _error ?? 'This item is no longer available.',
          )
        : _buildDetails(),
  );

  IconData get _icon => _isReminder
      ? PhosphorIconsRegular.alarm
      : _isWatch
      ? PhosphorIconsRegular.pulse
      : PhosphorIconsRegular.checkCircle;

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
        asJsonString(item['title']) ??
        (_isReminder
            ? 'Reminder'
            : _isWatch
            ? 'Condition watch'
            : 'Task');
    final status = asJsonString(item['status']) ?? '';
    final detail = switch (widget.notificationType) {
      'reminder.due' || 'reminder.failed' => _date(item['dueAt']),
      'task.completed' || 'task.failed' => asJsonString(item['summary']) ?? '',
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
                IconBadge(icon: _icon, size: 52),
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
                    PhosphorIconsRegular.chartLine,
                    'Latest value: ${item['lastValue']}',
                  ),
                  if (asJsonString(item['lastCheckedAt']) != null)
                    _fact(
                      PhosphorIconsRegular.clockCounterClockwise,
                      'Checked ${_date(item['lastCheckedAt'])}',
                    ),
                ],
                if ((_isTask || _isWatch) &&
                    asJsonString(item['createdAt']) != null)
                  _fact(
                    PhosphorIconsRegular.calendarBlank,
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
