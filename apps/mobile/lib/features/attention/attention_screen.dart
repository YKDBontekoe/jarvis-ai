import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../shell/utility_pages.dart';

/// Everything that needs you in one list, in the order that matters: what Jarvis
/// is waiting on, what went wrong, what is late, what is due today, what only
/// needs a look. Putting an item away hides it for a while and never changes the
/// record; approvals and problems cannot be put away.
class AttentionScreen extends StatefulWidget {
  const AttentionScreen({
    required this.http,
    this.onOpenConversation,
    this.onAskInChat,
    super.key,
  });

  final Dio http;
  final Future<void> Function(String conversationId)? onOpenConversation;
  final ValueChanged<String>? onAskInChat;

  @override
  State<AttentionScreen> createState() => _AttentionScreenState();
}

class _AttentionScreenState extends State<AttentionScreen> {
  Map<String, dynamic>? _queue;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/attention');
      if (!mounted) return;
      setState(() {
        _queue = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load what needs you.';
      });
    }
  }

  Future<void> _snooze(String id, int days) async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/attention/${Uri.encodeComponent(id)}/snooze',
        data: {'days': days},
      );
      if (mounted) setState(() => _queue = jsonObject(response.data) ?? _queue);
    } on DioException catch (error) {
      if (mounted) {
        ScaffoldMessenger.maybeOf(context)
          ?..hideCurrentSnackBar()
          ..showSnackBar(
            SnackBar(
              content: Text(
                firstProblemMessage(error.response?.data) ??
                    'That did not work.',
              ),
            ),
          );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// The page where the item is dealt with. Commitments and decisions open
  /// their list; the item stays here until it is actually resolved there.
  String _page(String destination) {
    if (destination.startsWith('task:')) return 'tasks';
    if (destination.startsWith('commitment:') || destination == 'commitments') {
      return 'inbox';
    }
    if (destination.startsWith('decision:')) return 'decisions';
    return destination;
  }

  Future<void> _open(String destination) async {
    final page = utilityPageFor(
      _page(destination),
      widget.http,
      onOpenConversation: widget.onOpenConversation,
      onAskInChat: widget.onAskInChat,
    );
    if (page == null) return;
    await Navigator.of(context)
        .push<void>(MaterialPageRoute(builder: (_) => page));
    if (mounted) unawaited(_load());
  }

  IconData _icon(String kind) => switch (kind) {
    'approval' || 'task_approval' => PhosphorIconsRegular.shieldCheck,
    'problem' => PhosphorIconsRegular.warningCircle,
    'commitment' || 'suggested_promise' => PhosphorIconsRegular.users,
    'decision' => PhosphorIconsRegular.target,
    _ => PhosphorIconsRegular.bell,
  };

  String _caption(Map<String, dynamic> item) {
    final overdue = asJsonInt(item['daysOverdue']);
    if (asJsonString(item['kind']) == 'problem') return 'WENT WRONG';
    return switch (asJsonString(item['urgency'])) {
      'now' => 'WAITING ON YOU',
      'late' => overdue == 1 ? '1 DAY LATE' : '$overdue DAYS LATE',
      'today' => 'DUE TODAY',
      _ => 'TO REVIEW',
    };
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final items = jsonMaps(_queue?['items']);
    final snoozed = asJsonInt(_queue?['snoozed']);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Needs you')),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      HeroCard(
                        key: const Key('attention-hero'),
                        label: 'NEEDS YOU',
                        title: items.isEmpty
                            ? 'All clear'
                            : '${items.length} thing${items.length == 1 ? '' : 's'}',
                        subtitle: asJsonString(_queue?['summary']),
                      ),
                      const SizedBox(height: 12),
                      for (final (i, item) in items.indexed)
                        FeatureRow(
                          key: Key('attention-${item['id']}'),
                          index: i,
                          icon: _icon(asJsonString(item['kind']) ?? ''),
                          badgeColor:
                              asJsonString(item['urgency']) == 'now' ||
                                  asJsonString(item['urgency']) == 'late'
                              ? colors.warning
                              : null,
                          caption: _caption(item),
                          title: asJsonString(item['title']) ?? '',
                          subtitle: asJsonString(item['detail']),
                          actions: [
                            PillAction(
                              key: Key('attention-open-${item['id']}'),
                              compact: true,
                              primary: true,
                              label: 'Open',
                              onPressed: () => unawaited(
                                _open(asJsonString(item['destination']) ?? ''),
                              ),
                            ),
                            if (!asJsonBool(item['locked']))
                              PillAction(
                                key: Key('attention-snooze-${item['id']}'),
                                compact: true,
                                label: 'Tomorrow',
                                onPressed: _busy
                                    ? null
                                    : () => unawaited(
                                        _snooze(
                                          asJsonString(item['id']) ?? '',
                                          1,
                                        ),
                                      ),
                              ),
                          ],
                        ),
                      if (snoozed > 0)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(4, 8, 4, 0),
                          child: Text(
                            key: const Key('attention-snoozed'),
                            '$snoozed put away for later. They come back by themselves.',
                            style: TextStyle(color: colors.muted),
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}
