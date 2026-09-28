import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';
import 'approvals_screen.dart';
import 'features/chat/chat_widgets.dart';
import 'theme.dart';
import 'json_maps.dart';
import 'ui/jarvis_ui.dart';

class TaskDetailsScreen extends StatefulWidget {
  const TaskDetailsScreen({
    required this.http,
    required this.taskId,
    super.key,
  });

  final Dio http;
  final String taskId;

  @override
  State<TaskDetailsScreen> createState() => _TaskDetailsScreenState();
}

class _TaskDetailsScreenState extends State<TaskDetailsScreen> {
  Map<String, dynamic>? _task;
  List<Map<String, dynamic>> _messages = [];
  bool _loading = true;
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
    });
    try {
      Map<String, dynamic>? task;
      try {
        final taskResponse = await widget.http.get<dynamic>(
          '/api/v1/tasks/${widget.taskId}',
        );
        task = jsonObject(taskResponse.data);
      } on DioException catch (error) {
        if (!mounted || revision != _requestRevision) return;
        setState(() {
          _error = error.response?.statusCode == 404
              ? 'This task is no longer available.'
              : 'Jarvis could not load the task details.';
        });
        return;
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _task = task;
        if (_task == null) {
          _error = 'Jarvis returned an invalid task.';
        }
      });
      if (task == null) return;
      try {
        final messagesResponse = await widget.http.get<dynamic>(
          '/api/v1/tasks/${widget.taskId}/messages',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _messages = jsonMaps(messagesResponse.data));
      } on DioException {
        if (!mounted || revision != _requestRevision) return;
        setState(() => _error = 'Jarvis could not load the task activity.');
      } catch (_) {
        if (!mounted || revision != _requestRevision) return;
        setState(() => _error = 'Jarvis could not load the task activity.');
      }
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() => _error = 'Jarvis could not load the task details.');
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  String _date(dynamic value) {
    final date = jsonDate(value, local: true);
    if (date == null) return '';
    final dateText = MaterialLocalizations.of(context).formatMediumDate(date);
    final timeText = MaterialLocalizations.of(
      context,
    ).formatTimeOfDay(TimeOfDay.fromDateTime(date));
    return '$dateText · $timeText';
  }

  Future<void> _openApprovals(String conversationId) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) =>
            ApprovalsScreen(http: widget.http, conversationId: conversationId),
      ),
    );
    if (mounted) await _load();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        asJsonString(_task?['title']) ?? 'Task',
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
      ),
      actions: [
        IconButton(
          tooltip: 'Refresh task',
          onPressed: _loading ? null : _load,
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        const SizedBox(width: 8),
      ],
    ),
    body: _loading && _task == null
        ? const LoadingState()
        : _task == null
        ? ErrorState(
            message: _error ?? 'Jarvis could not load the task details.',
            onRetry: _load,
          )
        : _buildDetails(),
  );

  Widget _buildDetails() {
    final task = _task!;
    final status = asJsonString(task['status']) ?? 'unknown';
    final style = statusStyle(status);
    final conversationId = asJsonString(task['conversationId']);
    final summary = asJsonString(task['summary']);
    final theme = Theme.of(context);
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SurfaceCard(
                  child: Row(
                    children: [
                      IconBadge(icon: style.icon, size: 48),
                      const SizedBox(width: 16),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              asJsonString(task['title']) ?? 'Task',
                              style: theme.textTheme.titleMedium,
                            ),
                            const SizedBox(height: 8),
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
                                  _date(task['createdAt']),
                                  style: theme.textTheme.bodySmall,
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
                if (status == 'needs_approval' && conversationId != null)
                  SurfaceCard(
                    margin: const EdgeInsets.only(top: 12),
                    padding: const EdgeInsets.fromLTRB(16, 14, 12, 14),
                    borderColor: JarvisColors.outlineStrong,
                    onTap: () => _openApprovals(conversationId),
                    child: Row(
                      children: [
                        const IconBadge(
                          icon: PhosphorIconsRegular.shieldWarning,
                          color: JarvisColors.warning,
                        ),
                        const SizedBox(width: 14),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'Jarvis needs your approval',
                                style: theme.textTheme.titleSmall,
                              ),
                              const SizedBox(height: 2),
                              Text(
                                'Review the actions before this task continues.',
                                style: theme.textTheme.bodySmall,
                              ),
                            ],
                          ),
                        ),
                        const Icon(
                          PhosphorIconsRegular.caretRight,
                          size: 16,
                          color: JarvisColors.inkSoft,
                        ),
                      ],
                    ),
                  ),
                if (_error != null)
                  InlineNotice(
                    message: _error!,
                    tone: NoticeTone.danger,
                    margin: const EdgeInsets.only(top: 12),
                  ),
                if (summary?.isNotEmpty == true &&
                    !_messages.any(
                      (message) => message['role'] == 'assistant',
                    )) ...[
                  const SizedBox(height: 16),
                  Text(summary!, style: theme.textTheme.bodyLarge),
                ],
                if (_messages.isNotEmpty) ...[
                  const SizedBox(height: 24),
                  const SectionHeader('Activity'),
                ],
                for (final message in _messages) _TaskMessage(message: message),
                if (_loading)
                  const Padding(
                    padding: EdgeInsets.all(12),
                    child: LoadingState(),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _TaskMessage extends StatelessWidget {
  const _TaskMessage({required this.message});

  final Map<String, dynamic> message;

  @override
  Widget build(BuildContext context) {
    final role = asJsonString(message['role']) ?? 'assistant';
    final isUser = role == 'user';
    final content = asJsonString(message['content']) ?? '';
    if (content.isEmpty) return const SizedBox.shrink();
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(16),
      color: isUser ? JarvisColors.surfaceMuted : JarvisColors.surface,
      borderColor: isUser ? JarvisColors.surfaceMuted : JarvisColors.outline,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (isUser)
                const IconBadge(icon: PhosphorIconsRegular.user, size: 26)
              else
                const JarvisAvatar(size: 26),
              const SizedBox(width: 10),
              Text(
                isUser ? 'You' : 'Jarvis',
                style: Theme.of(context).textTheme.labelLarge,
              ),
            ],
          ),
          const SizedBox(height: 10),
          if (isUser)
            SelectableText(
              content,
              style: const TextStyle(fontSize: 15, height: 1.5),
            )
          else
            JarvisMarkdown(data: content),
        ],
      ),
    );
  }
}
