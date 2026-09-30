import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../../ui/plain_text.dart';
import 'coding_run_detail_screen.dart';

/// Lists approval-gated coding runs and their isolated worktree diffs.
class CodingRunsScreen extends StatefulWidget {
  const CodingRunsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<CodingRunsScreen> createState() => _CodingRunsScreenState();
}

class _CodingRunsScreenState extends State<CodingRunsScreen> {
  List<Map<String, dynamic>> _runs = [];
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>('/api/v1/coding/runs');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _runs = jsonMaps(response.data);
        _loading = false;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load coding runs.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load coding runs.';
      });
    }
  }

  Future<void> _open(Map<String, dynamic> run) async {
    final id = jsonId(run);
    if (id == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => CodingRunDetailScreen(http: widget.http, runId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Coding runs'),
      actions: [
        IconButton(
          tooltip: 'Refresh coding runs',
          onPressed: _loading ? null : () => unawaited(_load()),
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _runs.isEmpty,
      onRetry: () => unawaited(_load()),
      empty: const EmptyState(
        icon: PhosphorIconsRegular.code,
        title: 'No coding runs yet',
        message: 'When you approve a coding task, Jarvis records the isolated worktree and diff here.',
      ),
      child: RefreshIndicator(
        onRefresh: _load,
        child: ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
          itemCount: _runs.length,
          itemBuilder: (context, index) =>
              ContentWidth(child: _runCard(_runs[index])),
        ),
      ),
    ),
  );

  Widget _runCard(Map<String, dynamic> run) {
    final status = asJsonString(run['status']) ?? '';
    final style = statusStyle(status);
    final files = jsonStrings(run['changedFiles']);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      onTap: () => unawaited(_open(run)),
      child: Row(
        children: [
          IconBadge(
            icon: status == 'failed'
                ? PhosphorIconsRegular.warningCircle
                : PhosphorIconsRegular.code,
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  codingTaskTitle(asJsonString(run['task']) ?? ''),
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 6),
                Wrap(
                  spacing: 8,
                  runSpacing: 6,
                  children: [
                    StatusPill(label: style.label, color: style.color),
                    if (asJsonString(run['repository']) != null)
                      StatusPill(
                        label: asJsonString(run['repository'])!,
                        color: JarvisColors.of(context).inkSoft,
                      ),
                    if (files.isNotEmpty)
                      StatusPill(
                        label:
                            '${files.length} ${files.length == 1 ? 'file' : 'files'}',
                        color: JarvisColors.of(context).muted,
                      ),
                    if (run['pullRequestNumber'] != null)
                      StatusPill(
                        label: switch (asJsonString(run['pullRequestState'])) {
                          'merged' => 'PR merged',
                          'closed' => 'PR closed',
                          _ => 'PR #${asJsonInt(run['pullRequestNumber'])} open',
                        },
                        color: switch (asJsonString(run['pullRequestState'])) {
                          'merged' => JarvisColors.of(context).violet,
                          'closed' => JarvisColors.of(context).muted,
                          _ => JarvisColors.of(context).accent,
                        },
                      ),
                  ],
                ),
              ],
            ),
          ),
          Icon(
            PhosphorIconsRegular.caretRight,
            size: 16,
            color: JarvisColors.of(context).muted,
          ),
        ],
      ),
    );
  }
}
