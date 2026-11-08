import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../json_maps.dart';
import '../../l10n/l10n.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../shell/utility_pages.dart';
import '../../schedule_format.dart' show clockOf, deviceTimeZoneLookup;

class ProactiveScreen extends StatefulWidget {
  const ProactiveScreen({
    required this.http,
    this.caseId,
    this.rootRef,
    super.key,
  });
  final Dio http;
  final String? caseId;
  final String? rootRef;

  @override
  State<ProactiveScreen> createState() => _ProactiveScreenState();
}

class _ProactiveScreenState extends State<ProactiveScreen> {
  List<Map<String, dynamic>> _items = [];
  Map<String, dynamic>? _case;
  bool _loading = true;
  bool _busy = false;
  String? _error;
  int _revision = 0;
  static const _base = '/api/v1/proactivity/cases';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_revision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>(
        widget.caseId == null ? _base : '$_base/${widget.caseId}',
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        if (widget.caseId == null) {
          _items = jsonMaps(response.data)
              .where(
                (c) =>
                    widget.rootRef == null ||
                    c['rootRef'] == widget.rootRef ||
                    jsonStrings(c['sourceRefs']).contains(widget.rootRef),
              )
              .toList();
        } else {
          _case = jsonObject(response.data);
        }
      });
    } on DioException catch (error) {
      if (mounted && revision == _revision) {
        setState(() => _error = describeApiError(error));
      }
    } finally {
      if (mounted && revision == _revision) setState(() => _loading = false);
    }
  }

  Future<void> _action(String path, Map<String, dynamic> data) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '$_base/${widget.caseId}/$path',
        data: data,
      );
      if (mounted) setState(() => _case = jsonObject(response.data));
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              describeApiError(error),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _command(String command) async {
    if (command == 'stop' || command == 'resolve' || command == 'dismiss') {
      final confirmed = await showJarvisConfirm(
        context,
        title: context.l10n.proactiveConfirm,
        message: context.l10n.proactiveConfirmBody,
        confirmLabel: context.l10n.proactiveContinue,
      );
      if (!confirmed || !mounted) return;
    }
    await _action('commands', {
      'command': command,
      'revision': _case?['revision'],
    });
  }

  Future<void> _edit() async {
    final changed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => _CaseEditor(
          http: widget.http,
          value: _case,
          rootRef: widget.rootRef,
        ),
      ),
    );
    if (changed == true && mounted) await _load();
  }

  Future<void> _resolveMemories(String choice) async {
    final sources = jsonMaps(_case?['context'])
        .where((s) => (asJsonString(s['ref']) ?? '').startsWith('memory:'))
        .toList();
    if (sources.length != 2) return;
    final s = context.l10n;
    String? firstContext;
    String? secondContext;
    if (choice == 'both') {
      final contexts = await showJarvisDialog<(String, String)>(
        context: context,
        builder: (_) => _MemoryContextsDialog(
          first: asJsonString(sources[0]['detail']) ?? '',
          second: asJsonString(sources[1]['detail']) ?? '',
        ),
      );
      if (contexts == null || !mounted) return;
      firstContext = contexts.$1;
      secondContext = contexts.$2;
    } else {
      final confirmed = await showJarvisConfirm(
        context,
        title: s.proactiveReviewMemories,
        message:
            '${s.proactiveMemoryHistory}\n\n1. ${sources[0]['detail']}\n\n2. ${sources[1]['detail']}',
        confirmLabel: choice == 'keep_first'
            ? s.proactiveKeepFirst
            : s.proactiveKeepSecond,
      );
      if (!confirmed || !mounted) return;
    }
    await _action('memory-resolution', {
      'revision': _case?['revision'],
      'choice': choice,
      'firstContext': firstContext,
      'secondContext': secondContext,
      'sourceVersions': {
        for (final source in sources)
          asJsonString(source['ref'])!: source['version'],
      },
    });
  }

  @override
  Widget build(BuildContext context) {
    final s = context.l10n;
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(
          _case == null
              ? s.proactiveTitle
              : asJsonString(_case!['title']) ?? s.proactiveTitle,
        ),
        actions: [
          if (!_busy && (widget.caseId == null || _case != null))
            HeaderAction(
              label: _case == null ? s.proactiveNew : s.proactiveEdit,
              icon: _case == null
                  ? PhosphorIconsRegular.plus
                  : PhosphorIconsRegular.pencilSimple,
              onPressed: _edit,
            ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: widget.caseId == null ? _items.isEmpty : _case == null,
        onRetry: _load,
        onRefresh: _load,
        empty: EmptyState(
          icon: PhosphorIconsRegular.sparkle,
          title: s.proactiveEmpty,
          message: s.proactiveDescription,
          action: PillAction(label: s.proactiveNew, onPressed: _edit),
        ),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 40),
          children: [
            ContentWidth(
              child: widget.caseId == null
                  ? _list()
                  : _case == null
                  ? const SizedBox.shrink()
                  : _detail(),
            ),
          ],
        ),
      ),
    );
  }

  Widget _list() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(context.l10n.proactiveDescription),
      const SizedBox(height: 16),
      for (final value in _items)
        FeatureRow(
          key: ValueKey('proactive-case-${value['id']}'),
          icon: PhosphorIconsRegular.sparkle,
          caption: _status(value),
          title: asJsonString(value['title']) ?? '',
          subtitle: asJsonString(value['nextAction']),
          onTap: () => unawaited(_openCase(value)),
          actions: [
            PillAction(
              label: context.l10n.proactiveOpen,
              onPressed: () => unawaited(_openCase(value)),
            ),
          ],
        ),
    ],
  );

  Future<void> _openCase(Map<String, dynamic> value) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) =>
            ProactiveScreen(http: widget.http, caseId: jsonId(value)),
      ),
    );
    if (mounted) await _load();
  }

  String _status(Map<String, dynamic> value) => switch (value['status']) {
    'suggested' => context.l10n.proactiveSuggested,
    'paused' => context.l10n.proactivePaused,
    'resolved' => context.l10n.proactiveResolved,
    'stopped' || 'dismissed' => context.l10n.proactiveStopped,
    'needs_input' => context.l10n.proactiveNeedsInput,
    _ =>
      value['enabled'] == true
          ? context.l10n.proactiveChecking
          : context.l10n.proactiveTracked,
  };

  Widget _detail() {
    final value = _case!;
    final s = context.l10n;
    final terminal = [
      'resolved',
      'dismissed',
      'stopped',
    ].contains(value['status']);
    final revision = value['revision'];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        HeroCard(
          maxTitleScale: 1.3,
          label: _status(value),
          title: asJsonString(value['title']) ?? '',
          subtitle: asJsonString(value['objective']),
          footer: Text(
            asJsonString(value['nextAction']) ?? '',
            style: const TextStyle(color: heroForeground),
          ),
        ),
        if (value['stopCondition'] is String)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 12),
            child: Text('${s.proactiveStopWhen}: ${value['stopCondition']}'),
          ),
        if (value['lastCheckedAt'] != null)
          Text('${s.proactiveLastCheck}: ${_date(value['lastCheckedAt'])}'),
        if (asJsonString(value['notes']) case final String notes
            when notes.isNotEmpty)
          SurfaceCard(child: SelectableText(notes)),
        if (value['nextCheckAt'] != null)
          Text('${s.proactiveNextCheck}: ${_date(value['nextCheckAt'])}'),
        if (!terminal)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 16),
            child: Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                if (value['status'] == 'suggested')
                  PillAction(
                    label: s.proactiveAccept,
                    onPressed: _busy
                        ? null
                        : () => unawaited(_command('accept')),
                  )
                else
                  PillAction(
                    label: value['enabled'] == true
                        ? s.proactivePause
                        : s.proactiveEnable,
                    onPressed: _busy
                        ? null
                        : () => unawaited(
                            _command(
                              value['enabled'] == true ? 'pause' : 'enable',
                            ),
                          ),
                  ),
                if (value['status'] != 'suggested')
                  PillAction(
                    label: s.proactiveResolve,
                    onPressed: _busy
                        ? null
                        : () => unawaited(_command('resolve')),
                  ),
                PillAction(
                  label: s.proactiveStop,
                  onPressed: _busy ? null : () => unawaited(_command('stop')),
                ),
              ],
            ),
          ),
        for (final finding in jsonMaps(value['findings']))
          FeatureRow(
            icon: PhosphorIconsRegular.warningCircle,
            caption: s.proactiveEvidence,
            title: asJsonString(finding['title']) ?? '',
            subtitle: asJsonString(finding['reason']),
          ),
        if (value['preparedReport'] is String) ...[
          GroupLabel(s.proactivePreparation),
          SurfaceCard(child: SelectableText(value['preparedReport'] as String)),
        ],
        if (jsonMaps(value['reports']).isNotEmpty &&
            (value['preparedReport'] == null ||
                jsonMaps(value['reports']).length > 1))
          ExpansionTile(
            title: Text(s.proactivePreviousPreparation),
            children: [
              for (final report in jsonMaps(
                value['reports'],
              ).reversed.skip(value['preparedReport'] is String ? 1 : 0))
                ExpansionTile(
                  title: Text(_date(report['at'])),
                  childrenPadding: const EdgeInsets.all(16),
                  children: [
                    SelectableText(asJsonString(report['content']) ?? ''),
                  ],
                ),
            ],
          ),
        GroupLabel(s.proactiveSteps),
        for (final assumption in jsonMaps(value['assumptions']))
          FeatureRow(
            icon: PhosphorIconsRegular.eye,
            title: asJsonString(assumption['statement']) ?? '',
            actions: [
              if (!terminal)
                PillAction(
                  label: s.proactiveReviewed,
                  onPressed: _busy
                      ? null
                      : () =>
                            unawaited(_command('reviewed:${assumption['id']}')),
                ),
            ],
          ),
        for (final step in jsonMaps(value['steps']))
          FeatureRow(
            icon: step['done'] == true
                ? PhosphorIconsRegular.checkCircle
                : PhosphorIconsRegular.circle,
            title: asJsonString(step['title']) ?? '',
            subtitle: asJsonString(step['completionCondition']),
            actions: [
              if (!terminal && step['done'] != true)
                PillAction(
                  label: s.proactiveDone,
                  onPressed: _busy
                      ? null
                      : () => unawaited(_command('done:${step['id']}')),
                ),
            ],
          ),
        GroupLabel(s.proactiveSources),
        for (final source in jsonMaps(value['context']))
          FeatureRow(
            icon: PhosphorIconsRegular.link,
            title: asJsonString(source['title']) ?? '',
            subtitle: asJsonString(source['detail']),
            actions: [
              PillAction(
                label: s.proactiveOpen,
                onPressed: () {
                  final page = utilityPageFor(
                    'entity:${source['ref']}',
                    widget.http,
                  );
                  if (page != null) {
                    Navigator.of(context)
                        .push<void>(MaterialPageRoute(builder: (_) => page));
                  }
                },
              ),
            ],
          ),
        GroupLabel(s.proactiveHistory),
        for (final receipt in jsonMaps(value['receipts']).reversed)
          ExpansionTile(
            title: Text(asJsonString(receipt['reason']) ?? ''),
            subtitle: Text(_date(receipt['at'])),
            childrenPadding: const EdgeInsets.all(16),
            children: [
              if (receipt['trigger'] != null)
                Text('${s.proactiveTrigger}: ${receipt['trigger']}'),
              if (receipt['before'] != null &&
                  receipt['before'] != receipt['after']) ...[
                GroupLabel(s.proactiveBefore),
                SelectableText('${receipt['before']}'),
                GroupLabel(s.proactiveAfter),
                SelectableText('${receipt['after']}'),
              ],
              if (['preference', 'internal_plan'].contains(receipt['kind']) &&
                  receipt['undone'] != true)
                PillAction(
                  label: s.proactiveUndo,
                  onPressed: _busy
                      ? null
                      : () => unawaited(
                          _action('receipts/${receipt['id']}/undo', {
                            'revision': revision,
                          }),
                        ),
                ),
            ],
          ),
        GroupLabel(s.proactiveFeedback),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final choice in [
              ('useful', s.proactiveUseful),
              ('not_useful', s.proactiveNotUseful),
              ('too_early', s.proactiveTooEarly),
              ('too_much_effort', s.proactiveTooMuch),
            ])
              PillAction(
                label: choice.$2,
                onPressed: _busy
                    ? null
                    : () => unawaited(
                        _action('feedback', {
                          'signal': choice.$1,
                          'revision': revision,
                        }),
                      ),
              ),
          ],
        ),
        if (value['kind'] == 'memory_conflict' && !terminal) ...[
          GroupLabel(s.proactiveReviewMemories),
          Text(s.proactiveMemoryHistory),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final choice in [
                ('keep_first', s.proactiveKeepFirst),
                ('keep_second', s.proactiveKeepSecond),
                ('both', s.proactiveKeepBoth),
              ])
                PillAction(
                  label: choice.$2,
                  onPressed: _busy
                      ? null
                      : () => unawaited(_resolveMemories(choice.$1)),
                ),
            ],
          ),
        ],
      ],
    );
  }

  String _date(Object? raw) {
    final date = jsonDate(raw, local: true);
    if (date == null) return '';
    final local = MaterialLocalizations.of(context);
    return '${local.formatMediumDate(date)} ${clockOf(context, date)}';
  }
}

class _MemoryContextsDialog extends StatefulWidget {
  const _MemoryContextsDialog({required this.first, required this.second});
  final String first;
  final String second;
  @override
  State<_MemoryContextsDialog> createState() => _MemoryContextsDialogState();
}

class _MemoryContextsDialogState extends State<_MemoryContextsDialog> {
  final _first = TextEditingController();
  final _second = TextEditingController();
  @override
  void dispose() {
    _first.dispose();
    _second.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = context.l10n;
    return AlertDialog(
      title: Text(s.proactiveKeepBoth),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(widget.first),
            TextField(
              controller: _first,
              maxLength: 200,
              decoration: InputDecoration(labelText: s.proactiveFirstContext),
            ),
            Text(widget.second),
            TextField(
              controller: _second,
              maxLength: 200,
              decoration: InputDecoration(labelText: s.proactiveSecondContext),
            ),
          ],
        ),
      ),
      actions: [
        PillAction(
          label: s.proactiveCancel,
          onPressed: () => Navigator.pop(context),
        ),
        PillAction(
          label: s.proactiveSave,
          onPressed: () {
            if (_first.text.trim().isNotEmpty &&
                _second.text.trim().isNotEmpty) {
              Navigator.pop(context, (_first.text.trim(), _second.text.trim()));
            }
          },
        ),
      ],
    );
  }
}

class _CaseEditor extends StatefulWidget {
  const _CaseEditor({required this.http, this.value, this.rootRef});
  final Dio http;
  final Map<String, dynamic>? value;
  final String? rootRef;
  @override
  State<_CaseEditor> createState() => _CaseEditorState();
}

class _CaseEditorState extends State<_CaseEditor> {
  final _form = GlobalKey<FormState>();
  late final _title = TextEditingController(
    text: asJsonString(widget.value?['title']),
  );
  late final _objective = TextEditingController(
    text: asJsonString(widget.value?['objective']),
  );
  late final _stop = TextEditingController(
    text: asJsonString(widget.value?['stopCondition']),
  );
  late final _capacity = TextEditingController(
    text: widget.value?['availableMinutes']?.toString(),
  );
  late final _notes = TextEditingController(
    text: asJsonString(widget.value?['notes']),
  );
  late final Map<String, dynamic> _policy =
      jsonObject(widget.value?['policy']) ?? {};
  late bool _useCalendarCapacity = asJsonBool(_policy['useCalendarCapacity']);
  late final _cadence = TextEditingController(
    text: '${_policy['cadenceMinutes'] ?? 60}',
  );
  late final _dailyChecks = TextEditingController(
    text: '${_policy['maxChecksPerDay'] ?? 12}',
  );
  late final _optionalItems = TextEditingController(
    text: '${_policy['optionalItemsPerDay'] ?? 3}',
  );
  late final _quietStart = TextEditingController(
    text: '${_policy['quietStart'] ?? 22}',
  );
  late final _quietEnd = TextEditingController(
    text: '${_policy['quietEnd'] ?? 8}',
  );
  late final _timeZone = TextEditingController(
    text: asJsonString(_policy['timeZoneId']),
  );
  late final List<Map<String, dynamic>> _steps = jsonMaps(
    widget.value?['steps'],
  ).toList();
  late final List<Map<String, dynamic>> _assumptions = jsonMaps(
    widget.value?['assumptions'],
  ).toList();
  late String? _root = asJsonString(widget.value?['rootRef']) ?? widget.rootRef;
  late DateTime? _deadline = jsonDate(widget.value?['deadline'], local: true);
  late DateTime? _expires = jsonDate(widget.value?['expiresAt'], local: true);
  late final _selected = {
    ...jsonStrings(widget.value?['sourceRefs']),
    if (widget.rootRef != null) widget.rootRef!,
  };
  List<Map<String, dynamic>> _sources = [];
  late String _kind =
      asJsonString(widget.value?['kind']) ??
      (widget.rootRef?.startsWith('project:') == true
          ? 'project'
          : 'open_loop');
  bool _saving = false;
  String? _zone;
  String? _error;
  @override
  void initState() {
    super.initState();
    unawaited(_loadSources());
  }

  Future<void> _loadSources() async {
    _zone = await deviceTimeZoneLookup();
    if (!mounted) return;
    if (_timeZone.text.isEmpty) _timeZone.text = _zone ?? 'UTC';
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/proactivity/cases/sources',
      );
      if (mounted) setState(() => _sources = jsonMaps(response.data));
    } on DioException catch (error) {
      if (mounted) setState(() => _error = describeApiError(error));
    }
  }

  @override
  void dispose() {
    _title.dispose();
    _objective.dispose();
    _stop.dispose();
    _notes.dispose();
    _capacity.dispose();
    for (final controller in [
      _cadence,
      _dailyChecks,
      _optionalItems,
      _quietStart,
      _quietEnd,
      _timeZone,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _addStep() async {
    final result = await showJarvisDialog<Map<String, dynamic>>(
      context: context,
      builder: (_) => _StepEditor(
        sources: _sources.where((s) => _selected.contains(s['ref'])).toList(),
        steps: _steps,
      ),
    );
    if (result != null && mounted) setState(() => _steps.add(result));
  }

  Future<void> _addAssumption() async {
    final result = await showJarvisDialog<Map<String, dynamic>>(
      context: context,
      builder: (_) => _AssumptionEditor(
        sources: _sources.where((s) => _selected.contains(s['ref'])).toList(),
        tripMode: _kind == 'trip',
        researchMode: _kind == 'research',
      ),
    );
    if (result != null && mounted) setState(() => _assumptions.add(result));
  }

  Future<void> _pickDeadline({bool expiry = false}) async {
    final current = expiry ? _expires : _deadline;
    final date = await showDatePicker(
      context: context,
      initialDate: current ?? DateTime.now(),
      firstDate: DateTime(2020),
      lastDate: DateTime(2100),
    );
    if (date == null || !mounted) return;
    final time = await showTimePicker(
      context: context,
      initialTime: current == null
          ? const TimeOfDay(hour: 17, minute: 0)
          : TimeOfDay.fromDateTime(current),
    );
    if (time != null && mounted) {
      setState(() {
        final selected = DateTime(
          date.year,
          date.month,
          date.day,
          time.hour,
          time.minute,
        );
        if (expiry) {
          _expires = selected;
        } else {
          _deadline = selected;
        }
      });
    }
  }

  Future<void> _save() async {
    if (_saving || !(_form.currentState?.validate() ?? false)) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    final body = <String, dynamic>{
      'kind': _kind,
      'title': _title.text.trim(),
      'objective': _objective.text.trim(),
      'stopCondition': _stop.text.trim(),
      'notes': _notes.text.trim(),
      'sourceRefs': _selected.toList(),
      'rootRef': _root ?? _selected.firstOrNull,
      'profileId': widget.value?['profileId'],
      'explicit': true,
      'enabled': false,
      'policy': {
        'cadenceMinutes': int.parse(_cadence.text),
        'maxChecksPerDay': int.parse(_dailyChecks.text),
        'optionalItemsPerDay': int.parse(_optionalItems.text),
        'quietStart': int.parse(_quietStart.text),
        'quietEnd': int.parse(_quietEnd.text),
        'timeZoneId': _timeZone.text.trim(),
        'useCalendarCapacity': _kind == 'goal_capacity' && _useCalendarCapacity,
      },
      'expiresAt': _expires?.toUtc().toIso8601String(),
      'steps': _steps,
      'assumptions': _assumptions,
      'deadline': _deadline?.toUtc().toIso8601String(),
      'availableMinutes': int.tryParse(_capacity.text),
    };
    try {
      if (widget.value == null) {
        await widget.http.post<dynamic>(
          '/api/v1/proactivity/cases',
          data: body,
        );
      } else {
        await widget.http.put<dynamic>(
          '/api/v1/proactivity/cases/${widget.value!['id']}',
          data: body,
          queryParameters: {'revision': widget.value!['revision']},
        );
      }
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (mounted) {
        setState(() {
          _saving = false;
          _error =
              firstProblemMessage(error.response?.data) ??
              describeApiError(error);
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = context.l10n;
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(
          widget.value == null ? s.proactiveNew : s.proactiveEdit,
        ),
        actions: [
          HeaderAction(
            label: s.proactiveSave,
            icon: PhosphorIconsRegular.check,
            busy: _saving,
            onPressed: () => unawaited(_save()),
          ),
        ],
      ),
      body: Form(
        key: _form,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            ContentWidth(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (_error != null) InlineNotice(message: _error!),
                  TextFormField(
                    controller: _title,
                    maxLength: 200,
                    decoration: InputDecoration(labelText: s.proactiveName),
                    validator: (v) => v == null || v.trim().isEmpty
                        ? s.proactiveRequired
                        : null,
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _objective,
                    minLines: 3,
                    maxLines: 6,
                    maxLength: 2000,
                    decoration: InputDecoration(
                      labelText: s.proactiveObjective,
                    ),
                    validator: (v) => v == null || v.trim().isEmpty
                        ? s.proactiveRequired
                        : null,
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    initialValue: _kind,
                    isExpanded: true,
                    decoration: InputDecoration(labelText: s.proactiveKind),
                    items: [
                      for (final choice in [
                        ('open_loop', s.proactiveKindIntention),
                        ('project', s.proactiveKindProject),
                        ('follow_through', s.proactiveKindFollow),
                        ('context', s.proactiveKindContext),
                        ('return', s.proactiveKindReturn),
                        ('refund', s.proactiveKindRefund),
                        ('repair', s.proactiveKindRepair),
                        ('deadline', s.proactiveKindDeadline),
                        ('day_plan', s.proactiveKindDay),
                        ('meeting', s.proactiveKindMeeting),
                        ('goal_capacity', s.proactiveKindCapacity),
                        ('trip', s.proactiveKindTrip),
                        ('consistency', s.proactiveKindConsistency),
                        ('decision', s.proactiveKindDecision),
                        ('research', s.proactiveKindResearch),
                        ('memory_conflict', s.proactiveKindMemory),
                        ('routine_trial', s.proactiveKindTrial),
                      ])
                        DropdownMenuItem(
                          value: choice.$1,
                          child: Text(choice.$2),
                        ),
                    ],
                    onChanged: widget.value == null
                        ? (v) => setState(() => _kind = v!)
                        : null,
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _stop,
                    maxLength: 1000,
                    decoration: InputDecoration(labelText: s.proactiveStopWhen),
                  ),
                  TextFormField(
                    controller: _capacity,
                    keyboardType: TextInputType.number,
                    decoration: InputDecoration(
                      labelText: _kind == 'goal_capacity'
                          ? s.proactiveWeeklyBudget
                          : s.proactiveCapacity,
                    ),
                    validator: (value) {
                      if (value == null || value.isEmpty) return null;
                      final minutes = int.tryParse(value);
                      return minutes == null || minutes < 0 || minutes > 10080
                          ? s.proactiveValidMinutes
                          : null;
                    },
                  ),
                  Wrap(
                    spacing: 8,
                    children: [
                      PillAction(
                        label: _deadline == null
                            ? s.proactiveDeadline
                            : MaterialLocalizations.of(context)
                                  .formatMediumDate(_deadline!),
                        onPressed: _pickDeadline,
                      ),
                      if (_deadline != null)
                        PillAction(
                          label: s.proactiveClearDeadline,
                          onPressed: () => setState(() => _deadline = null),
                        ),
                    ],
                  ),
                  TextFormField(
                    controller: _notes,
                    minLines: 2,
                    maxLines: 6,
                    maxLength: 4000,
                    decoration: InputDecoration(
                      labelText: s.proactiveCaseNotes,
                    ),
                  ),
                  GroupLabel(s.proactiveSteps),
                  for (final step in _steps)
                    FeatureRow(
                      icon: PhosphorIconsRegular.listChecks,
                      title: asJsonString(step['title']) ?? '',
                      actions: [
                        PillAction(
                          label: s.proactiveRemove,
                          onPressed: () => setState(() {
                            _steps.remove(step);
                            for (final other in _steps) {
                              other['dependsOn'] = jsonStrings(
                                other['dependsOn'],
                              ).where((id) => id != step['id']).toList();
                            }
                          }),
                        ),
                      ],
                    ),
                  PillAction(label: s.proactiveAddStep, onPressed: _addStep),
                  GroupLabel(
                    _kind == 'trip'
                        ? s.proactiveTripChanges
                        : _kind == 'research'
                        ? s.proactiveImportantClaims
                        : s.proactiveAssumptions,
                  ),
                  for (final assumption in _assumptions)
                    FeatureRow(
                      icon: PhosphorIconsRegular.eye,
                      title: asJsonString(assumption['statement']) ?? '',
                      actions: [
                        PillAction(
                          label: s.proactiveRemove,
                          onPressed: () =>
                              setState(() => _assumptions.remove(assumption)),
                        ),
                      ],
                    ),
                  PillAction(
                    label: _kind == 'trip'
                        ? s.proactiveAddTripMonitor
                        : _kind == 'research'
                        ? s.proactiveAddClaim
                        : s.proactiveAddAssumption,
                    onPressed: _addAssumption,
                  ),
                  Text(s.proactiveEnableExplanation),
                  ExpansionTile(
                    tilePadding: EdgeInsets.zero,
                    title: Text(s.proactiveCheckSettings),
                    children: [
                      Text(s.proactiveCheckSettingsHelp),
                      if (_kind == 'goal_capacity')
                        CheckboxListTile(
                          contentPadding: EdgeInsets.zero,
                          value: _useCalendarCapacity,
                          title: Text(s.proactiveCalendarCapacity),
                          subtitle: Text(s.proactiveCalendarCapacityHelp),
                          onChanged: widget.value?['profileId'] != null
                              ? null
                              : (value) => setState(
                                  () => _useCalendarCapacity = value ?? false,
                                ),
                        ),
                      const SizedBox(height: 16),
                      _numberSetting(_cadence, s.proactiveCadence, 15, 10080),
                      _numberSetting(
                        _dailyChecks,
                        s.proactiveDailyChecks,
                        1,
                        96,
                      ),
                      _numberSetting(
                        _optionalItems,
                        s.proactiveOptionalItems,
                        0,
                        20,
                      ),
                      _numberSetting(_quietStart, s.proactiveQuietStart, 0, 23),
                      _numberSetting(_quietEnd, s.proactiveQuietEnd, 0, 23),
                      TextFormField(
                        controller: _timeZone,
                        decoration: InputDecoration(
                          labelText: s.proactiveTimeZone,
                        ),
                        validator: (v) => v == null || v.trim().isEmpty
                            ? s.proactiveRequired
                            : null,
                      ),
                      Wrap(
                        spacing: 8,
                        children: [
                          PillAction(
                            label: _expires == null
                                ? s.proactiveExpiry
                                : '${s.proactiveExpiry}: ${MaterialLocalizations.of(context).formatMediumDate(_expires!)}',
                            onPressed: () =>
                                unawaited(_pickDeadline(expiry: true)),
                          ),
                          if (_expires != null)
                            PillAction(
                              label: s.proactiveRemove,
                              onPressed: () => setState(() => _expires = null),
                            ),
                        ],
                      ),
                    ],
                  ),
                  const SizedBox(height: 16),
                  GroupLabel(s.proactiveSources),
                  if (_selected.isNotEmpty)
                    DropdownButtonFormField<String>(
                      key: ValueKey(_root ?? _selected.firstOrNull),
                      initialValue: _root ?? _selected.firstOrNull,
                      isExpanded: true,
                      decoration: InputDecoration(
                        labelText: s.proactiveMainSource,
                      ),
                      items: [
                        for (final ref in _selected)
                          DropdownMenuItem(
                            value: ref,
                            child: Text(
                              asJsonString(
                                    _sources
                                        .where((s) => s['ref'] == ref)
                                        .firstOrNull?['title'],
                                  ) ??
                                  ref,
                            ),
                          ),
                      ],
                      onChanged: (ref) => setState(() => _root = ref),
                    ),
                  for (final source in _sources)
                    CheckboxListTile(
                      contentPadding: EdgeInsets.zero,
                      value: _selected.contains(source['ref']),
                      title: Text(asJsonString(source['title']) ?? ''),
                      onChanged: _saving
                          ? null
                          : (checked) => setState(() {
                              final ref = asJsonString(source['ref']);
                              if (ref == null) return;
                              if (checked == true) {
                                _selected.add(ref);
                              } else {
                                _selected.remove(ref);
                                if (_root == ref) _root = _selected.firstOrNull;
                                for (final step in _steps) {
                                  if (step['sourceRef'] == ref) {
                                    step['sourceRef'] = null;
                                  }
                                }
                                _assumptions.removeWhere(
                                  (a) =>
                                      a['sourceRef'] == ref ||
                                      a['tripRef'] == ref,
                                );
                              }
                            }),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _numberSetting(
    TextEditingController controller,
    String label,
    int minimum,
    int maximum,
  ) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: TextFormField(
      controller: controller,
      keyboardType: TextInputType.number,
      decoration: InputDecoration(labelText: '$label ($minimum–$maximum)'),
      validator: (value) {
        final number = int.tryParse(value ?? '');
        return number == null || number < minimum || number > maximum
            ? context.l10n.proactiveRequired
            : null;
      },
    ),
  );
}

class _StepEditor extends StatefulWidget {
  const _StepEditor({required this.sources, required this.steps});
  final List<Map<String, dynamic>> sources;
  final List<Map<String, dynamic>> steps;
  @override
  State<_StepEditor> createState() => _StepEditorState();
}

class _AssumptionEditor extends StatefulWidget {
  const _AssumptionEditor({
    required this.sources,
    this.tripMode = false,
    this.researchMode = false,
  });
  final List<Map<String, dynamic>> sources;
  final bool tripMode;
  final bool researchMode;
  @override
  State<_AssumptionEditor> createState() => _AssumptionEditorState();
}

class _AssumptionEditorState extends State<_AssumptionEditor> {
  final _statement = TextEditingController();
  final _threshold = TextEditingController();
  final _form = GlobalKey<FormState>();
  String? _source;
  String? _comparison;
  late bool _bindTrip = widget.tripMode;
  String? _tripItem;
  @override
  void dispose() {
    _statement.dispose();
    _threshold.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = context.l10n;
    return AlertDialog(
      title: Text(
        widget.tripMode
            ? s.proactiveAddTripMonitor
            : widget.researchMode
            ? s.proactiveAddClaim
            : s.proactiveAddAssumption,
      ),
      content: SizedBox(
        width: 440,
        child: SingleChildScrollView(
          child: Form(
            key: _form,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  _bindTrip
                      ? s.proactiveTripDelayHelp
                      : widget.researchMode
                      ? s.proactiveClaimHelp
                      : s.proactiveAssumptionHelp,
                ),
                if (widget.tripMode)
                  CheckboxListTile(
                    value: _bindTrip,
                    title: Text(s.proactiveShiftByDelay),
                    onChanged: (value) => setState(() {
                      _bindTrip = value ?? false;
                      _source = null;
                      _comparison = null;
                    }),
                  ),
                TextFormField(
                  controller: _statement,
                  maxLength: 500,
                  decoration: InputDecoration(
                    labelText: widget.researchMode
                        ? s.proactiveClaimStatement
                        : s.proactiveAssumptionStatement,
                  ),
                  validator: (v) => v == null || v.trim().isEmpty
                      ? s.proactiveRequired
                      : null,
                ),
                DropdownButtonFormField<String>(
                  key: ValueKey('assumption-source-$_bindTrip'),
                  initialValue: _source,
                  isExpanded: true,
                  decoration: InputDecoration(labelText: s.proactiveSource),
                  items: [
                    for (final source in widget.sources.where(
                      (source) => _bindTrip
                          ? asJsonString(source['ref'])?.startsWith('watch:') ==
                                true
                          : !widget.researchMode ||
                                asJsonString(source['url']) != null,
                    ))
                      DropdownMenuItem(
                        value: asJsonString(source['ref']),
                        child: Text(
                          asJsonString(source['title']) ?? '',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                  onChanged: (value) => setState(() => _source = value),
                  validator: (v) => v == null ? s.proactiveRequired : null,
                ),
                if (_bindTrip)
                  DropdownButtonFormField<String>(
                    key: const Key('trip-delay-item'),
                    isExpanded: true,
                    decoration: InputDecoration(labelText: s.proactiveTripItem),
                    items: [
                      for (final trip in widget.sources)
                        for (final item in jsonMaps(
                          trip['tripItems'],
                        ).where((item) => item['verified'] == true))
                          DropdownMenuItem(
                            value: '${trip['ref']}|${item['id']}',
                            child: Text(
                              '${trip['title']} · ${item['title']}',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                    ],
                    onChanged: (value) => setState(() => _tripItem = value),
                    validator: (v) => v == null ? s.proactiveRequired : null,
                  ),
                if (!_bindTrip && !widget.researchMode)
                  DropdownButtonFormField<String>(
                    isExpanded: true,
                    initialValue: 'changed',
                    decoration: InputDecoration(
                      labelText: s.proactiveReviewWhen,
                    ),
                    items: [
                      DropdownMenuItem(
                        value: 'changed',
                        child: Text(s.proactiveSourceChanges),
                      ),
                      DropdownMenuItem(
                        value: 'gt',
                        child: Text(s.proactiveAbove),
                      ),
                      DropdownMenuItem(
                        value: 'lt',
                        child: Text(s.proactiveBelow),
                      ),
                    ],
                    onChanged: (v) =>
                        setState(() => _comparison = v == 'changed' ? null : v),
                  ),
                if (_comparison != null)
                  TextFormField(
                    controller: _threshold,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                      signed: true,
                    ),
                    decoration: InputDecoration(
                      labelText: s.proactiveThreshold,
                    ),
                    validator: (v) => double.tryParse(v ?? '')?.isFinite == true
                        ? null
                        : s.proactiveRequired,
                  ),
              ],
            ),
          ),
        ),
      ),
      actions: [
        PillAction(
          label: s.proactiveCancel,
          onPressed: () => Navigator.pop(context),
        ),
        PillAction(
          label: s.proactiveSave,
          onPressed: () {
            if (!(_form.currentState?.validate() ?? false)) return;
            Navigator.pop(context, {
              'id': DateTime.now().microsecondsSinceEpoch.toString(),
              'statement': _statement.text.trim(),
              'sourceRef': _source,
              'comparison': _comparison,
              'threshold': _comparison == null
                  ? null
                  : double.tryParse(_threshold.text),
              if (_bindTrip && _tripItem != null) ...{
                'effect': 'delay_minutes',
                'tripRef': _tripItem!.substring(0, _tripItem!.indexOf('|')),
                'tripItemId': _tripItem!.substring(_tripItem!.indexOf('|') + 1),
              },
            });
          },
        ),
      ],
    );
  }
}

class _StepEditorState extends State<_StepEditor> {
  final _title = TextEditingController();
  final _minutes = TextEditingController();
  final _condition = TextEditingController();
  final _form = GlobalKey<FormState>();
  final Set<String> _dependencies = {};
  String? _source;
  @override
  void dispose() {
    _title.dispose();
    _minutes.dispose();
    _condition.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = context.l10n;
    return AlertDialog(
      title: Text(s.proactiveAddStep),
      content: SizedBox(
        width: 440,
        child: SingleChildScrollView(
          child: Form(
            key: _form,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                TextFormField(
                  controller: _title,
                  maxLength: 300,
                  decoration: InputDecoration(labelText: s.proactiveName),
                  validator: (v) => v == null || v.trim().isEmpty
                      ? s.proactiveRequired
                      : null,
                ),
                TextFormField(
                  controller: _minutes,
                  keyboardType: TextInputType.number,
                  decoration: InputDecoration(labelText: s.proactiveEffort),
                  validator: (v) {
                    if (v == null || v.isEmpty) return null;
                    final n = int.tryParse(v);
                    return n == null || n < 1 || n > 10080
                        ? s.proactiveValidMinutes
                        : null;
                  },
                ),
                TextFormField(
                  controller: _condition,
                  maxLength: 500,
                  decoration: InputDecoration(labelText: s.proactiveCompletion),
                ),
                DropdownButtonFormField<String>(
                  isExpanded: true,
                  decoration: InputDecoration(labelText: s.proactiveSource),
                  items: [
                    for (final source in widget.sources)
                      DropdownMenuItem(
                        value: asJsonString(source['ref']),
                        child: Text(asJsonString(source['title']) ?? ''),
                      ),
                  ],
                  onChanged: (value) => setState(() => _source = value),
                ),
                if (widget.steps.isNotEmpty) Text(s.proactiveDependsOn),
                for (final step in widget.steps)
                  CheckboxListTile(
                    contentPadding: EdgeInsets.zero,
                    title: Text(asJsonString(step['title']) ?? ''),
                    value: _dependencies.contains(step['id']),
                    onChanged: (checked) => setState(() {
                      final id = asJsonString(step['id']);
                      if (id == null) return;
                      if (checked == true) {
                        _dependencies.add(id);
                      } else {
                        _dependencies.remove(id);
                      }
                    }),
                  ),
              ],
            ),
          ),
        ),
      ),
      actions: [
        PillAction(
          label: s.proactiveCancel,
          onPressed: () => Navigator.of(context).pop(),
        ),
        PillAction(
          label: s.proactiveSave,
          onPressed: () {
            if (!(_form.currentState?.validate() ?? false)) return;
            Navigator.of(context).pop({
              'id': DateTime.now().microsecondsSinceEpoch.toString(),
              'title': _title.text.trim(),
              'minutes': int.tryParse(_minutes.text),
              'completionCondition': _condition.text.trim(),
              'sourceRef': _source,
              'dependsOn': _dependencies.toList(),
              'done': false,
            });
          },
        ),
      ],
    );
  }
}
