import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// One comparison: the ranking (computed by the server, so a saved comparison always gives the same answer), what is
/// still unknown, the weights you can change, each option's evidence, and a way to save or decide.
class ComparisonScreen extends StatefulWidget {
  const ComparisonScreen({
    required this.http,
    required this.comparisonId,
    super.key,
  });

  final Dio http;
  final String comparisonId;

  @override
  State<ComparisonScreen> createState() => _ComparisonScreenState();
}

class _ComparisonScreenState extends State<ComparisonScreen> {
  Map<String, dynamic>? _data;
  bool _loading = true;
  bool _missing = false;
  bool _busy = false;
  String? _error;

  String get _base => '/api/v1/comparisons/${widget.comparisonId}';

  Map<String, dynamic> get _definition =>
      jsonObject(_data?['definition']) ?? const {};
  Map<String, dynamic> get _result => jsonObject(_data?['result']) ?? const {};
  List<Map<String, dynamic>> get _criteria => jsonMaps(_definition['criteria']);
  List<Map<String, dynamic>> get _options => jsonMaps(_definition['options']);

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(_base);
      if (!mounted) return;
      setState(() {
        _data = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _missing = error.response?.statusCode == 404;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this comparison.';
      });
    }
  }

  void _say(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  /// Sends an edited definition and takes the server's ranking back.
  Future<void> _save(Map<String, dynamic> definition) async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      final response = await widget.http.put<dynamic>(
        _base,
        data: {'definition': definition},
      );
      if (mounted) setState(() => _data = jsonObject(response.data));
    } on DioException catch (error) {
      if (mounted) {
        _say(
          firstProblemMessage(error.response?.data) ??
              'Could not save that change.',
        );
        await _load();
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Map<String, dynamic> _copyDefinition() => {
    'title': _definition['title'],
    'question': _definition['question'],
    'criteria': [
      for (final item in _criteria) {...item},
    ],
    'options': [
      for (final option in _options)
        {
          ...option,
          'cells': {
            for (final entry
                in (jsonObject(option['cells']) ?? const {}).entries)
              entry.key: jsonObject(entry.value) ?? const {},
          },
        },
    ],
  };

  Future<void> _setWeight(String criterionId, int weight) {
    final definition = _copyDefinition();
    for (final criterion in definition['criteria'] as List) {
      if ((criterion as Map)['id'] == criterionId) criterion['weight'] = weight;
    }
    return _save(definition);
  }

  Future<void> _editCell(
    Map<String, dynamic> option,
    Map<String, dynamic> criterion,
  ) async {
    final cells = jsonObject(option['cells']) ?? const {};
    final existing = jsonObject(cells[criterion['id']]);
    final edited = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _CellSheet(
        option: asJsonString(option['name']) ?? '',
        criterion: asJsonString(criterion['name']) ?? '',
        cell: existing,
      ),
    );
    if (edited == null || !mounted) return;
    final definition = _copyDefinition();
    for (final candidate in definition['options'] as List) {
      if ((candidate as Map)['id'] == option['id']) {
        (candidate['cells'] as Map)[criterion['id']] = edited;
      }
    }
    await _save(definition);
  }

  Future<void> _addNamed({required bool option}) async {
    final name = await showJarvisDialog<String>(
      context: context,
      builder: (_) => _NameDialog(
        title: option ? 'Add an option' : 'Add a criterion',
        label: option ? 'Name' : 'What matters',
      ),
    );
    if (name == null || name.trim().isEmpty || !mounted) return;
    final definition = _copyDefinition();
    final list = definition[option ? 'options' : 'criteria'] as List;
    final used = {for (final item in list) (item as Map)['id']};
    var slug = name
        .toLowerCase()
        .replaceAll(RegExp(r'[^a-z0-9]+'), '-')
        .replaceAll(RegExp(r'^-+|-+$'), '');
    if (slug.isEmpty) slug = 'item';
    if (slug.length > 24) slug = slug.substring(0, 24);
    var id = slug;
    for (var n = 2; used.contains(id); n++) {
      id = '$slug-$n';
    }
    list.add(
      option
          ? {'id': id, 'name': name.trim(), 'summary': null, 'cells': {}}
          : {'id': id, 'name': name.trim(), 'weight': 5},
    );
    await _save(definition);
  }

  Future<void> _remove(String listKey, String id, String name) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove $name?',
      message: listKey == 'options'
          ? 'Its answers go with it.'
          : 'Every option’s answer to it goes with it.',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    final definition = _copyDefinition();
    (definition[listKey] as List).removeWhere(
      (item) => (item as Map)['id'] == id,
    );
    await _save(definition);
  }

  /// A copy without the criteria marked private, ranked again without them. Only copied; nothing is sent.
  Future<void> _copyShareable() async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      final response = await widget.http.get<dynamic>('$_base/export');
      final body = jsonObject(response.data);
      await Clipboard.setData(
        ClipboardData(text: asJsonString(body?['markdown']) ?? ''),
      );
      if (mounted) {
        _say(
          asJsonBool(body?['leftOut'])
              ? 'Copied. Private criteria were left out and the ranking worked out without them.'
              : 'Copied. Nothing was marked private, so this is the full comparison.',
        );
      }
    } on DioException catch (error) {
      if (mounted) {
        _say(
          firstProblemMessage(error.response?.data) ??
              'Could not make a shareable copy.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _setPrivate(String criterionId, bool value) {
    final definition = _copyDefinition();
    for (final criterion in definition['criteria'] as List) {
      if ((criterion as Map)['id'] == criterionId) criterion['private'] = value;
    }
    return _save(definition);
  }

  Future<void> _saveToLibrary() async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      await widget.http.post<dynamic>('$_base/library');
      if (mounted) _say('Saved to your Library as a report.');
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        _say(
          firstProblemMessage(error.response?.data) ??
              'Could not save to the Library.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _decide(Map<String, dynamic> option) async {
    final choice = await showModalBottomSheet<({int percent, DateTime on})>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _DecideSheet(option: asJsonString(option['name']) ?? ''),
    );
    if (choice == null || !mounted) return;
    final on = choice.on;
    final iso =
        '${on.year.toString().padLeft(4, '0')}-${on.month.toString().padLeft(2, '0')}-${on.day.toString().padLeft(2, '0')}';
    try {
      await widget.http.post<dynamic>(
        '$_base/decide',
        data: {
          'optionId': option['id'],
          'probability': choice.percent / 100,
          'reviewOn': iso,
        },
      );
      if (mounted) {
        _say('Logged as a decision. Jarvis will ask you on $iso how it went.');
        await _load();
      }
    } on DioException catch (error) {
      if (mounted) {
        _say(
          firstProblemMessage(error.response?.data) ??
              'Could not log the decision.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: PageTitle(
        _loading ? 'Comparison' : (asJsonString(_definition['title']) ?? ''),
        overflow: TextOverflow.ellipsis,
      ),
      actions: [
        if (_data != null)
          PopupMenuButton<String>(
            tooltip: 'More',
            enabled: !_busy,
            icon: const Icon(PhosphorIconsRegular.dotsThree, size: 22),
            onSelected: (value) {
              switch (value) {
                case 'option':
                  unawaited(_addNamed(option: true));
                case 'criterion':
                  unawaited(_addNamed(option: false));
                case 'library':
                  unawaited(_saveToLibrary());
                case 'share':
                  unawaited(_copyShareable());
              }
            },
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'option', child: Text('Add an option')),
              PopupMenuItem(value: 'criterion', child: Text('Add a criterion')),
              PopupMenuItem(value: 'library', child: Text('Save to Library')),
              PopupMenuItem(
                value: 'share',
                child: Text('Copy a shareable version'),
              ),
            ],
          ),
      ],
    ),
    body: _body(),
  );

  Widget _body() {
    if (_loading && _data == null) return const SkeletonList();
    if (_missing) {
      return const EmptyState(
        icon: PhosphorIconsRegular.sliders,
        title: 'This comparison is gone',
      );
    }
    if (_data == null) {
      return ErrorState(message: _error ?? 'Could not load.', onRetry: _load);
    }
    final colors = JarvisColors.of(context);
    final rows = jsonMaps(_result['rows']);
    final leaderId = asJsonString(_result['leaderId']);
    final warnings = [
      for (final warning in _result['warnings'] as List? ?? const [])
        if (warning is String) warning,
    ];
    final flips = [
      for (final item in jsonMaps(_result['sensitivity']))
        if (asJsonBool(item['changesLeader'])) item,
    ];
    final decidedId = asJsonString(_definition['chosenOptionId']);
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 40),
      children: [
        ContentWidth(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (asJsonString(_definition['question']) case final question?)
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text(
                    question,
                    style: TextStyle(fontSize: 15, color: colors.inkSoft),
                  ),
                ),
              Text('Ranking', style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8),
              for (final row in rows)
                _RankRow(
                  key: Key('comparison-rank-${row['optionId']}'),
                  row: row,
                  leader: row['optionId'] == leaderId,
                  chosen: row['optionId'] == decidedId,
                ),
              for (final warning in warnings)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: InlineNotice(message: warning, tone: NoticeTone.info),
                ),
              if (flips.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: InlineNotice(
                    key: const Key('comparison-sensitivity'),
                    message:
                        'What could change the answer: ${[for (final flip in flips) 'without “${flip['criterionName']}” ${flip['leaderWithout']} would lead'].join('; ')}.',
                    tone: NoticeTone.info,
                  ),
                ),
              const SizedBox(height: 20),
              Text(
                'What matters',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 4),
              for (final criterion in _criteria)
                _WeightRow(
                  key: Key('comparison-weight-${criterion['id']}'),
                  criterion: criterion,
                  onChanged: (weight) =>
                      unawaited(_setWeight(criterion['id'] as String, weight)),
                  onPrivate: (value) =>
                      unawaited(_setPrivate(criterion['id'] as String, value)),
                  onRemove: () => unawaited(
                    _remove(
                      'criteria',
                      criterion['id'] as String,
                      asJsonString(criterion['name']) ?? 'criterion',
                    ),
                  ),
                ),
              const SizedBox(height: 20),
              Text(
                'What is known',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 8),
              for (final option in _options)
                _OptionCard(
                  key: Key('comparison-option-${option['id']}'),
                  option: option,
                  criteria: _criteria,
                  chosen: option['id'] == decidedId,
                  decided: decidedId != null,
                  onCell: (criterion) =>
                      unawaited(_editCell(option, criterion)),
                  onDecide: () => unawaited(_decide(option)),
                  onRemove: () => unawaited(
                    _remove(
                      'options',
                      option['id'] as String,
                      asJsonString(option['name']) ?? 'option',
                    ),
                  ),
                ),
              if (asJsonString(_data?['libraryItemId']) != null)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    'Saved to your Library.',
                    style: TextStyle(fontSize: 13, color: colors.muted),
                  ),
                ),
            ],
          ),
        ),
      ],
    );
  }
}

String _score(Object? value) {
  final number = value is num ? value.toDouble() : null;
  if (number == null) return 'Unknown';
  return number == number.roundToDouble()
      ? number.toStringAsFixed(0)
      : number.toStringAsFixed(2).replaceFirst(RegExp(r'0+$'), '');
}

class _RankRow extends StatelessWidget {
  const _RankRow({
    required this.row,
    required this.leader,
    required this.chosen,
    super.key,
  });

  final Map<String, dynamic> row;
  final bool leader;
  final bool chosen;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final score = row['score'];
    final coverage = ((row['coverage'] as num?) ?? 0).toDouble();
    final unknown = [
      for (final name in row['unknownCriteria'] as List? ?? const [])
        if (name is String) name,
    ];
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: SurfaceCard(
        color: leader ? colors.accentSoft : null,
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Padding(
                  padding: const EdgeInsets.only(right: 12),
                  child: Text(
                    score == null ? '–' : '${row['rank']}',
                    style: TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w700,
                      color: colors.ink,
                    ),
                  ),
                ),
                // A Wrap, not a Row: at large text the score moves under the name instead of squeezing it.
                Expanded(
                  child: Wrap(
                    alignment: WrapAlignment.spaceBetween,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    runSpacing: 2,
                    children: [
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: Text(
                          asJsonString(row['name']) ?? '',
                          style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w600,
                            color: colors.ink,
                          ),
                        ),
                      ),
                      Text(
                        score == null ? 'Unknown' : '${_score(score)} / 10',
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w600,
                          color: colors.ink,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Wrap(
              spacing: 6,
              runSpacing: 4,
              children: [
                if (chosen) StatusPill(label: 'Chosen', color: colors.accent),
                if (asJsonBool(row['tied']))
                  StatusPill(label: 'Tied', color: colors.warning),
                if (asJsonBool(row['thin']))
                  StatusPill(label: 'Mostly unknown', color: colors.warning),
                Text(
                  '${(coverage * 100).round()}% of what you care about is known',
                  style: TextStyle(fontSize: 12.5, color: colors.muted),
                ),
              ],
            ),
            if (unknown.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  'Unknown: ${unknown.join(', ')}',
                  style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _WeightRow extends StatefulWidget {
  const _WeightRow({
    required this.criterion,
    required this.onChanged,
    required this.onRemove,
    required this.onPrivate,
    super.key,
  });

  final Map<String, dynamic> criterion;
  final ValueChanged<bool> onPrivate;
  final ValueChanged<int> onChanged;
  final VoidCallback onRemove;

  @override
  State<_WeightRow> createState() => _WeightRowState();
}

class _WeightRowState extends State<_WeightRow> {
  late double _weight = asJsonInt(widget.criterion['weight']).toDouble();

  @override
  void didUpdateWidget(_WeightRow oldWidget) {
    super.didUpdateWidget(oldWidget);
    _weight = asJsonInt(widget.criterion['weight']).toDouble();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final name = asJsonString(widget.criterion['name']) ?? '';
    return Row(
      children: [
        Expanded(
          flex: 3,
          child: Text(
            name,
            style: TextStyle(fontSize: 14.5, color: colors.ink),
          ),
        ),
        Expanded(
          flex: 4,
          child: Slider(
            key: Key('comparison-slider-${widget.criterion['id']}'),
            value: _weight,
            min: 0,
            max: 10,
            divisions: 10,
            label: '${_weight.round()}',
            semanticFormatterCallback: (value) =>
                '$name matters ${value.round()} out of 10',
            onChanged: (value) => setState(() => _weight = value),
            onChangeEnd: (value) => widget.onChanged(value.round()),
          ),
        ),
        SizedBox(
          width: 24,
          child: Text(
            '${_weight.round()}',
            textAlign: TextAlign.end,
            style: TextStyle(fontWeight: FontWeight.w600, color: colors.ink),
          ),
        ),
        IconButton(
          key: Key('comparison-private-${widget.criterion['id']}'),
          tooltip: asJsonBool(widget.criterion['private'])
              ? 'Private: left out of a shared copy. Tap to include it.'
              : 'Shared. Tap to keep $name out of a shared copy.',
          onPressed: () =>
              widget.onPrivate(!asJsonBool(widget.criterion['private'])),
          icon: Icon(
            asJsonBool(widget.criterion['private'])
                ? PhosphorIconsRegular.eyeSlash
                : PhosphorIconsRegular.eye,
            size: 18,
            color: asJsonBool(widget.criterion['private'])
                ? colors.accent
                : colors.muted,
          ),
        ),
        IconButton(
          tooltip: 'Remove $name',
          onPressed: widget.onRemove,
          icon: Icon(PhosphorIconsRegular.x, size: 16, color: colors.muted),
        ),
      ],
    );
  }
}

class _OptionCard extends StatelessWidget {
  const _OptionCard({
    required this.option,
    required this.criteria,
    required this.chosen,
    required this.decided,
    required this.onCell,
    required this.onDecide,
    required this.onRemove,
    super.key,
  });

  final Map<String, dynamic> option;
  final List<Map<String, dynamic>> criteria;
  final bool chosen;
  final bool decided;
  final ValueChanged<Map<String, dynamic>> onCell;
  final VoidCallback onDecide;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final cells = jsonObject(option['cells']) ?? const {};
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: SurfaceCard(
        padding: const EdgeInsets.fromLTRB(14, 12, 6, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    asJsonString(option['name']) ?? '',
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.w600,
                      color: colors.ink,
                    ),
                  ),
                ),
                IconButton(
                  tooltip: 'Remove ${option['name']}',
                  onPressed: onRemove,
                  icon: Icon(
                    PhosphorIconsRegular.x,
                    size: 16,
                    color: colors.muted,
                  ),
                ),
              ],
            ),
            for (final criterion in criteria)
              _CellRow(
                key: Key('cell-${option['id']}-${criterion['id']}'),
                criterion: asJsonString(criterion['name']) ?? '',
                cell: jsonObject(cells[criterion['id']]),
                onTap: () => onCell(criterion),
              ),
            Align(
              alignment: Alignment.centerRight,
              child: chosen
                  ? Padding(
                      padding: const EdgeInsets.only(right: 8, bottom: 6),
                      child: StatusPill(
                        label: 'Your choice, logged as a decision',
                        color: colors.accent,
                      ),
                    )
                  : TextButton(
                      key: Key('comparison-choose-${option['id']}'),
                      onPressed: decided ? null : onDecide,
                      child: Text(
                        decided ? 'Already decided' : 'Choose this one',
                      ),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class _CellRow extends StatelessWidget {
  const _CellRow({
    required this.criterion,
    required this.cell,
    required this.onTap,
    super.key,
  });

  final String criterion;
  final Map<String, dynamic>? cell;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final score = cell?['score'];
    final note = asJsonString(cell?['note']);
    final source = asJsonString(cell?['source']);
    final asOf = asJsonString(cell?['asOf']);
    return InkWell(
      onTap: onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 44),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 6, horizontal: 2),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      criterion,
                      style: TextStyle(fontSize: 13, color: colors.inkSoft),
                    ),
                    if (note != null)
                      Text(
                        note,
                        style: TextStyle(fontSize: 14, color: colors.ink),
                      ),
                    if (source != null || asOf != null)
                      Text(
                        [?source, if (asOf != null) 'seen $asOf'].join(' · '),
                        style: TextStyle(fontSize: 12, color: colors.muted),
                      ),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              Text(
                score == null ? 'Unknown' : '${_score(score)}/10',
                style: TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.w600,
                  color: score == null ? colors.muted : colors.ink,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _CellSheet extends StatefulWidget {
  const _CellSheet({
    required this.option,
    required this.criterion,
    required this.cell,
  });

  final String option;
  final String criterion;
  final Map<String, dynamic>? cell;

  @override
  State<_CellSheet> createState() => _CellSheetState();
}

class _CellSheetState extends State<_CellSheet> {
  late bool _known = widget.cell?['score'] != null;
  late double _score = ((widget.cell?['score'] as num?) ?? 5).toDouble();
  late final _note = TextEditingController(
    text: asJsonString(widget.cell?['note']) ?? '',
  );
  late final _source = TextEditingController(
    text: asJsonString(widget.cell?['source']) ?? '',
  );
  late final _asOf = TextEditingController(
    text: asJsonString(widget.cell?['asOf']) ?? '',
  );

  @override
  void dispose() {
    _note.dispose();
    _source.dispose();
    _asOf.dispose();
    super.dispose();
  }

  bool get _dateOk =>
      _asOf.text.trim().isEmpty ||
      RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(_asOf.text.trim());

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            '${widget.option} · ${widget.criterion}',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          SwitchListTile(
            key: const Key('cell-known'),
            contentPadding: EdgeInsets.zero,
            title: const Text('I know this'),
            subtitle: const Text(
              'Leave it off and it stays unknown, never zero.',
            ),
            value: _known,
            onChanged: (value) => setState(() => _known = value),
          ),
          if (_known)
            Row(
              children: [
                Expanded(
                  child: Slider(
                    key: const Key('cell-score'),
                    value: _score,
                    min: 0,
                    max: 10,
                    divisions: 20,
                    label: _score.toStringAsFixed(1),
                    semanticFormatterCallback: (value) =>
                        '${value.toStringAsFixed(1)} out of 10',
                    onChanged: (value) => setState(() => _score = value),
                  ),
                ),
                SizedBox(
                  width: 44,
                  child: Text(
                    _score.toStringAsFixed(1),
                    textAlign: TextAlign.end,
                  ),
                ),
              ],
            ),
          TextField(
            key: const Key('cell-note'),
            controller: _note,
            maxLength: 300,
            decoration: const InputDecoration(
              labelText: 'The facts',
              hintText: '€1,900 including fitting',
            ),
          ),
          TextField(
            key: const Key('cell-source'),
            controller: _source,
            maxLength: 300,
            decoration: const InputDecoration(
              labelText: 'Where it came from',
              hintText: 'quote-a.pdf',
            ),
          ),
          TextField(
            key: const Key('cell-asof'),
            controller: _asOf,
            onChanged: (_) => setState(() {}),
            decoration: InputDecoration(
              labelText: 'Seen on (yyyy-mm-dd)',
              errorText: _dateOk ? null : 'Use the form 2026-10-01',
            ),
          ),
          const SizedBox(height: 12),
          FilledButton(
            key: const Key('cell-save'),
            onPressed: _dateOk
                ? () => Navigator.pop<Map<String, Object?>>(context, {
                    'score': _known
                        ? double.parse(_score.toStringAsFixed(2))
                        : null,
                    'note': _note.text.trim().isEmpty
                        ? null
                        : _note.text.trim(),
                    'source': _source.text.trim().isEmpty
                        ? null
                        : _source.text.trim(),
                    'asOf': _asOf.text.trim().isEmpty
                        ? null
                        : _asOf.text.trim(),
                  })
                : null,
            child: const Text('Save'),
          ),
        ],
      ),
    ),
  );
}

class _DecideSheet extends StatefulWidget {
  const _DecideSheet({required this.option});

  final String option;

  @override
  State<_DecideSheet> createState() => _DecideSheetState();
}

class _DecideSheetState extends State<_DecideSheet> {
  double _percent = 70;
  DateTime _on = DateTime.now().add(const Duration(days: 30));

  @override
  Widget build(BuildContext context) => SafeArea(
    child: Padding(
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Choose ${widget.option}',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 4),
          const Text(
            'This logs a decision so Jarvis can ask later how it went. '
            'Nothing is bought or sent.',
          ),
          const SizedBox(height: 12),
          Text('How sure are you it is the right one? ${_percent.round()}%'),
          Slider(
            key: const Key('decide-percent'),
            value: _percent,
            min: 1,
            max: 99,
            divisions: 98,
            label: '${_percent.round()}%',
            onChanged: (value) => setState(() => _percent = value),
          ),
          ListTile(
            key: const Key('decide-date'),
            contentPadding: EdgeInsets.zero,
            title: const Text('Check how it went on'),
            subtitle: Text(
              '${_on.year}-${_on.month.toString().padLeft(2, '0')}-${_on.day.toString().padLeft(2, '0')}',
            ),
            trailing: const Icon(PhosphorIconsRegular.calendarBlank),
            onTap: () async {
              final picked = await showDatePicker(
                context: context,
                initialDate: _on,
                firstDate: DateTime.now(),
                lastDate: DateTime.now().add(const Duration(days: 700)),
              );
              if (picked != null) setState(() => _on = picked);
            },
          ),
          const SizedBox(height: 8),
          FilledButton(
            key: const Key('decide-save'),
            onPressed: () =>
                Navigator.pop(context, (percent: _percent.round(), on: _on)),
            child: const Text('Log my choice'),
          ),
        ],
      ),
    ),
  );
}

class _NameDialog extends StatefulWidget {
  const _NameDialog({required this.title, required this.label});

  final String title;
  final String label;

  @override
  State<_NameDialog> createState() => _NameDialogState();
}

class _NameDialogState extends State<_NameDialog> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: TextField(
      key: const Key('comparison-name-field'),
      controller: _controller,
      autofocus: true,
      maxLength: 80,
      decoration: InputDecoration(labelText: widget.label),
      onSubmitted: (value) => Navigator.pop(context, value),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('comparison-name-save'),
        onPressed: () => Navigator.pop(context, _controller.text),
        child: const Text('Add'),
      ),
    ],
  );
}
