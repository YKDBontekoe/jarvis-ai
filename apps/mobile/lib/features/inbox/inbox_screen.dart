import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart' show dayLabel;
import 'inbox_models.dart';

/// Conversations that need an answer, across the chats Jarvis reads along
/// with and mail threads it tracks, plus the ledger of promises made and
/// received. Jarvis only drafts replies; the owner sends them.
class InboxScreen extends StatefulWidget {
  const InboxScreen({required this.http, this.now, super.key});

  final Dio http;
  final DateTime? now;

  @override
  State<InboxScreen> createState() => _InboxScreenState();
}

class _InboxScreenState extends State<InboxScreen> {
  InboxData? _inbox;
  List<CommitmentData> _commitments = const [];
  bool _loading = true;
  String? _error;
  String _filter = 'needs_reply';
  final Set<String> _busy = {};

  DateTime get _now => widget.now ?? DateTime.now();

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final inbox = await widget.http.get<dynamic>(
        '/api/v1/inbox',
        queryParameters: {'sync': true},
      );
      final commitments = await widget.http.get<dynamic>(
        '/api/v1/commitments',
        queryParameters: {'status': 'open'},
      );
      if (!mounted) return;
      setState(() {
        _inbox = InboxData.fromJson(inbox.data);
        _commitments = [
          for (final item in jsonMaps(commitments.data))
            ?CommitmentData.fromJson(item),
        ];
        _loading = false;
        _error = _inbox == null ? 'Could not load your inbox.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your inbox.';
      });
    }
  }

  Future<void> _run(String id, Future<void> Function() action) async {
    setState(() => _busy.add(id));
    try {
      await action();
      await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ?? 'That did not work.',
          ),
        ),
      );
    } finally {
      if (mounted) setState(() => _busy.remove(id));
    }
  }

  Future<void> _setState(InboxThreadData thread, String state) => _run(
    thread.id,
    () => widget.http.put<dynamic>(
      '/api/v1/inbox/${thread.id}/state',
      data: {'state': state},
    ),
  );

  Future<void> _snooze(InboxThreadData thread) {
    final tomorrow = DateTime(_now.year, _now.month, _now.day + 1, 9);
    return _run(
      thread.id,
      () => widget.http.post<dynamic>(
        '/api/v1/inbox/${thread.id}/snooze',
        data: {'until': tomorrow.toUtc().toIso8601String()},
      ),
    );
  }

  Future<void> _triage(InboxThreadData thread) => _run(
    thread.id,
    () => widget.http.post<dynamic>('/api/v1/inbox/${thread.id}/triage'),
  );

  Future<void> _setCommitment(CommitmentData item, String status) => _run(
    item.id,
    () => widget.http.patch<dynamic>(
      '/api/v1/commitments/${item.id}',
      data: {'status': status},
    ),
  );

  Future<void> _addCommitment() async {
    final result = await showJarvisDialog<Map<String, String>>(
      context: context,
      builder: (_) => const _CommitmentDialog(),
    );
    if (result == null) return;
    await _run(
      'new',
      () => widget.http.post<dynamic>('/api/v1/commitments', data: result),
    );
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return DefaultTabController(
      length: 2,
      child: Scaffold(
        appBar: AppBar(
          title: const PageTitle('Inbox'),
          bottom: const TabBar(
            tabs: [
              Tab(key: Key('inbox-tab-threads'), text: 'Conversations'),
              Tab(key: Key('inbox-tab-commitments'), text: 'Promises'),
            ],
          ),
          actions: [
            IconButton(
              key: const Key('inbox-refresh'),
              tooltip: 'Check for new messages',
              icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
              onPressed: () => unawaited(_load()),
            ),
          ],
        ),
        body: ContentWidth(
          child: TabBarView(
            children: [_threads(colors), _promises(colors)],
          ),
        ),
      ),
    );
  }

  Widget _threads(JarvisColors colors) {
    final inbox = _inbox;
    final shown = [
      for (final thread in inbox?.threads ?? const <InboxThreadData>[])
        if (thread.state == _filter) thread,
    ];
    return ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: inbox == null || inbox.threads.isEmpty,
      onRetry: () => unawaited(_load()),
      empty: const EmptyState(
        icon: PhosphorIconsRegular.chatsCircle,
        title: 'Your inbox is clear',
        message:
            'Turn on read along for a WhatsApp chat, or ask Jarvis to track '
            'a mail thread, and conversations that need you show up here.',
      ),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
        children: [
          Wrap(
            spacing: 8,
            runSpacing: 4,
            children: [
              for (final state in inboxStates)
                ChoiceChip(
                  key: Key('inbox-filter-$state'),
                  label: Text(
                    '${inboxStateLabel(state)} ${inbox?.counts[state] ?? 0}',
                  ),
                  selected: _filter == state,
                  onSelected: (_) => setState(() => _filter = state),
                ),
            ],
          ),
          const SizedBox(height: 12),
          if (shown.isEmpty)
            Padding(
              padding: const EdgeInsets.all(32),
              child: Text(
                'Nothing here.',
                textAlign: TextAlign.center,
                style: TextStyle(color: colors.muted),
              ),
            ),
          for (final thread in shown) _threadCard(colors, thread),
        ],
      ),
    );
  }

  Widget _threadCard(JarvisColors colors, InboxThreadData thread) {
    final busy = _busy.contains(thread.id);
    return SurfaceCard(
      key: Key('inbox-thread-${thread.id}'),
      margin: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(
                icon: thread.source == 'whatsapp'
                    ? PhosphorIconsRegular.whatsappLogo
                    : PhosphorIconsRegular.paperPlaneTilt,
                size: 34,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  thread.title,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              if (thread.priority >= 2)
                StatusPill(
                  label: priorityLabel(thread.priority),
                  color: thread.priority >= 3 ? colors.danger : colors.warning,
                ),
            ],
          ),
          if (thread.summary != null || thread.preview != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                thread.summary ??
                    '${thread.lastFromMe ? 'You: ' : ''}${thread.preview}',
                style: TextStyle(color: colors.inkSoft),
              ),
            ),
          if (thread.lastMessageAt != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                dayLabel(thread.lastMessageAt!, now: _now),
                style: TextStyle(color: colors.muted, fontSize: 12),
              ),
            ),
          if (thread.suggestedReply != null)
            Container(
              margin: const EdgeInsets.only(top: 10),
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: colors.surfaceMuted,
                borderRadius: BorderRadius.circular(10),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(child: Text(thread.suggestedReply!)),
                  IconButton(
                    tooltip: 'Copy draft',
                    icon: const Icon(PhosphorIconsRegular.copy, size: 18),
                    onPressed: () => unawaited(
                      Clipboard.setData(
                        ClipboardData(text: thread.suggestedReply!),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 4,
            children: [
              TextButton.icon(
                key: Key('inbox-triage-${thread.id}'),
                onPressed: busy ? null : () => unawaited(_triage(thread)),
                icon: const Icon(PhosphorIconsRegular.sparkle, size: 16),
                label: const Text('Summarize & draft'),
              ),
              if (thread.state != 'done')
                TextButton(
                  key: Key('inbox-done-${thread.id}'),
                  onPressed: busy
                      ? null
                      : () => unawaited(_setState(thread, 'done')),
                  child: const Text('Done'),
                ),
              if (thread.state != 'snoozed' && thread.state != 'done')
                TextButton(
                  onPressed: busy ? null : () => unawaited(_snooze(thread)),
                  child: const Text('Tomorrow'),
                ),
              if (thread.state == 'done' || thread.state == 'snoozed')
                TextButton(
                  onPressed: busy
                      ? null
                      : () => unawaited(_setState(thread, 'needs_reply')),
                  child: const Text('Reopen'),
                ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _promises(JarvisColors colors) {
    final today = _now;
    final suggested = [
      for (final item in _commitments)
        if (item.suggested) item,
    ];
    final accepted = [
      for (final item in _commitments)
        if (!item.suggested) item,
    ];
    return Stack(
      children: [
        ListScreenBody(
          loading: _loading,
          error: _error,
          isEmpty: _commitments.isEmpty,
          onRetry: () => unawaited(_load()),
          empty: const EmptyState(
            icon: PhosphorIconsRegular.checkCircle,
            title: 'No open promises',
            message:
                'Tell Jarvis what you promised, or what someone promised you, '
                'and it keeps track and reminds you.',
          ),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
            children: [
              if (suggested.isNotEmpty) ...[
                const SectionHeader('Jarvis found these in your chats'),
                for (final item in suggested)
                  _commitmentCard(colors, item, today, suggestion: true),
                const SizedBox(height: 12),
              ],
              for (final item in accepted)
                _commitmentCard(colors, item, today),
            ],
          ),
        ),
        Positioned(
          right: 16,
          bottom: 16,
          child: FloatingActionButton.extended(
            key: const Key('commitment-add'),
            onPressed: () => unawaited(_addCommitment()),
            icon: const Icon(PhosphorIconsRegular.plus),
            label: const Text('Add'),
          ),
        ),
      ],
    );
  }

  Widget _commitmentCard(
    JarvisColors colors,
    CommitmentData item,
    DateTime today, {
    bool suggestion = false,
  }) {
    final overdue = item.isOverdue(today);
    return SurfaceCard(
      key: Key('commitment-${item.id}'),
      margin: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            item.headline,
            style: TextStyle(color: colors.inkSoft, fontSize: 13),
          ),
          const SizedBox(height: 2),
          Text(
            item.description,
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
          if (item.dueOn != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                overdue
                    ? 'Overdue · ${dayLabel(item.dueOn!, now: today)}'
                    : 'Due ${dayLabel(item.dueOn!, now: today)}',
                style: TextStyle(
                  color: overdue ? colors.danger : colors.muted,
                  fontSize: 12.5,
                ),
              ),
            ),
          Wrap(
            spacing: 4,
            children: [
              if (suggestion)
                TextButton(
                  key: Key('commitment-keep-${item.id}'),
                  onPressed: () => unawaited(_setCommitment(item, 'accepted')),
                  child: const Text('Keep'),
                ),
              if (suggestion)
                TextButton(
                  onPressed: () => unawaited(_setCommitment(item, 'dropped')),
                  child: const Text('Dismiss'),
                )
              else ...[
                TextButton(
                  key: Key('commitment-done-${item.id}'),
                  onPressed: () => unawaited(_setCommitment(item, 'done')),
                  child: const Text('Done'),
                ),
                TextButton(
                  onPressed: () => unawaited(_setCommitment(item, 'dropped')),
                  child: const Text('Drop'),
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class _CommitmentDialog extends StatefulWidget {
  const _CommitmentDialog();

  @override
  State<_CommitmentDialog> createState() => _CommitmentDialogState();
}

class _CommitmentDialogState extends State<_CommitmentDialog> {
  final _who = TextEditingController();
  final _what = TextEditingController();
  String _direction = 'i_owe';
  DateTime? _due;

  @override
  void dispose() {
    _who.dispose();
    _what.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Add a promise'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        SegmentedButton<String>(
          segments: const [
            ButtonSegment(value: 'i_owe', label: Text('I owe')),
            ButtonSegment(value: 'owed_to_me', label: Text('Owed to me')),
          ],
          selected: {_direction},
          onSelectionChanged: (value) =>
              setState(() => _direction = value.first),
        ),
        const SizedBox(height: 12),
        TextField(
          key: const Key('commitment-who'),
          controller: _who,
          decoration: const InputDecoration(labelText: 'Who'),
        ),
        TextField(
          key: const Key('commitment-what'),
          controller: _what,
          decoration: const InputDecoration(labelText: 'What was promised'),
        ),
        const SizedBox(height: 8),
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: () async {
              final now = DateTime.now();
              final picked = await showDatePicker(
                context: context,
                initialDate: now,
                firstDate: now.subtract(const Duration(days: 365)),
                lastDate: now.add(const Duration(days: 365 * 5)),
              );
              if (picked != null) setState(() => _due = picked);
            },
            icon: const Icon(PhosphorIconsRegular.calendarBlank, size: 18),
            label: Text(
              _due == null
                  ? 'Add a due date'
                  : '${_due!.year}-${_due!.month.toString().padLeft(2, '0')}-${_due!.day.toString().padLeft(2, '0')}',
            ),
          ),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('commitment-save'),
        onPressed: () {
          if (_what.text.trim().isEmpty) return;
          Navigator.pop(context, {
            'direction': _direction,
            'counterparty': _who.text.trim(),
            'description': _what.text.trim(),
            if (_due != null)
              'dueOn':
                  '${_due!.year}-${_due!.month.toString().padLeft(2, '0')}-${_due!.day.toString().padLeft(2, '0')}',
          });
        },
        child: const Text('Save'),
      ),
    ],
  );
}
