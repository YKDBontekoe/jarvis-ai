import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../ui/phosphor_icons.dart';
import '../approvals/approvals_screen.dart';
import '../chat/chat_widgets.dart';
import '../entities/entity_ref.dart';
import '../entities/entity_screen.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import 'task_refresh_bus.dart';

class TaskDetailsScreen extends StatefulWidget {
  const TaskDetailsScreen({
    required this.http,
    required this.taskId,
    this.pollEvery = const Duration(seconds: 5),
    this.bus,
    super.key,
  });

  final Dio http;
  final String taskId;

  /// How often an unfinished task is checked while the page is in front.
  final Duration pollEvery;

  /// Where realtime hints arrive; defaults to the app-wide bus.
  final TaskRefreshBus? bus;

  @override
  State<TaskDetailsScreen> createState() => _TaskDetailsScreenState();
}

class _TaskDetailsScreenState extends State<TaskDetailsScreen>
    with WidgetsBindingObserver {
  Map<String, dynamic>? _task;
  List<Map<String, dynamic>> _messages = [];
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  Timer? _poll;
  bool _loadingSilently = false;
  bool _foreground = true;
  TaskRefreshBus get _bus => widget.bus ?? TaskRefreshBus.instance;

  bool get _finished => isTerminalTaskStatus(asJsonString(_task?['status']));

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _bus.addListener(_onBusEvent);
    _load();
    _poll = Timer.periodic(widget.pollEvery, (_) => _pollOnce());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _bus.removeListener(_onBusEvent);
    _poll?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    _foreground = state == AppLifecycleState.resumed;
    // Catch up straight away when the app comes back to the front.
    if (_foreground) _pollOnce();
  }

  /// Refreshes quietly while the page is in front and the task can still change.
  void _pollOnce() {
    if (!mounted || !_foreground || _loading || _task == null) return;
    if (_finished) {
      _poll?.cancel();
      return;
    }
    // A page covered by another one is not being looked at.
    if (ModalRoute.of(context)?.isCurrent == false) return;
    unawaited(_load(silent: true));
  }

  void _onBusEvent() {
    final conversationId = asJsonString(_task?['conversationId']);
    if (conversationId != null && _bus.lastConversationId == conversationId) {
      _pollOnce();
    }
  }

  /// [silent] refreshes in place: no spinner, no error banner on failure, and
  /// the list keeps its scroll position.
  Future<void> _load({bool silent = false}) async {
    if (!mounted || (silent && _loadingSilently)) return;
    final revision = ++_requestRevision;
    if (!silent) {
      setState(() {
        _loading = true;
        _error = null;
      });
    }
    _loadingSilently = silent;
    try {
      Map<String, dynamic>? task;
      try {
        final taskResponse = await widget.http.get<dynamic>(
          '/api/v1/tasks/${widget.taskId}',
        );
        task = jsonObject(taskResponse.data);
      } on DioException catch (error) {
        if (!mounted || revision != _requestRevision) return;
        if (silent) return;
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
        if (!mounted || revision != _requestRevision || silent) return;
        setState(() => _error = 'Jarvis could not load the task activity.');
      } catch (_) {
        if (!mounted || revision != _requestRevision || silent) return;
        setState(() => _error = 'Jarvis could not load the task activity.');
      }
    } catch (_) {
      if (!mounted || revision != _requestRevision || silent) return;
      setState(() => _error = 'Jarvis could not load the task details.');
    } finally {
      if (silent) _loadingSilently = false;
      if (mounted && revision == _requestRevision && !silent) {
        setState(() => _loading = false);
      }
    }
  }

  String _date(dynamic value) {
    final date = jsonDate(value, local: true);
    if (date == null) return '';
    final dateText = MaterialLocalizations.of(context).formatMediumDate(date);
    final timeText = clockOf(context, date);
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
      title: PageTitle(
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
        ? const SkeletonList(shape: SkeletonShape.detail)
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
    final style = statusStyle(status, JarvisColors.of(context));
    final conversationId = asJsonString(task['conversationId']);
    final summary = asJsonString(task['summary']);
    final theme = Theme.of(context);
    return OrbRefresh(
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
                    borderColor: JarvisColors.of(context).outlineStrong,
                    onTap: () => _openApprovals(conversationId),
                    child: Row(
                      children: [
                        IconBadge(
                          icon: PhosphorIconsRegular.shieldWarning,
                          color: JarvisColors.of(context).warning,
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
                        Icon(
                          PhosphorIconsRegular.caretRight,
                          size: 16,
                          color: JarvisColors.of(context).inkSoft,
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
                // Missions it belongs to, what it follows up, links Jarvis drew.
                if (EntityRef.tryParse('task:${widget.taskId}') case final ref?)
                  Padding(
                    padding: const EdgeInsets.only(top: 20),
                    child: RelatedPanel(
                      key: const Key('task-related'),
                      http: widget.http,
                      entity: ref,
                    ),
                  ),
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
      color: isUser
          ? JarvisColors.of(context).surfaceMuted
          : JarvisColors.of(context).surface,
      borderColor: isUser
          ? JarvisColors.of(context).surfaceMuted
          : JarvisColors.of(context).outline,
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
