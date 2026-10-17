import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

const _kindLabels = {
  'meeting_prep': 'meeting prep',
  'follow_up': 'follow-ups',
  'renewal': 'renewals',
  'decision_review': 'decision reviews',
  'free_slot': 'free-time ideas',
};

/// A few chances to act while there is still time, each with the facts behind
/// it and where to act. Reading it proposes only; feedback quiets what you do
/// not want, and what you found useful is kept as history.
class OpportunitiesScreen extends StatefulWidget {
  const OpportunitiesScreen({required this.http, required this.pageFor, super.key});

  final Dio http;

  /// The page for a destination such as `meeting:{id}`, or null when there is none; supplied by the shell.
  final Widget? Function(String destination) pageFor;

  @override
  State<OpportunitiesScreen> createState() => _OpportunitiesScreenState();
}

class _OpportunitiesScreenState extends State<OpportunitiesScreen> {
  List<Map<String, dynamic>> _items = const [];
  List<Map<String, dynamic>> _history = const [];
  List<String> _muted = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/opportunities');
      final data = jsonObject(response.data) ?? const {};
      if (!mounted) return;
      setState(() {
        _items = jsonMaps(data['items']);
        _history = jsonMaps(data['history']);
        _muted = jsonStrings(data['mutedKinds']);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = firstProblemMessage(error.response?.data) ?? 'Could not look for opportunities.';
      });
    }
  }

  Future<void> _feedback(Map<String, dynamic> item, String verdict) async {
    try {
      await widget.http.post<dynamic>(
        '/api/v1/opportunities/feedback',
        data: {'key': item['key'], 'verdict': verdict},
      );
      if (mounted) await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)?.showSnackBar(
        SnackBar(content: Text(firstProblemMessage(error.response?.data) ?? 'That did not work.')),
      );
    }
  }

  Future<void> _open(String destination) async {
    final page = widget.pageFor(destination);
    if (page == null) {
      ScaffoldMessenger.maybeOf(context)?.showSnackBar(
        const SnackBar(content: Text('That page is not available here.')),
      );
      return;
    }
    await Navigator.of(context).push<void>(MaterialPageRoute(builder: (_) => page));
    if (mounted) unawaited(_load());
  }

  Future<void> _unmute(String kind) async {
    try {
      await widget.http.post<dynamic>('/api/v1/opportunities/unmute', data: {'kind': kind});
    } on DioException {
      // Nothing to undo.
    }
    if (mounted) await _load();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Worth doing')),
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
                      if (_items.isEmpty)
                        const EmptyState(
                          icon: PhosphorIconsRegular.checkCircle,
                          title: 'Nothing needs you right now',
                          message: 'When something is worth doing while there is still time, it shows up here with the reason.',
                        ),
                      for (final item in _items) _card(item, colors),
                      if (_muted.isNotEmpty) ...[
                        const SectionHeader('Quieted'),
                        for (final kind in _muted)
                          SurfaceCard(
                            key: Key('opportunity-muted-$kind'),
                            margin: const EdgeInsets.only(bottom: 8),
                            child: Row(
                              children: [
                                Expanded(child: Text('Fewer ${_kindLabels[kind] ?? kind} for now')),
                                TextButton(
                                  key: Key('opportunity-unmute-$kind'),
                                  onPressed: () => unawaited(_unmute(kind)),
                                  child: const Text('Bring back'),
                                ),
                              ],
                            ),
                          ),
                      ],
                      if (_history.isNotEmpty) ...[
                        const SectionHeader('You found these useful'),
                        for (final entry in _history.take(5))
                          Padding(
                            key: const Key('opportunity-history'),
                            padding: const EdgeInsets.only(bottom: 6),
                            child: Text('· ${asJsonString(entry['title']) ?? ''}', style: TextStyle(color: colors.inkSoft)),
                          ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _card(Map<String, dynamic> item, JarvisColors colors) {
    final key = asJsonString(item['key']) ?? '';
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: SurfaceCard(
        key: Key('opportunity-$key'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(asJsonString(item['title']) ?? '', style: const TextStyle(fontWeight: FontWeight.w600)),
            const SizedBox(height: 4),
            Text(asJsonString(item['whyNow']) ?? '', key: const Key('opportunity-why'), style: TextStyle(color: colors.inkSoft)),
            for (final also in jsonStrings(item['also']))
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('Also: $also', style: TextStyle(color: colors.muted, fontSize: 13)),
              ),
            Wrap(
              spacing: 4,
              children: [
                FilledButton.tonal(
                  key: Key('opportunity-open-$key'),
                  onPressed: () => unawaited(_open(asJsonString(item['destination']) ?? '')),
                  child: const Text('Open'),
                ),
                TextButton(
                  key: Key('opportunity-useful-$key'),
                  onPressed: () => unawaited(_feedback(item, 'useful')),
                  child: const Text('Useful'),
                ),
                PopupMenuButton<String>(
                  key: Key('opportunity-more-$key'),
                  tooltip: 'Not now',
                  onSelected: (verdict) => unawaited(_feedback(item, verdict)),
                  itemBuilder: (_) => const [
                    PopupMenuItem(value: 'later', child: Text('Later')),
                    PopupMenuItem(value: 'handled', child: Text('Already handled')),
                    PopupMenuItem(value: 'wrong', child: Text('Wrong assumption')),
                    PopupMenuItem(value: 'fewer', child: Text('Fewer like this')),
                  ],
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
