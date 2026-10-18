import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../outcomes/outcome_feedback_row.dart';
import '../../ui/feature_kit.dart';
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
  const OpportunitiesScreen({
    required this.http,
    required this.pageFor,
    super.key,
  });

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
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not look for opportunities.';
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
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ?? 'That did not work.',
          ),
        ),
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
    await Navigator.of(context)
        .push<void>(MaterialPageRoute(builder: (_) => page));
    if (mounted) unawaited(_load());
  }

  Future<void> _unmute(String kind) async {
    try {
      await widget.http.post<dynamic>(
        '/api/v1/opportunities/unmute',
        data: {'kind': kind},
      );
    } on DioException {
      // Nothing to undo.
    }
    if (mounted) await _load();
  }

  static const _kindIcons = {
    'meeting_prep': PhosphorIconsRegular.calendarBlank,
    'follow_up': PhosphorIconsRegular.arrowBendUpLeft,
    'renewal': PhosphorIconsRegular.receipt,
    'decision_review': PhosphorIconsRegular.gavel,
    'free_slot': PhosphorIconsRegular.hourglassMedium,
  };

  @override
  Widget build(BuildContext context) {
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
                      if (_items.isNotEmpty) _lead(_items.first),
                      if (_items.length > 1)
                        GroupLabel(
                          'Also worth a look',
                          count: _items.length - 1,
                        ),
                      for (var i = 1; i < _items.length; i++)
                        _card(_items[i], i),
                      if (_muted.isNotEmpty) ...[
                        const GroupLabel('Quieted'),
                        for (final kind in _muted)
                          FeatureRow(
                            key: Key('opportunity-muted-$kind'),
                            icon: PhosphorIconsRegular.bellSlash,
                            quiet: true,
                            title: 'Fewer ${_kindLabels[kind] ?? kind} for now',
                            trailing: PillAction(
                              key: Key('opportunity-unmute-$kind'),
                              label: 'Bring back',
                              onPressed: () => unawaited(_unmute(kind)),
                            ),
                          ),
                      ],
                      if (_items.isNotEmpty) ...[
                        const SizedBox(height: 12),
                        OutcomeFeedbackRow(
                          http: widget.http,
                          artifactKind: 'opportunity',
                        ),
                      ],
                      if (_history.isNotEmpty) ...[
                        const GroupLabel('You found these useful'),
                        for (final (i, entry) in _history.take(5).indexed)
                          FeatureRow(
                            key: Key('opportunity-history-$i'),
                            icon: PhosphorIconsRegular.thumbsUp,
                            quiet: true,
                            title: asJsonString(entry['title']) ?? '',
                          ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  /// The one thing most worth doing, on the ink card.
  Widget _lead(Map<String, dynamic> item) {
    final key = asJsonString(item['key']) ?? '';
    final also = jsonStrings(item['also']);
    return FadeSlideIn(
      child: KeyedSubtree(
        key: Key('opportunity-$key'),
        child: HeroCard(
          label: 'Right now',
          title: asJsonString(item['title']) ?? '',
          subtitle: [
            asJsonString(item['whyNow']) ?? '',
            for (final line in also) 'Also: $line',
          ].join('\n'),
          footer: Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              PillAction(
                key: Key('opportunity-open-$key'),
                primary: true,
                onDark: true,
                label: 'Open',
                onPressed: () =>
                    unawaited(_open(asJsonString(item['destination']) ?? '')),
              ),
              PillAction(
                key: Key('opportunity-useful-$key'),
                onDark: true,
                icon: PhosphorIconsRegular.thumbsUp,
                label: 'Useful',
                onPressed: () => unawaited(_feedback(item, 'useful')),
              ),
              _notNow(item, key, onDark: true),
            ],
          ),
        ),
      ),
    );
  }

  Widget _notNow(Map<String, dynamic> item, String key, {bool onDark = false}) {
    final colors = JarvisColors.of(context);
    return PopupMenuButton<String>(
      key: Key('opportunity-more-$key'),
      tooltip: 'Not now',
      icon: Icon(
        PhosphorIconsRegular.dotsThree,
        color: onDark ? heroForeground : colors.ink,
      ),
      onSelected: (verdict) => unawaited(_feedback(item, verdict)),
      itemBuilder: (_) => const [
        PopupMenuItem(value: 'later', child: Text('Later')),
        PopupMenuItem(value: 'handled', child: Text('Already handled')),
        PopupMenuItem(value: 'wrong', child: Text('Wrong assumption')),
        PopupMenuItem(value: 'fewer', child: Text('Fewer like this')),
      ],
    );
  }

  Widget _card(Map<String, dynamic> item, int index) {
    final key = asJsonString(item['key']) ?? '';
    final also = jsonStrings(item['also']);
    return FeatureRow(
      key: Key('opportunity-$key'),
      index: index,
      icon:
          _kindIcons[asJsonString(item['kind'])] ?? PhosphorIconsRegular.target,
      title: asJsonString(item['title']) ?? '',
      subtitle: [
        asJsonString(item['whyNow']) ?? '',
        for (final line in also) 'Also: $line',
      ].join('\n'),
      actions: [
        PillAction(
          key: Key('opportunity-open-$key'),
          primary: true,
          label: 'Open',
          onPressed: () =>
              unawaited(_open(asJsonString(item['destination']) ?? '')),
        ),
        PillAction(
          key: Key('opportunity-useful-$key'),
          icon: PhosphorIconsRegular.thumbsUp,
          label: 'Useful',
          onPressed: () => unawaited(_feedback(item, 'useful')),
        ),
        _notNow(item, key),
      ],
    );
  }
}
