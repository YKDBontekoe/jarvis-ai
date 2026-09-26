import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../task_details_screen.dart';
import '../chat/chat_widgets.dart';

class HomeOverview extends StatefulWidget {
  const HomeOverview({
    required this.http,
    required this.mark,
    required this.ready,
    required this.voiceStarting,
    required this.onTalk,
    required this.onOpenTasks,
    required this.refreshRevision,
    this.onContinueConversation,
    this.onSuggestion,
    super.key,
  });

  final Dio http;
  final Widget mark;
  final bool ready;
  final bool voiceStarting;
  final VoidCallback? onTalk;
  final VoidCallback onOpenTasks;
  final int refreshRevision;
  final VoidCallback? onContinueConversation;

  /// Sends a suggested prompt; hidden when null.
  final ValueChanged<String>? onSuggestion;

  @override
  State<HomeOverview> createState() => _HomeOverviewState();
}

class _HomeOverviewState extends State<HomeOverview>
    with WidgetsBindingObserver {
  List<_ActiveTask> _tasks = [];
  bool _loading = false;
  String? _error;
  int _requestRevision = 0;
  CancelToken? _request;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.ready) unawaited(_load());
  }

  @override
  void didUpdateWidget(HomeOverview oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.ready) {
      _requestRevision++;
      _request?.cancel();
      _tasks = [];
      _loading = false;
      _error = null;
    } else if (!oldWidget.ready ||
        oldWidget.refreshRevision != widget.refreshRevision) {
      unawaited(_load());
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && widget.ready) {
      unawaited(_load());
    }
  }

  Future<void> _load() async {
    if (!widget.ready || !mounted) return;
    final revision = ++_requestRevision;
    _request?.cancel();
    final request = CancelToken();
    _request = request;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>(
        '/api/v1/tasks',
        cancelToken: request,
      );
      final tasks =
          (response.data ?? [])
              .map(_ActiveTask.fromJson)
              .whereType<_ActiveTask>()
              .toList()
            ..sort((a, b) {
              final priority = a.priority.compareTo(b.priority);
              return priority != 0
                  ? priority
                  : b.createdAt.compareTo(a.createdAt);
            });
      if (mounted && revision == _requestRevision) {
        setState(() => _tasks = tasks);
      }
    } on DioException catch (error) {
      if (!CancelToken.isCancel(error) &&
          mounted &&
          revision == _requestRevision) {
        setState(() => _error = 'Could not load active tasks.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _openTask(_ActiveTask task) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => TaskDetailsScreen(http: widget.http, taskId: task.id),
      ),
    );
    if (mounted) await _load();
  }

  String get _greeting => switch (DateTime.now().hour) {
    < 12 => 'Good morning.',
    < 18 => 'Good afternoon.',
    _ => 'Good evening.',
  };

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _requestRevision++;
    _request?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: _load,
    child: LayoutBuilder(
      builder: (context, constraints) => ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 28),
        children: [
          Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 560),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    _greeting,
                    style: TextStyle(
                      fontSize: 16,
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'What do you need?',
                    style: TextStyle(fontSize: 28, fontWeight: FontWeight.w600),
                  ),
                  SizedBox(height: constraints.maxHeight < 480 ? 24 : 40),
                  Center(child: widget.mark),
                  const SizedBox(height: 22),
                  Center(
                    child: FilledButton.icon(
                      onPressed: widget.onTalk,
                      style: FilledButton.styleFrom(
                        minimumSize: const Size(200, 52),
                      ),
                      icon: widget.voiceStarting
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.mic_none_rounded),
                      label: Text(
                        widget.voiceStarting ? 'Connecting…' : 'Talk to Jarvis',
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Text(
                    widget.onSuggestion == null
                        ? 'Or send a message below.'
                        : 'Or try one of these:',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                  ),
                  if (widget.onSuggestion != null) ...[
                    const SizedBox(height: 14),
                    SuggestionChips(onSelected: widget.onSuggestion),
                  ],
                  if (widget.onContinueConversation != null)
                    Center(
                      child: TextButton.icon(
                        onPressed: widget.onContinueConversation,
                        icon: const Icon(Icons.chat_bubble_outline, size: 18),
                        label: const Text('Continue conversation'),
                      ),
                    ),
                  const SizedBox(height: 28),
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          _tasks.isEmpty
                              ? 'Active tasks'
                              : 'Active tasks (${_tasks.length})',
                          style: const TextStyle(fontWeight: FontWeight.w600),
                        ),
                      ),
                      TextButton(
                        onPressed: widget.onOpenTasks,
                        child: const Text('View all'),
                      ),
                      IconButton(
                        tooltip: 'Refresh active tasks',
                        onPressed: widget.ready && !_loading ? _load : null,
                        icon: const Icon(Icons.refresh, size: 20),
                      ),
                    ],
                  ),
                  if (_loading)
                    const Padding(
                      padding: EdgeInsets.only(bottom: 12),
                      child: LinearProgressIndicator(
                        semanticsLabel: 'Loading active tasks',
                      ),
                    ),
                  if (_error != null)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: Text(
                        '$_error Pull down to retry.',
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                    ),
                  if (!widget.ready)
                    const Text('Connect to Jarvis to see your active tasks.')
                  else if (!_loading && _error == null && _tasks.isEmpty)
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 12),
                      child: Text('No active tasks. Start one from Tasks.'),
                    ),
                  for (final task in _tasks.take(3))
                    Card(
                      margin: const EdgeInsets.only(bottom: 8),
                      child: ListTile(
                        leading: Icon(task.icon, color: task.color),
                        title: Text(
                          task.title,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        subtitle: Text(task.statusLabel),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => unawaited(_openTask(task)),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ],
      ),
    ),
  );
}

class _ActiveTask {
  const _ActiveTask({
    required this.id,
    required this.title,
    required this.status,
    required this.createdAt,
  });

  final String id;
  final String title;
  final String status;
  final DateTime createdAt;

  static _ActiveTask? fromJson(dynamic value) {
    if (value is! Map<String, dynamic>) return null;
    final id = value['id'];
    final title = value['title'];
    final status = value['status'];
    final createdAt = value['createdAt'];
    if (id is! String ||
        id.isEmpty ||
        title is! String ||
        status is! String ||
        createdAt is! String ||
        !const {
          'queued',
          'running',
          'waiting',
          'needs_approval',
        }.contains(status)) {
      return null;
    }
    final date = DateTime.tryParse(createdAt);
    if (date == null) return null;
    return _ActiveTask(id: id, title: title, status: status, createdAt: date);
  }

  int get priority => switch (status) {
    'needs_approval' => 0,
    'running' => 1,
    'waiting' => 2,
    _ => 3,
  };

  String get statusLabel => switch (status) {
    'needs_approval' => 'Needs your approval',
    'running' => 'In progress',
    'waiting' => 'Waiting',
    _ => 'Queued',
  };

  IconData get icon => switch (status) {
    'needs_approval' => Icons.gpp_maybe_outlined,
    'running' => Icons.autorenew_rounded,
    'waiting' => Icons.pause_circle_outline,
    _ => Icons.schedule,
  };

  Color get color => switch (status) {
    'needs_approval' => const Color(0xffffcb6b),
    'running' => const Color(0xff68d6a8),
    _ => const Color(0xffa895ff),
  };
}
