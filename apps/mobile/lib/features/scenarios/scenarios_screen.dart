import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What-if scenarios: change a few assumptions and see the consequence at
/// once. Calculating changes nothing; saving writes a Library report and, if
/// you ask, one decision.
class ScenariosScreen extends StatefulWidget {
  const ScenariosScreen({required this.http, this.now, super.key});

  final Dio http;
  final DateTime? now;

  @override
  State<ScenariosScreen> createState() => _ScenariosScreenState();
}

class _ScenariosScreenState extends State<ScenariosScreen> {
  String _kind = 'savings';
  final _title = TextEditingController();
  final _fields = <String, TextEditingController>{
    'nowMonthly': TextEditingController(text: '12.99'),
    'nextMonthly': TextEditingController(text: '9.49'),
    'months': TextEditingController(text: '24'),
    'once': TextEditingController(text: '0'),
    'currency': TextEditingController(text: 'EUR'),
    'task': TextEditingController(text: '40'),
    'nowHours': TextEditingController(text: '5'),
    'nextHours': TextEditingController(text: '8'),
    'buffer': TextEditingController(text: '20'),
  };
  Map<String, dynamic>? _result;
  String? _problem;
  bool _saving = false;
  bool _asDecision = false;
  Timer? _debounce;
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_evaluate());
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _title.dispose();
    for (final controller in _fields.values) {
      controller.dispose();
    }
    super.dispose();
  }

  double? _number(String key) =>
      double.tryParse(_fields[key]!.text.trim().replaceAll(',', '.'));

  Map<String, Object?>? _body() {
    if (_kind == 'savings') {
      final now = _number('nowMonthly');
      final next = _number('nextMonthly');
      final months = int.tryParse(_fields['months']!.text.trim());
      final once = _number('once');
      if (now == null || next == null || months == null || once == null) {
        return null;
      }
      final currency = _fields['currency']!.text.trim();
      return {
        'kind': 'savings',
        'title': _title.text.trim().isEmpty ? null : _title.text.trim(),
        'savingsBaseline': {
          'monthlyCost': now,
          'months': months,
          'oneTimeCost': 0,
          'currency': currency,
        },
        'savingsAlternative': {
          'monthlyCost': next,
          'months': months,
          'oneTimeCost': once,
          'currency': currency,
        },
      };
    }
    final task = _number('task');
    final now = _number('nowHours');
    final next = _number('nextHours');
    final buffer = _number('buffer');
    if (task == null || now == null || next == null || buffer == null) {
      return null;
    }
    return {
      'kind': 'time',
      'title': _title.text.trim().isEmpty ? null : _title.text.trim(),
      'timeBaseline': {
        'hoursPerWeek': now,
        'taskHours': task,
        'bufferPercent': buffer,
      },
      'timeAlternative': {
        'hoursPerWeek': next,
        'taskHours': task,
        'bufferPercent': buffer,
      },
    };
  }

  void _changed() {
    _debounce?.cancel();
    _debounce = Timer(
      const Duration(milliseconds: 250),
      () => unawaited(_evaluate()),
    );
  }

  Future<void> _evaluate() async {
    final body = _body();
    final revision = ++_revision;
    if (body == null) {
      setState(() {
        _result = null;
        _problem = 'Enter a number in every box.';
      });
      return;
    }
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/scenarios/evaluate',
        data: body,
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _result = jsonObject(response.data);
        _problem = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _result = null;
        _problem =
            firstProblemMessage(error.response?.data) ??
            'Could not work that out.';
      });
    }
  }

  Future<void> _save() async {
    final body = _body();
    if (body == null) return;
    final now = widget.now ?? DateTime.now();
    final review = now.add(const Duration(days: 90));
    setState(() => _saving = true);
    try {
      await widget.http.post<dynamic>(
        '/api/v1/scenarios/save',
        data: {
          'scenario': body,
          if (_asDecision) 'decisionProbability': 0.7,
          if (_asDecision)
            'decisionReviewOn':
                '${review.year.toString().padLeft(4, '0')}-${review.month.toString().padLeft(2, '0')}-${review.day.toString().padLeft(2, '0')}',
        },
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            _asDecision
                ? 'Saved to your Library and logged as a decision.'
                : 'Saved to your Library. Nothing else was changed.',
          ),
        ),
      );
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ?? 'Could not save it.',
          ),
        ),
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  /// The label sits above the box so it wraps at large text instead of being cut off.
  Widget _labelled(String label, Widget field) => Padding(
    padding: const EdgeInsets.only(top: 12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: const TextStyle(fontSize: 14)),
        const SizedBox(height: 4),
        field,
      ],
    ),
  );

  Widget _field(String key, String label, {bool decimal = true}) => _labelled(
    label,
    TextField(
      key: Key('scenario-$key'),
      controller: _fields[key],
      onChanged: (_) => _changed(),
      keyboardType: TextInputType.numberWithOptions(decimal: decimal),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final result = _result;
    return Scaffold(
      appBar: AppBar(title: const PageTitle('What if')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Wrap(
                  spacing: 8,
                  children: [
                    ChoiceChip(
                      key: const Key('scenario-kind-savings'),
                      label: const Text('A cost'),
                      selected: _kind == 'savings',
                      onSelected: (_) {
                        setState(() => _kind = 'savings');
                        unawaited(_evaluate());
                      },
                    ),
                    ChoiceChip(
                      key: const Key('scenario-kind-time'),
                      label: const Text('My time'),
                      selected: _kind == 'time',
                      onSelected: (_) {
                        setState(() => _kind = 'time');
                        unawaited(_evaluate());
                      },
                    ),
                  ],
                ),
                if (result != null) ..._outcome(result, colors),
                const GroupLabel('Your numbers'),
                _labelled(
                  'Name it (optional)',
                  TextField(
                    key: const Key('scenario-title'),
                    controller: _title,
                    maxLength: 120,
                    buildCounter: (
                      context, {
                      required currentLength,
                      required isFocused,
                      maxLength,
                    }) => null,
                  ),
                ),
                if (_kind == 'savings') ...[
                  _field('nowMonthly', 'Cost per month now'),
                  _field('nextMonthly', 'Cost per month with the alternative'),
                  _field('months', 'Months to compare', decimal: false),
                  _field('once', 'One-time cost of switching'),
                  _labelled(
                    'Currency',
                    TextField(
                      key: const Key('scenario-currency'),
                      controller: _fields['currency'],
                      onChanged: (_) => _changed(),
                      maxLength: 3,
                      textCapitalization: TextCapitalization.characters,
                      buildCounter: (
                        context, {
                        required currentLength,
                        required isFocused,
                        maxLength,
                      }) => null,
                    ),
                  ),
                ] else ...[
                  _field('task', 'Hours of work in total'),
                  _field('nowHours', 'Hours per week now'),
                  _field('nextHours', 'Hours per week with the alternative'),
                  _field('buffer', 'Extra time for the unexpected (%)'),
                ],
                const SizedBox(height: 12),
                if (_problem != null)
                  InlineNotice(
                    key: const Key('scenario-problem'),
                    tone: NoticeTone.info,
                    message: _problem!,
                  ),
                if (result != null) ..._saveBlock(),
              ],
            ),
          ),
        ],
      ),
    );
  }

  List<Widget> _saveBlock() => [
    SurfaceCard(
      margin: const EdgeInsets.only(top: 8, bottom: 14),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 2),
      child: SwitchListTile(
        key: const Key('scenario-decision'),
        contentPadding: EdgeInsets.zero,
        title: const Text('Also log it as a decision'),
        subtitle: const Text('Reviewed in 90 days'),
        value: _asDecision,
        onChanged: (value) => setState(() => _asDecision = value),
      ),
    ),
    Align(
      alignment: Alignment.centerLeft,
      child: PillAction(
        key: const Key('scenario-save'),
        primary: true,
        icon: PhosphorIconsRegular.bookmarkSimple,
        label: 'Save to Library',
        onPressed: _saving ? null : () => unawaited(_save()),
      ),
    ),
  ];

  List<Widget> _outcome(Map<String, dynamic> result, JarvisColors colors) {
    final baseline = jsonObject(result['baseline']) ?? const {};
    final alternative = jsonObject(result['alternative']) ?? const {};
    final deltas = jsonMaps(result['deltas']);
    final lead = deltas.firstOrNull;
    return [
      const GroupLabel('What changes'),
      if (lead != null)
        KeyedSubtree(
          key: const Key('scenario-result'),
          child: FadeSlideIn(
            child: HeroCard(
              label: asJsonString(lead['label']) ?? '',
              title: '${lead['value']} ${asJsonString(lead['unit']) ?? ''}'
                  .trim(),
              subtitle: asJsonString(lead['explanation']),
            ),
          ),
        ),
      const SizedBox(height: 12),
      FeatureRow(
        icon: PhosphorIconsRegular.clockCounterClockwise,
        caption: 'Now',
        title: asJsonString(baseline['summary']) ?? '',
      ),
      FeatureRow(
        index: 1,
        icon: PhosphorIconsRegular.sparkle,
        caption: 'With the change',
        title: asJsonString(alternative['summary']) ?? '',
      ),
      for (var i = 1; i < deltas.length; i++)
        FeatureRow(
          key: const Key('scenario-delta'),
          index: i + 1,
          icon: PhosphorIconsRegular.chartLine,
          caption: asJsonString(deltas[i]['label']),
          title:
              '${deltas[i]['value']} ${asJsonString(deltas[i]['unit']) ?? ''}'
                  .trim(),
          subtitle: asJsonString(deltas[i]['explanation']),
        ),
      for (final note in jsonStrings(result['notes']))
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 0, 4, 6),
          child: Text(
            note,
            style: TextStyle(color: colors.muted, fontSize: 13),
          ),
        ),
    ];
  }
}
