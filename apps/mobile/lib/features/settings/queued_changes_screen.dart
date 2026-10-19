import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/mutation_queue.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Changes made while Jarvis could not be reached. Nothing here counts as done
/// until the server confirms it; each one can be retried or discarded.
class QueuedChangesScreen extends StatefulWidget {
  const QueuedChangesScreen({
    required this.http,
    this.queue,
    this.clock,
    super.key,
  });

  final Dio http;
  final MutationQueue? queue;
  final DateTime Function()? clock;

  @override
  State<QueuedChangesScreen> createState() => _QueuedChangesScreenState();
}

class _QueuedChangesScreenState extends State<QueuedChangesScreen> {
  bool _syncing = false;
  String? _note;

  MutationQueue get _queue => widget.queue ?? MutationQueue.instance;
  DateTime get _now => (widget.clock ?? DateTime.now)();

  Future<void> _syncNow() async {
    setState(() {
      _syncing = true;
      _note = null;
    });
    final result = await _queue.drain(widget.http);
    if (!mounted) return;
    setState(() {
      _syncing = false;
      _note = switch (result.stopped) {
        'offline' => 'Jarvis still cannot be reached. Your changes are safe on this phone.',
        'signed_out' => 'You need to sign in again before these can be sent.',
        _ =>
          result.done == 0
              ? 'Nothing was waiting to send.'
              : '${result.done} sent and confirmed.',
      };
    });
  }

  String _age(DateTime at) {
    final age = _now.difference(at);
    if (age.inMinutes < 1) return 'just now';
    if (age.inHours < 1) return '${age.inMinutes} min ago';
    if (age.inDays < 1) return '${age.inHours} h ago';
    return '${age.inDays} d ago';
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Waiting to sync')),
      body: ValueListenableBuilder<List<QueuedMutation>>(
        valueListenable: _queue.items,
        builder: (context, items, _) {
          final waiting = items
              .where((m) => m.status == MutationStatus.queued)
              .length;
          final stuck = items.length - waiting;
          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
            children: [
              ContentWidth(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    FadeSlideIn(
                      child: KeyedSubtree(
                        key: const Key('queue-summary'),
                        child: HeroCard(
                          label: 'On this phone',
                          title: items.isEmpty
                              ? 'Nothing is waiting'
                              : '$waiting waiting${stuck > 0 ? ' · $stuck need you' : ''}',
                          subtitle: items.isEmpty
                              ? 'Changes you make without a connection show up here until Jarvis confirms them.'
                              : 'They are not done until Jarvis confirms them. Approvals, sends and deletes always need a connection.',
                          footer: items.isEmpty
                              ? null
                              : PillAction(
                                  key: const Key('queue-sync'),
                                  primary: true,
                                  onDark: true,
                                  icon: PhosphorIconsRegular.arrowsClockwise,
                                  label: _syncing ? 'Sending…' : 'Send now',
                                  onPressed: _syncing
                                      ? null
                                      : () => unawaited(_syncNow()),
                                ),
                        ),
                      ),
                    ),
                    if (_note != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 12),
                        child: Text(
                          _note!,
                          key: const Key('queue-note'),
                          style: TextStyle(color: colors.inkSoft),
                        ),
                      ),
                    if (items.isNotEmpty) const GroupLabel('Changes'),
                    for (final (i, item) in items.indexed)
                      FeatureRow(
                        key: Key('queue-${item.id}'),
                        index: i,
                        icon: switch (item.status) {
                          MutationStatus.queued => PhosphorIconsRegular.clock,
                          MutationStatus.conflict =>
                            PhosphorIconsRegular.warningCircle,
                          _ => PhosphorIconsRegular.xCircle,
                        },
                        badgeColor: item.status == MutationStatus.queued
                            ? colors.accent
                            : colors.warning,
                        caption: switch (item.status) {
                          MutationStatus.queued =>
                            'Waiting · ${_age(item.createdAt)}',
                          MutationStatus.failed => 'Needs you',
                          MutationStatus.conflict => 'Could not apply',
                          MutationStatus.expired => 'Too old',
                        },
                        title: item.label,
                        subtitle: item.message == null || item.message!.isEmpty
                            ? null
                            : item.message,
                        actions: [
                          if (item.status != MutationStatus.queued &&
                              item.status != MutationStatus.conflict)
                            PillAction(
                              key: Key('queue-retry-${item.id}'),
                              compact: true,
                              primary: true,
                              label: 'Try again',
                              onPressed: () => unawaited(_queue.retry(item.id)),
                            ),
                          PillAction(
                            key: Key('queue-discard-${item.id}'),
                            compact: true,
                            label: 'Discard',
                            onPressed: () => unawaited(_queue.discard(item.id)),
                          ),
                        ],
                      ),
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}
