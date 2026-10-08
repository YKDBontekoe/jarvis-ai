import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'calibration_card.dart';
import 'decision_editor_screen.dart';
import 'decision_format.dart';

/// The decision journal: calls you made with how sure you were, the ones waiting for an answer, and how well
/// calibrated you turn out to be.
class DecisionsScreen extends StatefulWidget {
  const DecisionsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<DecisionsScreen> createState() => _DecisionsScreenState();
}

class _DecisionsScreenState extends State<DecisionsScreen> {
  List<Map<String, dynamic>> _decisions = const [];
  Map<String, dynamic>? _calibration;
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
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/decisions',
        queryParameters: {'limit': 200},
      );
      Map<String, dynamic>? calibration;
      try {
        final scored = await widget.http.get<dynamic>(
          '/api/v1/decisions/calibration',
        );
        calibration = jsonObject(scored.data);
      } on DioException {
        // The list still works without the score.
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _decisions = jsonMaps(response.data);
        _calibration = calibration;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your decisions.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load your decisions.';
      });
    }
  }

  Future<void> _edit([Map<String, dynamic>? decision]) async {
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) =>
            DecisionEditorScreen(http: widget.http, decision: decision),
      ),
    );
    if (saved == true && mounted) unawaited(_load());
  }

  Future<void> _open(Map<String, dynamic> decision) async {
    final result = await showModalBottomSheet<_SheetResult>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _DecisionSheet(decision: decision),
    );
    if (result == null || !mounted) return;
    final id = jsonId(decision);
    if (id == null) return;
    switch (result) {
      case _Resolve(:final outcome, :final note):
        await _resolve(id, outcome, note);
      case _Edit():
        await _edit(decision);
      case _Delete():
        await _delete(id);
    }
  }

  Future<void> _resolve(String id, bool outcome, String note) async {
    try {
      await widget.http.post<dynamic>(
        '/api/v1/decisions/$id/resolve',
        data: {'outcome': outcome, 'note': note},
      );
      if (!mounted) return;
      _show(outcome ? 'Recorded: it happened.' : 'Recorded: it did not.');
      unawaited(_load());
    } on DioException catch (error) {
      if (mounted) {
        _show(
          firstProblemMessage(error.response?.data) ??
              'Could not record the outcome.',
        );
      }
    } catch (_) {
      if (mounted) _show('Could not record the outcome.');
    }
  }

  Future<void> _delete(String id) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this decision?',
      message: 'It will no longer count towards your calibration.',
      cancelLabel: 'Keep',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<dynamic>('/api/v1/decisions/$id');
      if (mounted) unawaited(_load());
    } on DioException catch (error) {
      if (mounted) {
        _show(
          firstProblemMessage(error.response?.data) ??
              'Could not delete this decision.',
        );
      }
    } catch (_) {
      if (mounted) _show('Could not delete this decision.');
    }
  }

  void _show(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  List<Map<String, dynamic>> _withStatus(String status) => [
    for (final d in _decisions)
      if (asJsonString(d['status']) == status) d,
  ];

  @override
  Widget build(BuildContext context) {
    final due = _withStatus('due');
    final open = _withStatus('open');
    final resolved = _withStatus('resolved');
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Decisions'),
        actions: [
          HeaderAction(
            key: const Key('decision-log'),
            label: 'Log',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => unawaited(_edit()),
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: _decisions.isEmpty,
        onRetry: () => unawaited(_load()),
        empty: EmptyState(
          icon: PhosphorIconsRegular.hourglassMedium,
          title: 'No decisions logged',
          message:
              'Write down a call you are making and how sure you are. '
              'Jarvis asks you later how it went and shows how well your '
              'confidence matches reality.',
          action: FilledButton.icon(
            key: const Key('decision-log-empty'),
            onPressed: () => unawaited(_edit()),
            icon: const Icon(PhosphorIconsRegular.plus, size: 18),
            label: const Text('Log a decision'),
          ),
        ),
        child: OrbRefresh(
          onRefresh: _load,
          child: ListView(
            padding: EdgeInsets.fromLTRB(
              16,
              8,
              16,
              32 + MediaQuery.paddingOf(context).bottom,
            ),
            children: [
              ContentWidth(child: CalibrationCard(report: _calibration)),
              ..._section('Waiting for your answer', due),
              ..._section('Open', open),
              ..._section('Resolved', resolved),
            ],
          ),
        ),
      ),
    );
  }

  List<Widget> _section(String title, List<Map<String, dynamic>> items) {
    if (items.isEmpty) return const [];
    return [
      ContentWidth(child: SectionHeader(title)),
      for (final decision in items)
        ContentWidth(
          child: _DecisionCard(
            decision: decision,
            onTap: () => unawaited(_open(decision)),
          ),
        ),
      const SizedBox(height: 8),
    ];
  }
}

class _DecisionCard extends StatelessWidget {
  const _DecisionCard({required this.decision, required this.onTap});

  final Map<String, dynamic> decision;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final id = jsonId(decision) ?? '';
    final status = asJsonString(decision['status']) ?? 'open';
    final outcome = decision['outcome'];
    final probability = decision['probability'];
    final reviewOn = parseReviewDate(decision['reviewOn']);
    return SurfaceCard(
      key: Key('decision-$id'),
      margin: const EdgeInsets.only(bottom: 10),
      borderColor: colors.outline,
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            asJsonString(decision['title']) ?? 'Decision',
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: 4),
          Text(
            asJsonString(decision['prediction']) ?? '',
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 14, height: 1.4, color: colors.inkSoft),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              if (probability is num)
                StatusPill(
                  label: '${percentLabel(probability)} sure',
                  color: colors.accent,
                ),
              if (status == 'resolved' && outcome is bool)
                StatusPill(
                  label: outcome ? 'Happened' : 'Did not happen',
                  color: outcome ? colors.success : colors.warning,
                )
              else if (reviewOn != null)
                StatusPill(
                  label: status == 'due'
                      ? 'Answer now · ${reviewLabel(reviewOn).toLowerCase()}'
                      : reviewLabel(reviewOn),
                  color: status == 'due' ? colors.warning : colors.muted,
                ),
            ],
          ),
        ],
      ),
    );
  }
}

sealed class _SheetResult {
  const _SheetResult();
}

class _Resolve extends _SheetResult {
  const _Resolve(this.outcome, this.note);
  final bool outcome;
  final String note;
}

class _Edit extends _SheetResult {
  const _Edit();
}

class _Delete extends _SheetResult {
  const _Delete();
}

/// One decision in full, with the buttons to answer, change or remove it.
class _DecisionSheet extends StatefulWidget {
  const _DecisionSheet({required this.decision});

  final Map<String, dynamic> decision;

  @override
  State<_DecisionSheet> createState() => _DecisionSheetState();
}

class _DecisionSheetState extends State<_DecisionSheet> {
  late final _note = TextEditingController(
    text: asJsonString(widget.decision['outcomeNote']) ?? '',
  );

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final d = widget.decision;
    final resolved = asJsonString(d['status']) == 'resolved';
    final probability = d['probability'];
    final context_ = asJsonString(d['context']);
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          0,
          20,
          16 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                asJsonString(d['title']) ?? 'Decision',
                style: text.titleMedium,
              ),
              const SizedBox(height: 8),
              Text(
                asJsonString(d['prediction']) ?? '',
                style: const TextStyle(fontSize: 15, height: 1.45),
              ),
              if (probability is num)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(
                    'You were ${percentLabel(probability)} sure.',
                    style: TextStyle(fontSize: 13, color: colors.muted),
                  ),
                ),
              if (context_ != null && context_.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 10),
                  child: Text(
                    context_,
                    style: TextStyle(fontSize: 14, color: colors.inkSoft),
                  ),
                ),
              const SizedBox(height: 16),
              Text(
                resolved ? 'Change your answer' : 'Did it happen?',
                style: text.titleSmall,
              ),
              const SizedBox(height: 8),
              TextField(
                key: const Key('decision-note'),
                controller: _note,
                maxLength: 1000,
                minLines: 1,
                maxLines: 3,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'What actually happened? (optional)',
                ),
              ),
              Row(
                children: [
                  Expanded(
                    child: FilledButton.icon(
                      key: const Key('decision-happened'),
                      onPressed: () => Navigator.of(
                        context,
                      ).pop(_Resolve(true, _note.text.trim())),
                      icon: const Icon(
                        PhosphorIconsRegular.checkCircle,
                        size: 18,
                      ),
                      label: const Text('It happened'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: OutlinedButton.icon(
                      key: const Key('decision-did-not-happen'),
                      onPressed: () => Navigator.of(
                        context,
                      ).pop(_Resolve(false, _note.text.trim())),
                      icon: const Icon(PhosphorIconsRegular.xCircle, size: 18),
                      label: const Text('It did not'),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  if (!resolved)
                    TextButton(
                      key: const Key('decision-edit'),
                      onPressed: () => Navigator.of(context).pop(const _Edit()),
                      child: const Text('Edit'),
                    ),
                  const Spacer(),
                  TextButton(
                    key: const Key('decision-delete'),
                    onPressed: () => Navigator.of(context).pop(const _Delete()),
                    child: Text(
                      'Delete',
                      style: TextStyle(color: colors.danger),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
