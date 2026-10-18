import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'trip_screen.dart';

String dateKey(DateTime day) =>
    '${day.year.toString().padLeft(4, '0')}-${day.month.toString().padLeft(2, '0')}-${day.day.toString().padLeft(2, '0')}';

/// Trips you are planning. Planning books nothing and writes to no calendar.
class TripsScreen extends StatefulWidget {
  const TripsScreen({required this.http, this.today, super.key});

  final Dio http;
  final DateTime? today;

  @override
  State<TripsScreen> createState() => _TripsScreenState();
}

class _TripsScreenState extends State<TripsScreen> {
  List<Map<String, dynamic>> _trips = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/trips');
      if (!mounted) return;
      setState(() {
        _trips = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your trips.';
      });
    }
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) =>
            TripScreen(http: widget.http, tripId: id, today: widget.today),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create() async {
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _NewTripSheet(today: widget.today ?? DateTime.now()),
    );
    if (draft == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/trips',
        data: draft,
      );
      final id = jsonString(
        jsonObject(jsonObject(response.data)?['trip']) ?? const {},
        'id',
      );
      if (id != null && mounted) await _open(id);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'Could not start the trip.',
            ),
          ),
        );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Trips'),
      actions: [
        HeaderAction(
          label: 'New',
          icon: PhosphorIconsRegular.plus,
          onPressed: _create,
        ),
      ],
    ),
    body: _loading
        ? const SkeletonList()
        : _error != null
        ? ErrorState(message: _error!, onRetry: _load)
        : ListView(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
            children: [
              ContentWidth(
                child: _trips.isEmpty
                    ? Padding(
                        padding: const EdgeInsets.only(top: 24),
                        child: EmptyState(
                          icon: PhosphorIconsRegular.airplaneTilt,
                          title: 'Plan a trip you can trust',
                          message: 'Dates, options with dated and sourced prices, a day-by-day plan and what is still unknown. Nothing is booked.',
                          action: PillAction(
                            key: const Key('trip-start'),
                            primary: true,
                            icon: PhosphorIconsRegular.plus,
                            label: 'Plan a trip',
                            onPressed: _create,
                          ),
                        ),
                      )
                    : Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          for (final (i, trip) in _trips.indexed)
                            FeatureRow(
                              key: Key('trip-${trip['id']}'),
                              index: i,
                              icon: PhosphorIconsRegular.airplaneTilt,
                              caption:
                                  '${asJsonString(trip['start']) ?? ''} – ${asJsonString(trip['end']) ?? ''}',
                              title: asJsonString(trip['title']) ?? 'Trip',
                              subtitle: asJsonString(trip['destination']),
                              trailing: asJsonInt(trip['unknowns']) > 0
                                  ? StatusPill(
                                      label:
                                          '${asJsonInt(trip['unknowns'])} unknown',
                                      color: JarvisColors.of(context).warning,
                                    )
                                  : null,
                              onTap: () =>
                                  unawaited(_open(trip['id'] as String)),
                            ),
                        ],
                      ),
              ),
            ],
          ),
  );
}

class _NewTripSheet extends StatefulWidget {
  const _NewTripSheet({required this.today});

  final DateTime today;

  @override
  State<_NewTripSheet> createState() => _NewTripSheetState();
}

class _NewTripSheetState extends State<_NewTripSheet> {
  final _title = TextEditingController();
  final _destination = TextEditingController();
  final _budget = TextEditingController();
  late DateTime _start = widget.today.add(const Duration(days: 30));
  late DateTime _end = widget.today.add(const Duration(days: 33));
  int _travelers = 1;
  bool _abroad = false;

  @override
  void dispose() {
    _title.dispose();
    _destination.dispose();
    _budget.dispose();
    super.dispose();
  }

  Future<void> _pick(bool start) async {
    final picked = await showDatePicker(
      context: context,
      initialDate: start ? _start : _end,
      firstDate: widget.today.subtract(const Duration(days: 1)),
      lastDate: widget.today.add(const Duration(days: 730)),
    );
    if (picked == null) return;
    setState(() {
      if (start) {
        _start = picked;
        if (_end.isBefore(_start)) _end = _start;
      } else {
        _end = picked.isBefore(_start) ? _start : picked;
      }
    });
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
            'New trip',
            style: JarvisType.displayOf(context).copyWith(fontSize: 26),
          ),
          _label('Name'),
          TextField(
            key: const Key('trip-title'),
            controller: _title,
            decoration: const InputDecoration(hintText: 'Lisbon in November'),
          ),
          _label('Where to'),
          TextField(
            key: const Key('trip-destination'),
            controller: _destination,
            decoration: const InputDecoration(hintText: 'Lisbon'),
          ),
          _label('When'),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              PillAction(
                key: const Key('trip-start-date'),
                icon: PhosphorIconsRegular.calendarBlank,
                label: 'From ${dateKey(_start)}',
                onPressed: () => unawaited(_pick(true)),
              ),
              PillAction(
                key: const Key('trip-end-date'),
                icon: PhosphorIconsRegular.calendarBlank,
                label: 'To ${dateKey(_end)}',
                onPressed: () => unawaited(_pick(false)),
              ),
            ],
          ),
          _label('Travellers'),
          Row(
            children: [
              IconButton(
                key: const Key('trip-fewer'),
                onPressed: _travelers > 1
                    ? () => setState(() => _travelers--)
                    : null,
                icon: const Icon(Icons.remove),
              ),
              Text(
                '$_travelers',
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              IconButton(
                key: const Key('trip-more'),
                onPressed: _travelers < 20
                    ? () => setState(() => _travelers++)
                    : null,
                icon: const Icon(Icons.add),
              ),
            ],
          ),
          _label('Budget in EUR (optional)'),
          TextField(
            key: const Key('trip-budget'),
            controller: _budget,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(hintText: '1500'),
          ),
          SwitchListTile(
            key: const Key('trip-abroad'),
            contentPadding: EdgeInsets.zero,
            title: const Text('Crossing a border'),
            value: _abroad,
            onChanged: (value) => setState(() => _abroad = value),
          ),
          const SizedBox(height: 8),
          PillAction(
            key: const Key('trip-create'),
            primary: true,
            icon: PhosphorIconsRegular.sparkle,
            label: 'Start planning',
            onPressed: () => Navigator.pop<Map<String, Object?>>(context, {
              'title': _title.text.trim().isEmpty
                  ? _destination.text.trim()
                  : _title.text.trim(),
              'destination': _destination.text.trim(),
              'start': dateKey(_start),
              'end': dateKey(_end),
              'travelers': _travelers,
              'abroad': _abroad,
              'budget': ?double.tryParse(
                _budget.text.trim().replaceAll(',', '.'),
              ),
              'currency': 'EUR',
            }),
          ),
        ],
      ),
    ),
  );
}
