import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../http_urls.dart';
import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../../ui/plain_text.dart';

/// Review surface for one coding run: what changed, whether it has a pull
/// request, and the owner's decision to open, merge, or close it. Merging only
/// ever happens from the button on this screen.
class CodingRunDetailScreen extends StatefulWidget {
  const CodingRunDetailScreen({
    required this.http,
    required this.runId,
    super.key,
  });

  final Dio http;
  final String runId;

  @override
  State<CodingRunDetailScreen> createState() => _CodingRunDetailScreenState();
}

class _CodingRunDetailScreenState extends State<CodingRunDetailScreen> {
  Map<String, dynamic>? _run;
  Map<String, dynamic>? _diff;
  Map<String, dynamic>? _pullRequest;
  String? _error;
  String? _actionError;
  String? _busyAction;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  String get _base => '/api/v1/coding/runs/${widget.runId}';

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(_base);
      if (!mounted || revision != _requestRevision) return;
      final run = jsonObject(response.data);
      setState(() {
        _run = run;
        _error = run == null ? 'Could not load this coding run.' : null;
      });
      if (run == null) return;
      await Future.wait([_loadDiff(run), _loadPullRequest(run)]);
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

  Future<void> _loadDiff(Map<String, dynamic> run) async {
    if (asJsonString(run['status']) != 'completed') return;
    try {
      final response = await widget.http.get<dynamic>('$_base/diff');
      if (mounted) setState(() => _diff = jsonObject(response.data));
    } on DioException {
      // The workspace can be cleaned up; the summary still explains the run.
      if (mounted) setState(() => _diff = null);
    } catch (_) {
      if (mounted) setState(() => _diff = null);
    }
  }

  Future<void> _loadPullRequest(Map<String, dynamic> run) async {
    if (run['pullRequestNumber'] == null) return;
    try {
      final response = await widget.http.get<dynamic>('$_base/pull-request');
      if (mounted) setState(() => _pullRequest = jsonObject(response.data));
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _actionError =
              firstProblemMessage(error.response?.data) ??
              'Could not refresh the pull request.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _actionError = 'Could not refresh the pull request.');
      }
    }
  }

  Future<void> _act(String action, String path, {Object? data}) async {
    setState(() {
      _busyAction = action;
      _actionError = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '$_base/$path',
        data: data ?? const <String, Object>{},
        options: Options(receiveTimeout: const Duration(seconds: 90)),
      );
      if (!mounted) return;
      setState(() => _pullRequest = jsonObject(response.data));
      final refreshed = await widget.http.get<dynamic>(_base);
      if (mounted) setState(() => _run = jsonObject(refreshed.data) ?? _run);
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _actionError =
              firstProblemMessage(error.response?.data) ??
              'That did not work. Try again.',
        );
      }
    } catch (_) {
      if (mounted) setState(() => _actionError = 'That did not work. Try again.');
    } finally {
      if (mounted) setState(() => _busyAction = null);
    }
  }

  Future<void> _merge() async {
    final base = asJsonString(_pullRequest?['title']) ?? 'this change';
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Merge into main?',
      message:
          '“$base” will be squashed and merged. Jarvis will not deploy it '
          'automatically — your normal release process applies.',
      confirmLabel: 'Merge',
      icon: PhosphorIconsRegular.checkCircle,
    );
    if (confirmed && mounted) await _act('merge', 'pull-request/merge');
  }

  Future<void> _close() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Close pull request?',
      message: 'The branch stays on GitHub, but nothing will be merged.',
      confirmLabel: 'Close',
      cancelLabel: 'Keep open',
      destructive: true,
      icon: PhosphorIconsRegular.x,
    );
    if (confirmed && mounted) await _act('close', 'pull-request/close');
  }

  Future<void> _openOnGitHub(String url) async {
    final uri = parseHttpUrl(url);
    if (uri == null || !await launchHttpUrl(uri)) {
      await Clipboard.setData(ClipboardData(text: url));
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Link copied.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final run = _run;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Coding run'),
        actions: [
          IconButton(
            tooltip: 'Refresh',
            onPressed: () => unawaited(_load()),
            icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
          ),
        ],
      ),
      body: run == null
          ? (_error == null
                ? const SkeletonList(shape: SkeletonShape.detail)
                : ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  ))
          : _body(run),
    );
  }

  Widget _body(Map<String, dynamic> run) {
    final status = asJsonString(run['status']) ?? '';
    final style = statusStyle(status);
    final summary = asJsonString(run['summary']);
    final error = asJsonString(run['error']);
    final files = jsonMaps(_diff?['files']);
    final warnings = jsonStrings(_diff?['warnings']);
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
      children: [
        ContentWidth(
          child: FadeSlideIn(
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
                  codingTaskTitle(asJsonString(run['task']) ?? ''),
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
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
                if (error != null && error.isNotEmpty) ...[
                  const SizedBox(height: 16),
                  InlineNotice(message: error, tone: NoticeTone.danger),
                ],
                const SizedBox(height: 16),
                _pullRequestCard(run, hasChanges: files.isNotEmpty),
                for (final warning in warnings) ...[
                  const SizedBox(height: 12),
                  InlineNotice(message: warning),
                ],
                if (summary != null && summary.isNotEmpty) ...[
                  const SizedBox(height: 16),
                  const SectionHeader('What Jarvis did'),
                  SurfaceCard(child: Text(summary)),
                ],
                if (files.isNotEmpty) ...[
                  const SizedBox(height: 16),
                  const SectionHeader('Changes'),
                  DiffView(
                    patch: asJsonString(_diff?['patch']) ?? '',
                    protectedPaths: {
                      for (final file in files)
                        if (file['protected'] == true)
                          asJsonString(file['path']) ?? '',
                    },
                  ),
                  if (_diff?['truncated'] == true)
                    const Padding(
                      padding: EdgeInsets.only(top: 8),
                      child: InlineNotice(
                        message:
                            'This diff is long, so only the first part is shown here. '
                            'The full change is in the pull request.',
                        tone: NoticeTone.info,
                      ),
                    ),
                ] else if (_diff == null &&
                    (jsonStrings(run['changedFiles'])).isNotEmpty) ...[
                  const SizedBox(height: 16),
                  const SectionHeader('Changed files'),
                  GroupedSection(
                    children: [
                      for (final file in jsonStrings(run['changedFiles']))
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
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _pullRequestCard(Map<String, dynamic> run, {required bool hasChanges}) {
    final colors = JarvisColors.of(context);
    final status = _pullRequest;
    final hasPullRequest = run['pullRequestNumber'] != null || status != null;
    final number = asJsonInt(status?['number'], asJsonInt(run['pullRequestNumber']));
    final state = asJsonString(status?['state']) ?? asJsonString(run['pullRequestState']) ?? '';
    final url = asJsonString(status?['url']) ?? asJsonString(run['pullRequestUrl']);
    final checksState = asJsonString(status?['checksState']) ?? 'none';
    final busy = _busyAction != null;
    final completed = asJsonString(run['status']) == 'completed';

    if (!hasPullRequest && !(completed && hasChanges)) {
      return const SizedBox.shrink();
    }

    return SurfaceCard(
      elevated: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBadge(
                icon: state == 'merged'
                    ? PhosphorIconsRegular.checkCircle
                    : PhosphorIconsRegular.code,
                color: switch (state) {
                  'merged' => colors.violet,
                  'closed' => colors.muted,
                  _ => colors.accent,
                },
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      hasPullRequest
                          ? 'Pull request #$number'
                          : 'Ready for review',
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      hasPullRequest
                          ? _stateLabel(state, checksState)
                          : 'Open a pull request to review this change on GitHub. '
                                'Nothing is merged until you approve it.',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
            ],
          ),
          if (hasPullRequest && jsonMaps(status?['checks']).isNotEmpty) ...[
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 6,
              children: [
                for (final check in jsonMaps(status?['checks']).take(8))
                  StatusPill(
                    label: asJsonString(check['name']) ?? 'check',
                    color: _checkColor(check),
                  ),
              ],
            ),
          ],
          if (_actionError != null) ...[
            const SizedBox(height: 12),
            InlineNotice(message: _actionError!, tone: NoticeTone.danger),
          ],
          const SizedBox(height: 14),
          if (!hasPullRequest)
            FilledButton.icon(
              onPressed: busy ? null : () => _act('open', 'pull-request'),
              icon: _busyAction == 'open'
                  ? const SizedBox.square(
                      dimension: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(PhosphorIconsRegular.code, size: 18),
              label: Text(
                _busyAction == 'open' ? 'Opening…' : 'Open pull request',
              ),
            )
          else ...[
            if (state == 'open')
              Row(
                children: [
                  Expanded(
                    child: FilledButton.icon(
                      onPressed: busy || checksState == 'failure' || checksState == 'pending'
                          ? null
                          : _merge,
                      icon: _busyAction == 'merge'
                          ? const SizedBox.square(
                              dimension: 16,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(PhosphorIconsRegular.checkCircle, size: 18),
                      label: const Text('Approve & merge'),
                    ),
                  ),
                  const SizedBox(width: 8),
                  OutlinedButton(
                    onPressed: busy ? null : _close,
                    child: const Text('Close'),
                  ),
                ],
              ),
            if (state == 'open' && (checksState == 'failure' || checksState == 'pending'))
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  checksState == 'pending'
                      ? 'Merging unlocks when checks finish.'
                      : 'Merging is blocked while checks fail.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
            if (url != null)
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: () => unawaited(_openOnGitHub(url)),
                  icon: const Icon(PhosphorIconsRegular.arrowSquareOut, size: 16),
                  label: const Text('View on GitHub'),
                ),
              ),
          ],
        ],
      ),
    );
  }

  String _stateLabel(String state, String checksState) {
    if (state == 'merged') return 'Merged';
    if (state == 'closed') return 'Closed without merging';
    return switch (checksState) {
      'success' => 'Open · checks passed',
      'failure' => 'Open · checks failing',
      'pending' => 'Open · checks running',
      _ => 'Open · waiting for your review',
    };
  }

  Color _checkColor(Map<String, dynamic> check) {
    final colors = JarvisColors.of(context);
    final conclusion = asJsonString(check['conclusion']);
    if (asJsonString(check['status']) != 'completed') return colors.info;
    return switch (conclusion) {
      'success' || 'neutral' || 'skipped' => colors.success,
      _ => colors.danger,
    };
  }
}

/// Renders a unified diff with one collapsible block per file.
class DiffView extends StatelessWidget {
  const DiffView({required this.patch, this.protectedPaths = const {}, super.key});

  final String patch;
  final Set<String> protectedPaths;

  /// Splits a unified diff into `(path, lines)` blocks, one per `diff --git`.
  static List<({String path, List<String> lines})> split(String patch) {
    final blocks = <({String path, List<String> lines})>[];
    String? path;
    var lines = <String>[];
    void flush() {
      final current = path;
      if (current != null) blocks.add((path: current, lines: lines));
    }

    for (final line in patch.split('\n')) {
      if (line.startsWith('diff --git ')) {
        flush();
        final match = RegExp(r' b/(.+)$').firstMatch(line);
        path = match?.group(1) ?? line.substring(11);
        lines = <String>[];
      } else if (path != null) {
        lines.add(line);
      }
    }
    flush();
    return blocks;
  }

  @override
  Widget build(BuildContext context) {
    final blocks = split(patch);
    if (blocks.isEmpty) {
      return const SurfaceCard(child: Text('No textual changes.'));
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final (index, block) in blocks.indexed)
          _FileDiff(
            path: block.path,
            lines: block.lines,
            sensitive: protectedPaths.contains(block.path),
            initiallyExpanded: index == 0,
          ),
      ],
    );
  }
}

class _FileDiff extends StatelessWidget {
  const _FileDiff({
    required this.path,
    required this.lines,
    required this.sensitive,
    required this.initiallyExpanded,
  });

  final String path;
  final List<String> lines;
  final bool sensitive;
  final bool initiallyExpanded;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    var added = 0;
    var removed = 0;
    for (final line in lines) {
      if (line.startsWith('+') && !line.startsWith('+++')) added++;
      if (line.startsWith('-') && !line.startsWith('---')) removed++;
    }
    // Header lines (index, mode, ---/+++) are noise once the file name is shown.
    final body = lines.skipWhile((line) => !line.startsWith('@@') && !line.startsWith('Binary')).toList();
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: SurfaceCard(
        padding: EdgeInsets.zero,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(JarvisRadii.lg),
          child: Theme(
            data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
            child: ExpansionTile(
              initiallyExpanded: initiallyExpanded,
              tilePadding: const EdgeInsets.symmetric(horizontal: 14),
              childrenPadding: EdgeInsets.zero,
              title: Text(
                path,
                style: const TextStyle(
                  fontFamily: 'monospace',
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                ),
              ),
              subtitle: Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Wrap(
                  spacing: 8,
                  children: [
                    Text(
                      '+$added',
                      style: TextStyle(color: colors.success, fontSize: 12.5),
                    ),
                    Text(
                      '−$removed',
                      style: TextStyle(color: colors.danger, fontSize: 12.5),
                    ),
                    if (sensitive)
                      Text(
                        'security-sensitive',
                        style: TextStyle(color: colors.warning, fontSize: 12.5),
                      ),
                  ],
                ),
              ),
              children: [
                SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: ConstrainedBox(
                    constraints: BoxConstraints(
                      minWidth: MediaQuery.sizeOf(context).width - 64,
                    ),
                    child: IntrinsicWidth(
                      child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        for (final line in body.take(600))
                          _DiffLine(line: line),
                        if (body.length > 600)
                          Padding(
                            padding: const EdgeInsets.all(12),
                            child: Text(
                              '… ${body.length - 600} more lines in the pull request',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                          ),
                      ],
                    ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _DiffLine extends StatelessWidget {
  const _DiffLine({required this.line});

  final String line;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final (Color? background, Color foreground) = switch (line.isEmpty ? ' ' : line[0]) {
      '+' => (colors.successSoft, colors.ink),
      '-' => (colors.dangerSoft, colors.ink),
      '@' => (colors.infoSoft, colors.inkSoft),
      _ => (null, colors.inkSoft),
    };
    return Container(
      color: background,
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 1.5),
      child: Text(
        line.isEmpty ? ' ' : line,
        softWrap: false,
        style: TextStyle(
          fontFamily: 'monospace',
          fontSize: 12,
          height: 1.4,
          color: foreground,
        ),
      ),
    );
  }
}
