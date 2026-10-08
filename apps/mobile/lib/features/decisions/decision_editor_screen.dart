import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../journal/journal_format.dart';
import 'decision_format.dart';

/// Logs a new decision or edits an unresolved one. Pops `true` after saving so the list reloads.
class DecisionEditorScreen extends StatefulWidget {
  const DecisionEditorScreen({required this.http, this.decision, super.key});

  final Dio http;
  final Map<String, dynamic>? decision;

  @override
  State<DecisionEditorScreen> createState() => _DecisionEditorScreenState();
}

class _DecisionEditorScreenState extends State<DecisionEditorScreen> {
  late final _title = TextEditingController(
    text: asJsonString(widget.decision?['title']) ?? '',
  );
  late final _prediction = TextEditingController(
    text: asJsonString(widget.decision?['prediction']) ?? '',
  );
  late final _context = TextEditingController(
    text: asJsonString(widget.decision?['context']) ?? '',
  );
  late int _percent = _initialPercent();
  late DateTime _reviewOn =
      parseReviewDate(widget.decision?['reviewOn']) ??
      DateTime.now().add(const Duration(days: 7));
  bool _saving = false;
  String? _error;

  bool get _editing => widget.decision != null;

  int _initialPercent() {
    final value = widget.decision?['probability'];
    if (value is num) return (value * 100).round().clamp(1, 99);
    return 70;
  }

  @override
  void dispose() {
    _title.dispose();
    _prediction.dispose();
    _context.dispose();
    super.dispose();
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final picked = await showDatePicker(
      context: context,
      initialDate: _reviewOn.isBefore(today) ? today : _reviewOn,
      firstDate: today,
      lastDate: today.add(const Duration(days: 700)),
    );
    if (picked != null && mounted) setState(() => _reviewOn = picked);
  }

  Future<void> _save() async {
    final title = _title.text.trim();
    final prediction = _prediction.text.trim();
    if (title.isEmpty || prediction.isEmpty) {
      setState(() => _error = 'Give the decision a name and a prediction.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    final body = {
      'title': title,
      'prediction': prediction,
      'probability': _percent / 100,
      'reviewOn': journalDateKey(_reviewOn),
      'context': _context.text.trim(),
    };
    try {
      final id = _editing ? jsonId(widget.decision) : null;
      if (_editing && id == null) {
        setState(() {
          _saving = false;
          _error = 'This decision cannot be edited.';
        });
        return;
      }
      if (_editing) {
        await widget.http.put<dynamic>('/api/v1/decisions/$id', data: body);
      } else {
        await widget.http.post<dynamic>('/api/v1/decisions', data: body);
      }
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save this decision.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not save this decision.';
      });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: PageTitle(_editing ? 'Edit decision' : 'Log a decision'),
      actions: [
        HeaderAction(
          key: const Key('decision-save'),
          label: 'Save',
          icon: PhosphorIconsRegular.check,
          busy: _saving,
          onPressed: _saving ? null : () => unawaited(_save()),
        ),
      ],
    ),
    body: ListView(
      padding: EdgeInsets.fromLTRB(
        16,
        8,
        16,
        32 + MediaQuery.paddingOf(context).bottom,
      ),
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
              TextField(
                key: const Key('decision-title'),
                controller: _title,
                maxLength: 200,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'What are you deciding?',
                  hintText: 'Take the new job',
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                key: const Key('decision-prediction'),
                controller: _prediction,
                minLines: 1,
                maxLines: 3,
                maxLength: 500,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'What do you expect to happen?',
                  hintText: 'I will still enjoy it a year from now',
                  helperText: 'A statement that will turn out true or false.',
                  alignLabelWithHint: true,
                ),
              ),
              const SizedBox(height: 20),
              Text(
                'How sure are you? $_percent%',
                key: const Key('decision-percent'),
                style: Theme.of(context).textTheme.titleSmall,
              ),
              Slider(
                key: const Key('decision-slider'),
                value: _percent.toDouble(),
                min: 1,
                max: 99,
                divisions: 98,
                label: '$_percent%',
                onChanged: (value) => setState(() => _percent = value.round()),
              ),
              const SizedBox(height: 8),
              Text(
                'When should Jarvis ask how it went?',
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: 8),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final (label, days) in reviewShortcuts)
                    ActionChip(
                      key: Key('decision-in-$days'),
                      label: Text(label),
                      onPressed: () => setState(
                        () => _reviewOn = DateTime.now().add(
                          Duration(days: days),
                        ),
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 8),
              OutlinedButton.icon(
                key: const Key('decision-date'),
                onPressed: _pickDate,
                icon: const Icon(PhosphorIconsRegular.calendarBlank, size: 18),
                label: Text(formatJournalDate(_reviewOn)),
              ),
              const SizedBox(height: 16),
              TextField(
                key: const Key('decision-context'),
                controller: _context,
                minLines: 2,
                maxLines: 8,
                maxLength: 2000,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'Why? (optional)',
                  alignLabelWithHint: true,
                ),
              ),
            ],
          ),
        ),
      ],
    ),
  );
}
