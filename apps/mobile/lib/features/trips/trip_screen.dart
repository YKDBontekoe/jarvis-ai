import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'trips_screen.dart' show dateKey;

/// One trip: where it stands, options with how fresh each price is, the days,
/// a checklist from the trip's facts, and what is still unknown. Choosing an
/// option books nothing; holds are proposals only.
class TripScreen extends StatefulWidget {
  const TripScreen({
    required this.http,
    required this.tripId,
    this.today,
    super.key,
  });

  final Dio http;
  final String tripId;
  final DateTime? today;

  @override
  State<TripScreen> createState() => _TripScreenState();
}

class _TripScreenState extends State<TripScreen> {
  Map<String, dynamic>? _view;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  String get _path => '/api/v1/trips/${widget.tripId}';
  Map<String, dynamic> get _trip => jsonObject(_view?['trip']) ?? const {};
  DateTime get _today => widget.today ?? DateTime.now();

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      if (!mounted) return;
      setState(() {
        _view = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this trip.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _send(Future<Response<dynamic>> Function() call) async {
    setState(() => _busy = true);
    try {
      final response = await call();
      if (mounted) setState(() => _view = jsonObject(response.data) ?? _view);
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _addOption() async {
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _OptionSheet(trip: _trip, today: _today),
    );
    if (draft == null) return;
    await _send(() => widget.http.post<dynamic>('$_path/options', data: draft));
  }

  Future<void> _addItem(String? day) async {
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) =>
          _ItemSheet(day: day ?? asJsonString(_trip['start']) ?? ''),
    );
    if (draft == null) return;
    await _send(() => widget.http.post<dynamic>('$_path/items', data: draft));
  }

  Future<void> _changeDates() async {
    final start =
        DateTime.tryParse(asJsonString(_trip['start']) ?? '') ?? _today;
    final end = DateTime.tryParse(asJsonString(_trip['end']) ?? '') ?? _today;
    final range = await showDateRangePicker(
      context: context,
      initialDateRange: DateTimeRange(start: start, end: end),
      firstDate: _today.subtract(const Duration(days: 1)),
      lastDate: _today.add(const Duration(days: 730)),
    );
    if (range == null || !mounted) return;
    final body = {'start': dateKey(range.start), 'end': dateKey(range.end)};
    try {
      final response = await widget.http.post<dynamic>(
        '$_path/dates/preview',
        data: body,
      );
      final preview = jsonObject(response.data);
      if (preview == null || !mounted) return;
      final confirmed = await showModalBottomSheet<bool>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (_) => _ReplanSheet(preview: preview),
      );
      if (confirmed == true) {
        await _send(() => widget.http.put<dynamic>('$_path/dates', data: body));
      }
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    }
  }

  String _countdown() {
    final start = DateTime.tryParse(asJsonString(_trip['start']) ?? '');
    if (start == null) return '';
    final days = DateTime(
      start.year,
      start.month,
      start.day,
    ).difference(DateTime(_today.year, _today.month, _today.day)).inDays;
    if (days > 1) return 'In $days days';
    if (days == 1) return 'Tomorrow';
    if (days == 0) return 'Today';
    return 'Under way or past';
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final options = jsonMaps(_view?['options']);
    final days = jsonMaps(_view?['days']);
    final checklist = jsonMaps(_view?['checklist']);
    final holds = jsonMaps(_view?['holds']);
    final unknowns = jsonStrings(_view?['unknowns']);
    final budget = jsonObject(_view?['budget']) ?? const {};
    final currency = asJsonString(budget['currency']) ?? 'EUR';
    final total = budget['budget'];
    return Scaffold(
      appBar: AppBar(title: PageTitle(asJsonString(_trip['title']) ?? 'Trip')),
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
                      FadeSlideIn(
                        child: KeyedSubtree(
                          key: const Key('trip-summary'),
                          child: HeroCard(
                            label:
                                '${_countdown()} · ${asJsonString(_trip['start']) ?? ''} – ${asJsonString(_trip['end']) ?? ''}',
                            title: asJsonString(_trip['destination']) ?? '',
                            subtitle:
                                'Chosen so far: $currency ${(budget['chosen'] as num? ?? 0).toStringAsFixed(2)}'
                                '${total is num ? ' of $currency ${total.toStringAsFixed(2)}' : ''}. Nothing is booked.',
                            footer: PillAction(
                              key: const Key('trip-dates'),
                              onDark: true,
                              icon: PhosphorIconsRegular.calendarBlank,
                              label: 'Change dates',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_changeDates()),
                            ),
                          ),
                        ),
                      ),
                      if (unknowns.isNotEmpty) ...[
                        GroupLabel('Still unknown', count: unknowns.length),
                        for (final (i, unknown) in unknowns.indexed)
                          FeatureRow(
                            key: Key('trip-unknown-$i'),
                            index: i,
                            icon: PhosphorIconsRegular.info,
                            badgeColor: colors.warning,
                            title: unknown,
                          ),
                      ],
                      const GroupLabel('Options'),
                      for (final (i, view) in options.indexed) _option(view, i),
                      Align(
                        alignment: Alignment.centerLeft,
                        child: PillAction(
                          key: const Key('trip-add-option'),
                          icon: PhosphorIconsRegular.plus,
                          label: 'Add an option',
                          onPressed: _busy
                              ? null
                              : () => unawaited(_addOption()),
                        ),
                      ),
                      const GroupLabel('Days'),
                      for (final day in days) _day(day),
                      if (checklist.isNotEmpty) ...[
                        const GroupLabel('Checklist'),
                        for (final (i, entry) in checklist.indexed)
                          FeatureRow(
                            key: Key('trip-check-${entry['id']}'),
                            index: i,
                            icon: entry['done'] == true
                                ? PhosphorIconsRegular.checkCircle
                                : PhosphorIconsRegular.circle,
                            quiet: entry['done'] == true,
                            caption: asJsonString(entry['group']),
                            title: asJsonString(entry['text']) ?? '',
                            subtitle: asJsonString(entry['note']),
                            onTap: _busy
                                ? null
                                : () => unawaited(
                                    _send(
                                      () => widget.http.put<dynamic>(
                                        '$_path/checklist/${entry['id']}',
                                        data: {'done': entry['done'] != true},
                                      ),
                                    ),
                                  ),
                          ),
                      ],
                      if (holds.isNotEmpty) ...[
                        const GroupLabel('Proposed holds'),
                        Padding(
                          padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
                          child: Text(
                            'Ideas for your calendar and reminders. Nothing has been added.',
                            key: const Key('trip-holds-note'),
                            style: TextStyle(color: colors.muted, fontSize: 13),
                          ),
                        ),
                        for (final (i, hold) in holds.indexed)
                          FeatureRow(
                            key: Key('trip-hold-$i'),
                            index: i,
                            icon: PhosphorIconsRegular.alarm,
                            caption: asJsonString(hold['why']),
                            title: asJsonString(hold['title']) ?? '',
                            subtitle:
                                '${asJsonString(hold['date']) ?? ''}${asJsonString(hold['at']) == null ? '' : ' · ${(asJsonString(hold['at']) ?? '').substring(0, 5)}'}',
                          ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _option(Map<String, dynamic> view, int index) {
    final colors = JarvisColors.of(context);
    final option = jsonObject(view['option']) ?? const {};
    final id = asJsonString(option['id']) ?? '';
    final state = asJsonString(view['quote']);
    final chosen = option['chosen'] == true;
    final price = asJsonString(view['priceText']);
    return FeatureRow(
      key: Key('trip-option-$id'),
      index: index,
      icon: switch (asJsonString(option['kind'])) {
        'transport' => PhosphorIconsRegular.airplaneTilt,
        'stay' => PhosphorIconsRegular.house,
        _ => PhosphorIconsRegular.sparkle,
      },
      caption: asJsonString(option['kind']),
      title: asJsonString(option['title']) ?? '',
      subtitle: [
        ?price,
        asJsonString(view['quoteNote']) ?? '',
        ?asJsonString(option['cancellation']),
      ].join('\n'),
      trailing: StatusPill(
        label: switch (state) {
          'current' => 'Current',
          'outdated' => 'Outdated',
          'other_dates' => 'Other dates',
          _ => 'Not quoted',
        },
        color: state == 'current' ? colors.accent : colors.warning,
      ),
      actions: [
        PillAction(
          key: Key('trip-choose-$id'),
          compact: true,
          primary: !chosen,
          icon: chosen ? PhosphorIconsRegular.checkCircle : null,
          label: chosen ? 'Chosen' : 'Choose',
          onPressed: _busy
              ? null
              : () => unawaited(
                  _send(
                    () => widget.http.put<dynamic>(
                      '$_path/options/$id/chosen',
                      data: {'chosen': !chosen},
                    ),
                  ),
                ),
        ),
        PillAction(
          key: Key('trip-remove-$id'),
          compact: true,
          label: 'Remove',
          onPressed: _busy
              ? null
              : () => unawaited(
                  _send(
                    () => widget.http.delete<dynamic>('$_path/options/$id'),
                  ),
                ),
        ),
      ],
    );
  }

  Widget _day(Map<String, dynamic> day) {
    final colors = JarvisColors.of(context);
    final date = asJsonString(day['date']) ?? '';
    final items = jsonMaps(day['items']);
    final flags = jsonMaps(day['flags']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(4, 10, 0, 6),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    date,
                    style: const TextStyle(
                      fontWeight: FontWeight.w700,
                      fontSize: 16,
                    ),
                  ),
                ),
                Text(
                  '${asJsonInt(day['freeMinutes'])} min free',
                  style: TextStyle(color: colors.muted, fontSize: 13),
                ),
              ],
            ),
          ),
          for (final flag in flags)
            Padding(
              padding: const EdgeInsets.fromLTRB(4, 0, 4, 6),
              child: Text(
                asJsonString(flag['text']) ?? '',
                style: TextStyle(color: colors.warning, fontSize: 13),
              ),
            ),
          for (final (i, view) in items.indexed) _item(view, i),
          Align(
            alignment: Alignment.centerLeft,
            child: PillAction(
              key: Key('trip-add-item-$date'),
              compact: true,
              icon: PhosphorIconsRegular.plus,
              label: 'Add to this day',
              onPressed: _busy ? null : () => unawaited(_addItem(date)),
            ),
          ),
        ],
      ),
    );
  }

  Widget _item(Map<String, dynamic> view, int index) {
    final colors = JarvisColors.of(context);
    final item = jsonObject(view['item']) ?? const {};
    final id = asJsonString(item['id']) ?? '';
    final flags = jsonMaps(view['flags']);
    String time(String key) =>
        (asJsonString(item[key]) ?? '').padLeft(5).substring(0, 5);
    return FeatureRow(
      key: Key('trip-item-$id'),
      index: index,
      icon: switch (asJsonString(item['kind'])) {
        'travel' => PhosphorIconsRegular.airplaneTilt,
        'stay' => PhosphorIconsRegular.house,
        'meal' => PhosphorIconsRegular.forkKnife,
        _ => PhosphorIconsRegular.mapPin,
      },
      caption:
          '${time('start')}–${time('end')}${asJsonString(item['place']) == null ? '' : ' · ${item['place']}'}',
      title: asJsonString(item['title']) ?? '',
      subtitle: flags.isEmpty
          ? null
          : flags.map((f) => asJsonString(f['text']) ?? '').join('\n'),
      badgeColor: flags.any((f) => f['code'] != 'unverified')
          ? colors.warning
          : null,
      trailing: IconButton(
        key: Key('trip-item-remove-$id'),
        tooltip: 'Remove',
        icon: const Icon(PhosphorIconsRegular.trash, size: 18),
        onPressed: _busy
            ? null
            : () => unawaited(
                _send(() => widget.http.delete<dynamic>('$_path/items/$id')),
              ),
      ),
    );
  }
}

class _OptionSheet extends StatefulWidget {
  const _OptionSheet({required this.trip, required this.today});

  final Map<String, dynamic> trip;
  final DateTime today;

  @override
  State<_OptionSheet> createState() => _OptionSheetState();
}

class _OptionSheetState extends State<_OptionSheet> {
  final _title = TextEditingController();
  final _price = TextEditingController();
  final _source = TextEditingController();
  String _kind = 'transport';

  @override
  void dispose() {
    _title.dispose();
    _price.dispose();
    _source.dispose();
    super.dispose();
  }

  Widget _label(String text) => Padding(
    padding: const EdgeInsets.fromLTRB(2, 14, 0, 6),
    child: Text(text, style: const TextStyle(fontWeight: FontWeight.w600)),
  );

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
            'Add an option',
            style: JarvisType.displayOf(context).copyWith(fontSize: 26),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            children: [
              for (final kind in const ['transport', 'stay', 'activity'])
                ChoiceChip(
                  key: Key('option-kind-$kind'),
                  label: Text(kind),
                  selected: _kind == kind,
                  onSelected: (_) => setState(() => _kind = kind),
                ),
            ],
          ),
          _label('What is it?'),
          TextField(
            key: const Key('option-title'),
            controller: _title,
            decoration: const InputDecoration(hintText: 'TAP flight 08:15'),
          ),
          _label('Price (only if a source says so)'),
          TextField(
            key: const Key('option-price'),
            controller: _price,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(hintText: '240'),
          ),
          _label('Where you saw it (web address)'),
          TextField(
            key: const Key('option-source'),
            controller: _source,
            decoration: const InputDecoration(hintText: 'https://…'),
          ),
          const Padding(
            padding: EdgeInsets.only(top: 8),
            child: Text(
              'A price without a source is saved as not quoted, never as a current fact.',
            ),
          ),
          const SizedBox(height: 14),
          PillAction(
            key: const Key('option-save'),
            primary: true,
            label: 'Add',
            onPressed: () {
              final price = double.tryParse(
                _price.text.trim().replaceAll(',', '.'),
              );
              final hasSource = _source.text.trim().isNotEmpty;
              Navigator.pop<Map<String, Object?>>(context, {
                'kind': _kind,
                'title': _title.text.trim(),
                'price': ?price,
                if (price != null) 'currency': widget.trip['currency'],
                if (price != null && hasSource)
                  'quotedOn': dateKey(widget.today),
                if (price != null && hasSource && _kind != 'activity')
                  'forStart': widget.trip['start'],
                if (price != null && hasSource && _kind != 'activity')
                  'forEnd': widget.trip['end'],
                if (hasSource) 'sourceUrl': _source.text.trim(),
              });
            },
          ),
        ],
      ),
    ),
  );
}

class _ItemSheet extends StatefulWidget {
  const _ItemSheet({required this.day});

  final String day;

  @override
  State<_ItemSheet> createState() => _ItemSheetState();
}

class _ItemSheetState extends State<_ItemSheet> {
  final _title = TextEditingController();
  final _place = TextEditingController();
  TimeOfDay _from = const TimeOfDay(hour: 10, minute: 0);
  TimeOfDay _to = const TimeOfDay(hour: 12, minute: 0);

  @override
  void dispose() {
    _title.dispose();
    _place.dispose();
    super.dispose();
  }

  String _hhmm(TimeOfDay t) =>
      '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}:00';

  Future<void> _pick(bool from) async {
    final picked = await showTimePicker(
      context: context,
      initialTime: from ? _from : _to,
    );
    if (picked != null) setState(() => from ? _from = picked : _to = picked);
  }

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
            widget.day,
            style: JarvisType.displayOf(context).copyWith(fontSize: 26),
          ),
          const SizedBox(height: 8),
          TextField(
            key: const Key('item-title'),
            controller: _title,
            decoration: const InputDecoration(labelText: 'What will you do?'),
          ),
          const SizedBox(height: 8),
          TextField(
            key: const Key('item-place'),
            controller: _place,
            decoration: const InputDecoration(labelText: 'Where (optional)'),
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              PillAction(
                key: const Key('item-from'),
                icon: PhosphorIconsRegular.clock,
                label: 'From ${_from.format(context)}',
                onPressed: () => unawaited(_pick(true)),
              ),
              PillAction(
                key: const Key('item-to'),
                icon: PhosphorIconsRegular.clock,
                label: 'To ${_to.format(context)}',
                onPressed: () => unawaited(_pick(false)),
              ),
            ],
          ),
          const SizedBox(height: 14),
          PillAction(
            key: const Key('item-save'),
            primary: true,
            label: 'Add to the day',
            onPressed: () => Navigator.pop<Map<String, Object?>>(context, {
              'date': widget.day,
              'start': _hhmm(_from),
              'end': _hhmm(_to),
              'title': _title.text.trim(),
              'kind': 'activity',
              if (_place.text.trim().isNotEmpty) 'place': _place.text.trim(),
            }),
          ),
        ],
      ),
    ),
  );
}

class _ReplanSheet extends StatelessWidget {
  const _ReplanSheet({required this.preview});

  final Map<String, dynamic> preview;

  @override
  Widget build(BuildContext context) {
    final notes = jsonStrings(preview['notes']);
    final recheck = jsonStrings(preview['optionsToRecheck']);
    return SafeArea(
      child: SingleChildScrollView(
        key: const Key('trip-replan'),
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              '${asJsonString(preview['newStart']) ?? ''} – ${asJsonString(preview['newEnd']) ?? ''}',
              style: JarvisType.displayOf(context).copyWith(fontSize: 26),
            ),
            const SizedBox(height: 8),
            for (final note in notes)
              FeatureRow(icon: PhosphorIconsRegular.info, title: note),
            if (asJsonInt(preview['itemsOutsideNewDates']) > 0)
              FeatureRow(
                icon: PhosphorIconsRegular.warningCircle,
                title:
                    '${asJsonInt(preview['itemsOutsideNewDates'])} planned item(s) fall outside the new dates.',
              ),
            for (final title in recheck)
              FeatureRow(
                icon: PhosphorIconsRegular.arrowsClockwise,
                caption: 'Check again',
                title: title,
              ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: [
                PillAction(
                  key: const Key('trip-replan-confirm'),
                  primary: true,
                  label: 'Change the dates',
                  onPressed: () => Navigator.pop(context, true),
                ),
                PillAction(
                  label: 'Not now',
                  onPressed: () => Navigator.pop(context, false),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
