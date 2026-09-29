import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

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
        builder: (_) => _CodingRunDetailScreen(http: widget.http, runId: id),
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
                  asJsonString(run['task']) ?? 'Coding task',
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

class _CodingRunDetailScreen extends StatefulWidget {
  const _CodingRunDetailScreen({required this.http, required this.runId});

  final Dio http;
  final String runId;

  @override
  State<_CodingRunDetailScreen> createState() => _CodingRunDetailScreenState();
}

class _CodingRunDetailScreenState extends State<_CodingRunDetailScreen> {
  Map<String, dynamic>? _run;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/coding/runs/${widget.runId}',
      );
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _run = jsonObject(response.data);
        _error = _run == null ? 'Could not load this coding run.' : null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this coding run.',
      );
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() => _error = 'Could not load this coding run.');
    }
  }

  Future<void> _copy(String value) async {
    await Clipboard.setData(ClipboardData(text: value));
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(const SnackBar(content: Text('Copied.')));
  }

  @override
  Widget build(BuildContext context) {
    final run = _run;
    final files = jsonStrings(run?['changedFiles']);
    final diff = asJsonString(run?['diffSummary']);
    final summary = asJsonString(run?['summary']);
    final error = asJsonString(run?['error']);
    final path = asJsonString(run?['worktreePath']);
    return Scaffold(
      appBar: AppBar(title: const Text('Coding run')),
      body: run == null
          ? (_error == null
                ? const LoadingState()
                : ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  ))
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (_error != null)
                        InlineNotice(
                          message: _error!,
                          tone: NoticeTone.danger,
                          margin: const EdgeInsets.only(bottom: 12),
                        ),
                      Text(
                        asJsonString(run['task']) ?? 'Coding task',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 10),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          StatusPill(
                            label: statusStyle(
                              asJsonString(run['status']) ?? '',
                            ).label,
                            color: statusStyle(
                              asJsonString(run['status']) ?? '',
                            ).color,
                          ),
                          if (asJsonString(run['repository']) != null)
                            StatusPill(
                              label: asJsonString(run['repository'])!,
                              color: JarvisColors.of(context).inkSoft,
                            ),
                          if (run['exitCode'] is num)
                            StatusPill(
                              label: 'exit ${asJsonInt(run['exitCode'])}',
                              color: JarvisColors.of(context).muted,
                            ),
                        ],
                      ),
                      if (path != null) ...[
                        const SizedBox(height: 16),
                        const SectionHeader('Worktree'),
                        SurfaceCard(
                          child: Row(
                            children: [
                              Expanded(
                                child: SelectableText(
                                  path,
                                  style: const TextStyle(
                                    fontFamily: 'monospace',
                                    fontSize: 13,
                                  ),
                                ),
                              ),
                              IconButton(
                                tooltip: 'Copy worktree path',
                                onPressed: () => unawaited(_copy(path)),
                                icon: const Icon(
                                  PhosphorIconsRegular.copy,
                                  size: 18,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                      if (error != null && error.isNotEmpty) ...[
                        const SizedBox(height: 16),
                        InlineNotice(message: error, tone: NoticeTone.danger),
                      ],
                      if (summary != null && summary.isNotEmpty) ...[
                        const SizedBox(height: 16),
                        const SectionHeader('Summary'),
                        SurfaceCard(child: Text(summary)),
                      ],
                      if (files.isNotEmpty) ...[
                        const SizedBox(height: 16),
                        const SectionHeader('Changed files'),
                        GroupedSection(
                          children: [
                            for (final file in files)
                              ListTile(
                                leading: const IconBadge(
                                  icon: PhosphorIconsRegular.fileText,
                                  size: 32,
                                ),
                                title: Text(
                                  file,
                                  style: const TextStyle(
                                    fontFamily: 'monospace',
                                    fontSize: 13,
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ],
                      if (diff != null && diff.isNotEmpty) ...[
                        const SizedBox(height: 16),
                        const SectionHeader('Diff'),
                        SurfaceCard(
                          child: SelectableText(
                            diff,
                            style: const TextStyle(
                              fontFamily: 'monospace',
                              fontSize: 12.5,
                              height: 1.4,
                            ),
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}
