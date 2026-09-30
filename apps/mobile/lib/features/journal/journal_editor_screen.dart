import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'journal_format.dart';

/// Writes a new journal entry or edits an existing one. Pops `true` after a
/// save or delete so the list reloads.
class JournalEditorScreen extends StatefulWidget {
  const JournalEditorScreen({required this.http, this.entry, super.key});

  final Dio http;
  final Map<String, dynamic>? entry;

  @override
  State<JournalEditorScreen> createState() => _JournalEditorScreenState();
}

class _JournalEditorScreenState extends State<JournalEditorScreen> {
  late final _content = TextEditingController(
    text: asJsonString(widget.entry?['content']) ?? '',
  );
  late final _highlights = TextEditingController(
    text: asJsonString(widget.entry?['highlights']) ?? '',
  );
  late final _gratitude = TextEditingController(
    text: asJsonString(widget.entry?['gratitude']) ?? '',
  );
  late final _tags = TextEditingController(
    text: jsonStrings(widget.entry?['tags']).join(', '),
  );
  late DateTime _date =
      parseJournalDate(widget.entry?['entryDate']) ?? DateTime.now();
  late int? _rating = _intOrNull(widget.entry?['rating']);
  late int? _mood = _intOrNull(widget.entry?['mood']);
  late int? _energy = _intOrNull(widget.entry?['energy']);
  late int? _stress = _intOrNull(widget.entry?['stress']);
  bool _saving = false;
  String? _error;

  bool get _editing => widget.entry != null;

  static int? _intOrNull(Object? value) => value is int ? value : null;

  @override
  void dispose() {
    _content.dispose();
    _highlights.dispose();
    _gratitude.dispose();
    _tags.dispose();
    super.dispose();
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: _date.isAfter(now) ? now : _date,
      firstDate: DateTime(2000),
      lastDate: now,
    );
    if (picked != null && mounted) setState(() => _date = picked);
  }

  Future<void> _save() async {
    final content = _content.text.trim();
    final highlights = _highlights.text.trim();
    final gratitude = _gratitude.text.trim();
    if (content.isEmpty &&
        highlights.isEmpty &&
        gratitude.isEmpty &&
        _rating == null &&
        _mood == null &&
        _energy == null &&
        _stress == null) {
      setState(() => _error = 'Write something or add a rating before saving.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    final body = {
      'entryDate': journalDateKey(_date),
      'content': content,
      'highlights': highlights,
      'gratitude': gratitude,
      'rating': _rating,
      'mood': _mood,
      'energy': _energy,
      'stress': _stress,
      'tags': parseJournalTags(_tags.text),
    };
    try {
      final id = _editing ? jsonId(widget.entry) : null;
      if (_editing && id == null) {
        setState(() {
          _saving = false;
          _error = 'This entry cannot be edited.';
        });
        return;
      }
      if (_editing) {
        await widget.http.put<dynamic>('/api/v1/journal/$id', data: body);
      } else {
        await widget.http.post<dynamic>('/api/v1/journal', data: body);
      }
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save this entry.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not save this entry.';
      });
    }
  }

  Future<void> _delete() async {
    final id = jsonId(widget.entry);
    if (id == null) return;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this entry?',
      message: 'The entry and the memory Jarvis keeps of it will be removed.',
      cancelLabel: 'Keep',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    setState(() => _saving = true);
    try {
      await widget.http.delete<dynamic>('/api/v1/journal/$id');
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not delete this entry.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not delete this entry.';
      });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(_editing ? 'Edit entry' : 'New entry'),
      actions: [
        if (_editing)
          IconButton(
            tooltip: 'Delete entry',
            onPressed: _saving ? null : () => unawaited(_delete()),
            icon: const Icon(PhosphorIconsRegular.trash),
          ),
        HeaderAction(
          key: const Key('journal-save'),
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
              OutlinedButton.icon(
                key: const Key('journal-date'),
                onPressed: _pickDate,
                icon: const Icon(PhosphorIconsRegular.calendarBlank, size: 18),
                label: Text(formatJournalDate(_date)),
              ),
              const SizedBox(height: 16),
              TextField(
                key: const Key('journal-content'),
                controller: _content,
                minLines: 6,
                maxLines: 14,
                maxLength: 6000,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'How was your day?',
                  alignLabelWithHint: true,
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                key: const Key('journal-highlights'),
                controller: _highlights,
                minLines: 1,
                maxLines: 3,
                maxLength: 1000,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'Highlights',
                  alignLabelWithHint: true,
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                key: const Key('journal-gratitude'),
                controller: _gratitude,
                minLines: 1,
                maxLines: 3,
                maxLength: 1000,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'Grateful for',
                  alignLabelWithHint: true,
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                key: const Key('journal-tags'),
                controller: _tags,
                decoration: const InputDecoration(
                  labelText: 'Tags',
                  hintText: 'work, family, health',
                ),
              ),
              const SizedBox(height: 20),
              _RatingSelector(
                idPrefix: 'day',
                label: 'Rate your day',
                low: 'Awful',
                high: 'Great',
                count: 10,
                value: _rating,
                onChanged: (value) => setState(() => _rating = value),
              ),
              _RatingSelector(
                idPrefix: 'mood',
                label: 'Mood',
                low: 'Low',
                high: 'Great',
                count: 5,
                value: _mood,
                faces: true,
                onChanged: (value) => setState(() => _mood = value),
              ),
              _RatingSelector(
                idPrefix: 'energy',
                label: 'Energy',
                low: 'Drained',
                high: 'Energetic',
                count: 5,
                value: _energy,
                onChanged: (value) => setState(() => _energy = value),
              ),
              _RatingSelector(
                idPrefix: 'stress',
                label: 'Stress',
                low: 'Calm',
                high: 'Very stressed',
                count: 5,
                value: _stress,
                onChanged: (value) => setState(() => _stress = value),
              ),
            ],
          ),
        ),
      ],
    ),
  );
}

/// A row of 1..[count] chips. Tapping the selected chip clears the rating.
class _RatingSelector extends StatelessWidget {
  const _RatingSelector({
    required this.idPrefix,
    required this.label,
    required this.low,
    required this.high,
    required this.count,
    required this.value,
    required this.onChanged,
    this.faces = false,
  });

  final String idPrefix;
  final String label;
  final String low;
  final String high;
  final int count;
  final int? value;
  final bool faces;
  final ValueChanged<int?> onChanged;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: Theme.of(context).textTheme.titleSmall),
          const SizedBox(height: 8),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              for (var rating = 1; rating <= count; rating++)
                ChoiceChip(
                  key: Key('rating-$idPrefix-$rating'),
                  label: Text(faces ? moodEmoji(rating) : '$rating'),
                  selected: value == rating,
                  showCheckmark: false,
                  onSelected: (_) => onChanged(value == rating ? null : rating),
                ),
            ],
          ),
          const SizedBox(height: 4),
          Row(
            children: [
              Text(low, style: TextStyle(fontSize: 12, color: colors.muted)),
              const Spacer(),
              Text(high, style: TextStyle(fontSize: 12, color: colors.muted)),
            ],
          ),
        ],
      ),
    );
  }
}
