import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../review/weekly_review_screen.dart';
import 'journal_editor_screen.dart';
import 'journal_format.dart';

/// Daily journal: a summary of recent days and every entry, written by the
/// user or told to Jarvis. Entries also become memories Jarvis can recall.
class JournalScreen extends StatefulWidget {
  const JournalScreen({required this.http, this.onTalkAboutDay, super.key});

  final Dio http;

  /// Starts a journaling conversation in chat; null hides the talk actions.
  final ValueChanged<String>? onTalkAboutDay;

  @override
  State<JournalScreen> createState() => _JournalScreenState();
}

class _JournalScreenState extends State<JournalScreen> {
  List<Map<String, dynamic>> _entries = const [];
  Map<String, dynamic>? _summary;
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
        '/api/v1/journal',
        queryParameters: {'limit': 100},
      );
      Map<String, dynamic>? summary;
      try {
        final summaryResponse = await widget.http.get<dynamic>(
          '/api/v1/journal/summary',
          queryParameters: {'days': 30},
        );
        summary = jsonObject(summaryResponse.data);
      } on DioException {
        // The list still works without the summary card.
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _entries = jsonMaps(response.data);
        _summary = summary;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your journal.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load your journal.';
      });
    }
  }

  Future<void> _open([Map<String, dynamic>? entry]) async {
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => JournalEditorScreen(http: widget.http, entry: entry),
      ),
    );
    if (saved == true && mounted) unawaited(_load());
  }

  void _talk() {
    final talk = widget.onTalkAboutDay;
    if (talk == null) return;
    Navigator.of(context).pop();
    talk(journalTalkPrompt);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Journal'),
      actions: [
        if (widget.onTalkAboutDay != null)
          HeaderAction(
            label: 'Talk',
            icon: PhosphorIconsRegular.waveform,
            collapsesWhenNarrow: true,
            onPressed: _talk,
          ),
        HeaderAction(
          label: 'Write',
          icon: PhosphorIconsRegular.pencilSimple,
          onPressed: () => unawaited(_open()),
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _entries.isEmpty,
      onRetry: () => unawaited(_load()),
      empty: EmptyState(
        icon: PhosphorIconsRegular.notebook,
        title: 'Your journal is empty',
        message:
            'Write about your day or talk it through with Jarvis. '
            'Entries become part of what Jarvis remembers.',
        action: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (widget.onTalkAboutDay != null)
              FilledButton.icon(
                key: const Key('journal-talk'),
                onPressed: _talk,
                icon: const Icon(PhosphorIconsRegular.waveform, size: 18),
                label: const Text('Talk about my day'),
              ),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              key: const Key('journal-write'),
              onPressed: () => unawaited(_open()),
              icon: const Icon(PhosphorIconsRegular.pencilSimple, size: 18),
              label: const Text('Write an entry'),
            ),
          ],
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
            ContentWidth(child: _SummaryCard(summary: _summary)),
            ContentWidth(
              child: _WeekReviewLink(
                onTap: () => unawaited(
                  Navigator.of(context).push<void>(
                    MaterialPageRoute(
                      builder: (_) => WeeklyReviewScreen(http: widget.http),
                    ),
                  ),
                ),
              ),
            ),
            if (widget.onTalkAboutDay != null)
              ContentWidth(
                child: Padding(
                  padding: const EdgeInsets.only(bottom: 14),
                  child: OutlinedButton.icon(
                    key: const Key('journal-talk'),
                    onPressed: _talk,
                    icon: const Icon(PhosphorIconsRegular.waveform, size: 18),
                    label: const Text('Talk about my day'),
                  ),
                ),
              ),
            for (final entry in _entries)
              ContentWidth(
                child: _EntryCard(
                  entry: entry,
                  onTap: () => unawaited(_open(entry)),
                ),
              ),
          ],
        ),
      ),
    ),
  );
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.summary});

  final Map<String, dynamic>? summary;

  @override
  Widget build(BuildContext context) {
    final data = summary;
    final total = asJsonInt(data?['totalEntries']);
    if (data == null || total == 0) return const SizedBox.shrink();
    final streak = asJsonInt(data['currentStreak']);
    final mood = data['averageMood'];
    final days = asJsonInt(data['days'], 30);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 14),
      borderColor: JarvisColors.of(context).outline,
      child: Row(
        children: [
          _Stat(value: '$streak', label: 'day streak'),
          _Stat(
            value: mood is num ? mood.toStringAsFixed(1) : '–',
            label: 'avg mood /5',
          ),
          _Stat(value: '$total', label: 'entries · $days days'),
        ],
      ),
    );
  }
}

class _WeekReviewLink extends StatelessWidget {
  const _WeekReviewLink({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SurfaceCard(
      key: const Key('journal-weekly-review'),
      margin: const EdgeInsets.only(bottom: 14),
      padding: const EdgeInsets.fromLTRB(16, 14, 14, 14),
      onTap: onTap,
      child: Row(
        children: [
          const IconBadge(icon: PhosphorIconsRegular.chartLine),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Your week in review',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 2),
                Text(
                  'Mood trends and a short story about your week',
                  style: TextStyle(fontSize: 13, color: colors.inkSoft),
                ),
              ],
            ),
          ),
          Icon(PhosphorIconsRegular.caretRight, size: 16, color: colors.muted),
        ],
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.value, required this.label});

  final String value;
  final String label;

  @override
  Widget build(BuildContext context) => Expanded(
    child: Column(
      children: [
        Text(value, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 2),
        Text(
          label,
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 12, color: JarvisColors.of(context).muted),
        ),
      ],
    ),
  );
}

class _EntryCard extends StatelessWidget {
  const _EntryCard({required this.entry, required this.onTap});

  final Map<String, dynamic> entry;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final date = parseJournalDate(entry['entryDate']);
    final source = asJsonString(entry['source']) ?? 'written';
    final told = source == 'voice' || source == 'chat';
    final content = asJsonString(entry['content']) ?? '';
    final highlights = asJsonString(entry['highlights']);
    final preview = content.isNotEmpty ? content : (highlights ?? '');
    final rating = entry['rating'];
    final mood = entry['mood'];
    final energy = entry['energy'];
    final stress = entry['stress'];
    final tags = jsonStrings(entry['tags']);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      borderColor: colors.outline,
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  date == null ? 'Undated' : formatJournalDate(date),
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              Icon(
                told
                    ? PhosphorIconsRegular.waveform
                    : PhosphorIconsRegular.pencilSimple,
                size: 14,
                color: colors.muted,
              ),
              const SizedBox(width: 4),
              Text(
                told ? 'Told to Jarvis' : 'Written',
                style: TextStyle(fontSize: 12, color: colors.muted),
              ),
            ],
          ),
          if (rating is int || mood is int || energy is int || stress is int)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  if (rating is int)
                    StatusPill(
                      label: ratingLabel('Day', rating, 10),
                      color: colors.accent,
                    ),
                  if (mood is int)
                    StatusPill(
                      label:
                          '${moodEmoji(mood)} ${ratingLabel('Mood', mood, 5)}'
                              .trim(),
                      color: colors.success,
                    ),
                  if (energy is int)
                    StatusPill(
                      label: ratingLabel('Energy', energy, 5),
                      color: colors.warning,
                    ),
                  if (stress is int)
                    StatusPill(
                      label: ratingLabel('Stress', stress, 5),
                      color: colors.danger,
                    ),
                ],
              ),
            ),
          if (preview.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                preview,
                maxLines: 4,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 15, height: 1.45),
              ),
            ),
          if (tags.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                tags.map((tag) => '#$tag').join('  '),
                style: TextStyle(fontSize: 13, color: colors.muted),
              ),
            ),
        ],
      ),
    );
  }
}
