import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What Jarvis would change about how it helps, from what the owner said a few
/// times. Each change shows why, a made-up before/after, and can be applied,
/// declined for good, or undone. None of them can touch tools or permissions.
class LearnedScreen extends StatefulWidget {
  const LearnedScreen({required this.http, super.key});

  final Dio http;

  @override
  State<LearnedScreen> createState() => _LearnedScreenState();
}

class _LearnedScreenState extends State<LearnedScreen> {
  Map<String, dynamic>? _card;
  List<Map<String, dynamic>> _all = const [];
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
      final learned = await widget.http.get<dynamic>(
        '/api/v1/outcomes/learned',
      );
      final changes = await widget.http.get<dynamic>(
        '/api/v1/outcomes/changes',
      );
      if (!mounted) return;
      setState(() {
        _card = jsonObject(learned.data);
        _all = jsonMaps(changes.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load what Jarvis learned.';
      });
    }
  }

  Future<void> _decide(String id, String action) async {
    setState(() => _busy = true);
    try {
      await widget.http.post<dynamic>('/api/v1/outcomes/changes/$id/$action');
      await _load();
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

  @override
  Widget build(BuildContext context) {
    final waiting = _all
        .where(
          (c) => asJsonString(jsonObject(c['change'])?['status']) == 'pending',
        )
        .toList();
    final applied = _all
        .where(
          (c) => asJsonString(jsonObject(c['change'])?['status']) == 'applied',
        )
        .toList();
    return Scaffold(
      appBar: AppBar(title: const PageTitle('What Jarvis learned')),
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
                        key: const Key('learned-hero'),
                        label: 'HELPING YOU BETTER',
                        title: waiting.isEmpty && applied.isEmpty
                            ? 'Nothing new this week'
                            : waiting.isNotEmpty
                            ? '${waiting.length} change${waiting.length == 1 ? '' : 's'} to review'
                            : 'Your changes are on',
                        subtitle: asJsonString(_card?['summary']),
                      ),
                      for (final (i, stat) in jsonMaps(
                        _card?['suggestions'],
                      ).indexed)
                        FeatureRow(
                          key: Key('suggestion-stat-${stat['kind']}'),
                          index: i,
                          icon: PhosphorIconsRegular.chatCircle,
                          caption: 'YOUR ANSWERS',
                          title: (asJsonString(stat['kind']) ?? '').replaceAll(
                            '_',
                            ' ',
                          ),
                          subtitle: asJsonString(stat['statement']),
                        ),
                      if (waiting.isNotEmpty) const GroupLabel('To review'),
                      for (final (i, view) in waiting.indexed)
                        _change(view, i, pending: true),
                      if (applied.isNotEmpty) const GroupLabel('On now'),
                      for (final (i, view) in applied.indexed)
                        _change(view, i, pending: false),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _change(
    Map<String, dynamic> view,
    int index, {
    required bool pending,
  }) {
    final change = jsonObject(view['change']) ?? const {};
    final id = asJsonString(change['id']) ?? '';
    final effect = jsonObject(view['effect']);
    final colors = JarvisColors.of(context);
    return FeatureRow(
      key: Key('change-$id'),
      index: index,
      icon: asJsonString(change['kind']) == 'quiet'
          ? PhosphorIconsRegular.bellSlash
          : PhosphorIconsRegular.sparkle,
      caption: pending ? 'SUGGESTED CHANGE' : 'ON',
      title: asJsonString(change['title']) ?? '',
      subtitle: [
        asJsonString(change['reason']),
        if (effect != null) asJsonString(effect['statement']),
      ].whereType<String>().join('\n'),
      extra: Padding(
        padding: const EdgeInsets.only(top: 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Example (made up)',
              style: TextStyle(color: colors.muted, fontSize: 12),
            ),
            const SizedBox(height: 4),
            Text(
              'Before: ${asJsonString(change['before']) ?? ''}',
              key: Key('change-before-$id'),
              style: TextStyle(color: colors.inkSoft),
            ),
            const SizedBox(height: 2),
            Text(
              'After: ${asJsonString(change['after']) ?? ''}',
              key: Key('change-after-$id'),
            ),
          ],
        ),
      ),
      actions: pending
          ? [
              PillAction(
                key: Key('change-apply-$id'),
                compact: true,
                primary: true,
                label: 'Apply',
                onPressed: _busy ? null : () => unawaited(_decide(id, 'apply')),
              ),
              PillAction(
                key: Key('change-reject-$id'),
                compact: true,
                label: 'No, never',
                onPressed: _busy
                    ? null
                    : () => unawaited(_decide(id, 'reject')),
              ),
            ]
          : [
              PillAction(
                key: Key('change-undo-$id'),
                compact: true,
                icon: PhosphorIconsRegular.arrowBendUpLeft,
                label: 'Undo',
                onPressed: _busy ? null : () => unawaited(_decide(id, 'undo')),
              ),
            ],
    );
  }
}
