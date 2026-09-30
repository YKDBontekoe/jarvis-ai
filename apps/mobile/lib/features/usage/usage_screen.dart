import 'dart:async';
import 'dart:math' as math;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

String formatTokenCount(num value) {
  final tokens = value.round().abs();
  if (tokens < 1000) return '$tokens';
  if (tokens < 1000000) {
    final scaled = tokens / 1000;
    final text = scaled >= 10
        ? scaled.round().toString()
        : scaled.toStringAsFixed(1);
    return '${text}k';
  }
  final scaled = tokens / 1000000;
  final text = scaled >= 10
      ? scaled.round().toString()
      : scaled.toStringAsFixed(1);
  return '${text}M';
}

String formatUsd(num? value) {
  if (value == null) return '—';
  if (value == 0) return '\$0.00';
  if (value < 0.01) return '<\$0.01';
  if (value < 1) return '\$${value.toStringAsFixed(3)}';
  return '\$${value.toStringAsFixed(2)}';
}

/// Token, cost, and personalization totals for the signed-in owner.
class UsageScreen extends StatefulWidget {
  const UsageScreen({required this.http, super.key});

  final Dio http;

  @override
  State<UsageScreen> createState() => _UsageScreenState();
}

class _UsageScreenState extends State<UsageScreen> {
  static const _periods = [
    ('today', 'Today'),
    ('7d', '7 days'),
    ('30d', '30 days'),
    ('all', 'All time'),
  ];

  Map<String, dynamic>? _usage;
  String _period = '7d';
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/usage',
        queryParameters: {'period': _period},
      );
      if (!mounted) return;
      final data = jsonObject(response.data);
      if (data == null) {
        throw const FormatException('Missing usage payload.');
      }
      setState(() {
        _usage = data;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load usage.';
      });
    } on FormatException {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Jarvis returned an unreadable usage summary.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Could not load usage.';
      });
    }
  }

  Future<void> _selectPeriod(String period) async {
    if (period == _period) return;
    setState(() => _period = period);
    await _load();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Usage')),
    body: _loading && _usage == null
        ? const LoadingState()
        : _error != null && _usage == null
        ? ErrorState(message: _error!, onRetry: () => unawaited(_load()))
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _periodPicker(),
                      if (_error != null) ...[
                        const SizedBox(height: 12),
                        InlineNotice(
                          message: _error!,
                          tone: NoticeTone.danger,
                          actions: [
                            TextButton(
                              onPressed: () => unawaited(_load()),
                              child: const Text('Retry'),
                            ),
                          ],
                        ),
                      ],
                      const SizedBox(height: 16),
                      _personalization(),
                      const SizedBox(height: 16),
                      _headline(),
                      const SizedBox(height: 16),
                      _providerCard(
                        'Codex CLI',
                        'ChatGPT subscription',
                        PhosphorIconsRegular.cpu,
                        _map(_usage?['codex']),
                        showCost: false,
                      ),
                      const SizedBox(height: 12),
                      _providerCard(
                        'OpenRouter',
                        'Estimated spend',
                        PhosphorIconsRegular.plugsConnected,
                        _map(_usage?['openRouter']),
                        showCost: true,
                      ),
                      if (_map(_usage?['other'])['calls'] != null &&
                          asJsonInt(_map(_usage?['other'])['calls']) > 0) ...[
                        const SizedBox(height: 12),
                        _providerCard(
                          'Other models',
                          'Server embeddings',
                          PhosphorIconsRegular.brain,
                          _map(_usage?['other']),
                          showCost: false,
                        ),
                      ],
                      const SizedBox(height: 20),
                      const SectionHeader('Tokens by day'),
                      _chart(),
                      const SizedBox(height: 20),
                      const SectionHeader('Models'),
                      _models(),
                      const SizedBox(height: 20),
                      const SectionHeader('Activity'),
                      _activityGrid(),
                    ],
                  ),
                ),
              ],
            ),
          ),
  );

  Widget _periodPicker() => Wrap(
    spacing: 8,
    runSpacing: 8,
    children: [
      for (final (id, label) in _periods)
        ChoiceChip(
          key: Key('usage-period-$id'),
          label: Text(label),
          selected: _period == id,
          onSelected: (_) => unawaited(_selectPeriod(id)),
        ),
    ],
  );

  Widget _personalization() {
    final persona = _map(_usage?['personalization']);
    final score = asJsonInt(persona['score']);
    final band = asJsonString(persona['band']) ?? 'New';
    final summary =
        asJsonString(persona['summary']) ??
        'Jarvis is just getting to know you.';
    return SurfaceCard(
      key: const Key('usage-personalization'),
      gradient: LinearGradient(
        colors: [
          JarvisColors.of(context).accentSoft,
          JarvisColors.of(context).surface,
        ],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _ScoreRing(score: score, band: band),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(band, style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 4),
                Text(
                  summary,
                  style: TextStyle(
                    color: JarvisColors.of(context).inkSoft,
                    height: 1.35,
                  ),
                ),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: [
                    _chip(
                      _plural(
                        asJsonInt(persona['activeMemories']),
                        'memory',
                        'memories',
                      ),
                    ),
                    if (asJsonInt(persona['pinnedMemories']) > 0)
                      _chip('${asJsonInt(persona['pinnedMemories'])} pinned'),
                    _chip(
                      _plural(
                        asJsonInt(persona['memoryKinds']),
                        'kind',
                        'kinds',
                      ),
                    ),
                    if (asJsonInt(persona['personaTraits']) > 0)
                      _chip('${asJsonInt(persona['personaTraits'])} traits'),
                    if (asJsonBool(persona['hasCustomInstructions']))
                      _chip('Custom instructions'),
                    if (asJsonBool(persona['hasPreferredName']))
                      _chip('Preferred name'),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _chip(String label) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
    decoration: BoxDecoration(
      color: JarvisColors.of(context).surface,
      borderRadius: BorderRadius.circular(999),
      border: Border.all(color: JarvisColors.of(context).outline),
    ),
    child: Text(label, style: const TextStyle(fontSize: 12)),
  );

  Widget _headline() {
    final activity = _map(_usage?['activity']);
    final persona = _map(_usage?['personalization']);
    final codex = _map(_usage?['codex']);
    final openRouter = _map(_usage?['openRouter']);
    final tokens =
        asJsonInt(codex['totalTokens']) + asJsonInt(openRouter['totalTokens']);
    return LayoutBuilder(
      builder: (context, constraints) {
        // Two tiles per row on any phone width, instead of a fixed 168 that
        // no longer fits two columns at 360dp.
        _tileWidth = constraints.maxWidth.isFinite
            ? ((constraints.maxWidth - 10) / 2).floorToDouble().clamp(0, 240)
            : 168;
        return _headlineTiles(activity, persona, codex, openRouter, tokens);
      },
    );
  }

  double _tileWidth = 168;

  String _plural(int count, String one, String many) =>
      '$count ${count == 1 ? one : many}';

  Widget _headlineTiles(
    Map<String, dynamic> activity,
    Map<String, dynamic> persona,
    Map<String, dynamic> codex,
    Map<String, dynamic> openRouter,
    int tokens,
  ) {
    return Wrap(
      spacing: 10,
      runSpacing: 10,
      children: [
        _stat(
          'Tokens',
          formatTokenCount(tokens),
          _lifetimeTokens(codex, openRouter),
          key: const Key('usage-tokens'),
        ),
        _stat(
          'OpenRouter',
          _money(openRouter),
          _period == 'all'
              ? null
              : 'All time ${_money(openRouter, lifetime: true)}',
          key: const Key('usage-openrouter-cost'),
        ),
        _stat(
          'Messages sent',
          _amount(activity['messagesSent']),
          _caption(activity['messagesSent']),
          key: const Key('usage-messages'),
        ),
        _stat(
          'Memories',
          '${asJsonInt(persona['activeMemories'])}',
          _memoryCaption(activity['memories'], persona),
          key: const Key('usage-memories'),
        ),
        _stat(
          'Dreams',
          _amount(activity['dreams']),
          _dreamCaption(activity),
          key: const Key('usage-dreams'),
        ),
        _stat(
          'Skills',
          _amount(activity['skills']),
          _caption(activity['skills']),
        ),
        _stat('Files', _amount(activity['files']), _caption(activity['files'])),
        _stat(
          'Graph facts',
          _amount(activity['graphEntities']),
          _caption(activity['graphRelations'], noun: 'links'),
        ),
      ],
    );
  }

  Widget _stat(String label, String value, String? caption, {Key? key}) =>
      SizedBox(
        key: key,
        width: _tileWidth,
        child: SurfaceCard(
          padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                label,
                style: TextStyle(
                  color: JarvisColors.of(context).muted,
                  fontSize: 12,
                ),
              ),
              const SizedBox(height: 4),
              Text(value, style: Theme.of(context).textTheme.titleLarge),
              if (caption != null) ...[
                const SizedBox(height: 2),
                Text(
                  caption,
                  style: TextStyle(
                    color: JarvisColors.of(context).inkSoft,
                    fontSize: 12,
                  ),
                ),
              ],
            ],
          ),
        ),
      );

  Widget _providerCard(
    String title,
    String subtitle,
    IconData icon,
    Map<String, dynamic> provider, {
    required bool showCost,
  }) {
    final calls = asJsonInt(provider['calls']);
    final failed = asJsonInt(provider['failedCalls']);
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(icon: icon, size: 40),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: Theme.of(context).textTheme.titleMedium),
                    Text(
                      subtitle,
                      style: TextStyle(
                        color: JarvisColors.of(context).inkSoft,
                        fontSize: 13,
                      ),
                    ),
                  ],
                ),
              ),
              Text(
                showCost
                    ? _money(provider)
                    : formatTokenCount(asJsonInt(provider['totalTokens'])),
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ],
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 16,
            runSpacing: 8,
            children: [
              _metric('Calls', '$calls'),
              _metric(
                'Input',
                formatTokenCount(asJsonInt(provider['inputTokens'])),
              ),
              _metric(
                'Output',
                formatTokenCount(asJsonInt(provider['outputTokens'])),
              ),
              _metric(
                'Cached',
                formatTokenCount(asJsonInt(provider['cachedInputTokens'])),
              ),
              _metric(
                'Reasoning',
                formatTokenCount(asJsonInt(provider['reasoningOutputTokens'])),
              ),
              if (asJsonInt(provider['webSearchActions']) > 0)
                _metric(
                  'Searches',
                  '${asJsonInt(provider['webSearchActions'])}',
                ),
              _metric(
                'Latency',
                _latency(asJsonInt(provider['averageDurationMs'])),
              ),
              if (failed > 0) _metric('Failed', '$failed'),
            ],
          ),
          if (_purposeLine(provider) case final line?) ...[
            const SizedBox(height: 10),
            Text(
              line,
              style: TextStyle(
                color: JarvisColors.of(context).inkSoft,
                fontSize: 13,
              ),
            ),
          ],
          if (_period != 'all') ...[
            const SizedBox(height: 6),
            Text(
              'All time · ${asJsonInt(provider['lifetimeCalls'])} calls · ${formatTokenCount(asJsonInt(provider['lifetimeTotalTokens']))} tokens'
              '${showCost ? ' · ${_money(provider, lifetime: true)}' : ''}',
              style: TextStyle(
                color: JarvisColors.of(context).muted,
                fontSize: 12,
              ),
            ),
          ],
          if (asJsonString(provider['costNote']) case final note?) ...[
            const SizedBox(height: 8),
            Text(
              note,
              style: TextStyle(
                color: JarvisColors.of(context).muted,
                fontSize: 12,
                height: 1.35,
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _metric(String label, String value) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        label,
        style: TextStyle(color: JarvisColors.of(context).muted, fontSize: 11),
      ),
      Text(value, style: const TextStyle(fontWeight: FontWeight.w600)),
    ],
  );

  Widget _chart() {
    final days = jsonMaps(_usage?['daily']);
    final note = asJsonString(_usage?['chartNote']);
    final zone = asJsonString(_usage?['timeZoneId']) ?? 'UTC';
    final hasTokens = days.any(
      (day) =>
          asJsonInt(day['codexTokens']) > 0 ||
          asJsonInt(day['openRouterTokens']) > 0,
    );
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (!hasTokens)
            Text(
              'No model calls in this period yet.',
              style: TextStyle(color: JarvisColors.of(context).inkSoft),
            )
          else
            SizedBox(
              height: 132,
              child: CustomPaint(
                painter: _UsageChartPainter(days, JarvisColors.of(context)),
                child: const SizedBox.expand(),
              ),
            ),
          const SizedBox(height: 10),
          Wrap(
            spacing: 14,
            runSpacing: 6,
            children: [
              _Legend(color: JarvisColors.of(context).accent, label: 'Codex'),
              _Legend(
                color: JarvisColors.of(context).violet,
                label: 'OpenRouter',
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            note ?? 'Days follow $zone.',
            style: TextStyle(
              color: JarvisColors.of(context).muted,
              fontSize: 12,
            ),
          ),
        ],
      ),
    );
  }

  Widget _models() {
    final models = jsonMaps(_usage?['models']);
    if (models.isEmpty) {
      return SurfaceCard(
        child: Text(
          'Model calls will show up here after your next conversation.',
          style: TextStyle(color: JarvisColors.of(context).inkSoft),
        ),
      );
    }
    return SurfaceCard(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Column(
        children: [
          for (final (index, model) in models.indexed) ...[
            if (index > 0) const Divider(height: 1, indent: 16, endIndent: 16),
            ListTile(
              dense: true,
              title: Text(asJsonString(model['model']) ?? 'Default'),
              subtitle: Text(
                '${_label(asJsonString(model['purpose']))} · ${asJsonString(model['provider']) ?? ''} · ${asJsonInt(model['calls'])} calls',
              ),
              trailing: Text(
                model['provider'] == 'openrouter' &&
                        model['estimatedCostUsd'] != null
                    ? '${formatTokenCount(asJsonInt(model['totalTokens']))} · ${formatUsd(asJsonNum(model['estimatedCostUsd']))}'
                    : formatTokenCount(asJsonInt(model['totalTokens'])),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _activityGrid() {
    final activity = _map(_usage?['activity']);
    final items = [
      ('Assistant replies', activity['assistantReplies']),
      ('Conversations', activity['conversations']),
      ('Tasks completed', activity['tasksCompleted']),
      ('Reminders', activity['reminders']),
      ('Approvals', activity['approvals']),
      ('Web searches', activity['webSearches']),
      ('WhatsApp & Signal', activity['channelMessages']),
      ('Reply ratings', activity['feedbackRatings']),
      ('Browser sessions', activity['browserSessions']),
    ];
    return SurfaceCard(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Column(
        children: [
          for (final (index, item) in items.indexed) ...[
            if (index > 0) const Divider(height: 1, indent: 16, endIndent: 16),
            ListTile(
              dense: true,
              title: Text(item.$1),
              trailing: Text(
                _period == 'all'
                    ? _total(item.$2).toString()
                    : '${_inPeriod(item.$2)} · ${_total(item.$2)} all time',
                style: TextStyle(color: JarvisColors.of(context).inkSoft),
              ),
            ),
          ],
        ],
      ),
    );
  }

  String _amount(dynamic window) =>
      '${_period == 'all' ? _total(window) : _inPeriod(window)}';

  String? _caption(dynamic window, {String noun = 'all time'}) {
    if (_period == 'all') return null;
    return '${_total(window)} $noun';
  }

  String? _lifetimeTokens(
    Map<String, dynamic> codex,
    Map<String, dynamic> openRouter,
  ) {
    if (_period == 'all') return null;
    final total =
        asJsonInt(codex['lifetimeTotalTokens']) +
        asJsonInt(openRouter['lifetimeTotalTokens']);
    return '${formatTokenCount(total)} all time';
  }

  String? _memoryCaption(dynamic stored, Map<String, dynamic> persona) {
    final superseded = asJsonInt(persona['supersededMemories']);
    final created = _inPeriod(stored);
    if (_period == 'all') {
      return superseded == 0
          ? '${_total(stored)} stored'
          : '$superseded superseded';
    }
    final added = created == 1 ? '1 added' : '$created added';
    return superseded == 0 ? added : '$added · $superseded superseded';
  }

  String? _dreamCaption(Map<String, dynamic> activity) {
    final diary = asJsonInt(activity['diaryEntries']);
    final last = asJsonString(activity['lastDreamAt']);
    final diaryText = diary == 1 ? '1 diary entry' : '$diary diary entries';
    if (last == null) return diaryText;
    final parsed = DateTime.tryParse(last)?.toLocal();
    if (parsed == null) return diaryText;
    const months = [
      'Jan',
      'Feb',
      'Mar',
      'Apr',
      'May',
      'Jun',
      'Jul',
      'Aug',
      'Sep',
      'Oct',
      'Nov',
      'Dec',
    ];
    return '$diaryText · last ${months[parsed.month - 1]} ${parsed.day}';
  }

  String _money(Map<String, dynamic> provider, {bool lifetime = false}) {
    final calls = asJsonInt(
      lifetime ? provider['lifetimeCalls'] : provider['calls'],
    );
    final cost = asJsonNum(
      lifetime
          ? provider['lifetimeEstimatedCostUsd']
          : provider['estimatedCostUsd'],
    );
    if (cost == null) return calls == 0 ? '\$0.00' : 'Unpriced';
    return formatUsd(cost);
  }

  String? _purposeLine(Map<String, dynamic> provider) {
    final purposes = jsonMaps(provider['purposes']);
    if (purposes.isEmpty) return null;
    return purposes
        .map(
          (item) =>
              '${asJsonInt(item['calls'])} ${_label(asJsonString(item['purpose'])).toLowerCase()}',
        )
        .join(' · ');
  }

  String _latency(int milliseconds) {
    if (milliseconds <= 0) return '—';
    if (milliseconds < 1000) return '${milliseconds}ms';
    return '${(milliseconds / 1000).toStringAsFixed(1)}s';
  }

  String _label(String? purpose) => switch (purpose) {
    'background' => 'Background',
    'vision' => 'Vision',
    'embedding' => 'Embedding',
    _ => 'Chat',
  };

  int _inPeriod(dynamic value) => asJsonInt(_map(value)['inPeriod']);
  int _total(dynamic value) => asJsonInt(_map(value)['total']);

  Map<String, dynamic> _map(dynamic value) => jsonObject(value) ?? const {};
}

num? asJsonNum(dynamic value) => value is num ? value : null;

class _ScoreRing extends StatelessWidget {
  const _ScoreRing({required this.score, required this.band});

  final int score;
  final String band;

  @override
  Widget build(BuildContext context) => Semantics(
    label: 'Personalization $band, $score out of 100',
    child: SizedBox.square(
      dimension: 84,
      child: CustomPaint(
        painter: _RingPainter(
          score.clamp(0, 100) / 100,
          JarvisColors.of(context),
        ),
        child: Center(
          child: Text('$score', style: Theme.of(context).textTheme.titleLarge),
        ),
      ),
    ),
  );
}

class _RingPainter extends CustomPainter {
  const _RingPainter(this.progress, this.colors);

  final double progress;
  final JarvisColors colors;

  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.width / 2 - 5;
    final track = Paint()
      ..color = colors.outline
      ..style = PaintingStyle.stroke
      ..strokeWidth = 6;
    final arc = Paint()
      ..color = colors.accent
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeWidth = 6;
    canvas.drawCircle(center, radius, track);
    canvas.drawArc(
      Rect.fromCircle(center: center, radius: radius),
      -math.pi / 2,
      math.pi * 2 * progress,
      false,
      arc,
    );
  }

  @override
  bool shouldRepaint(_RingPainter oldDelegate) =>
      oldDelegate.progress != progress || oldDelegate.colors != colors;
}

class _Legend extends StatelessWidget {
  const _Legend({required this.color, required this.label});

  final Color color;
  final String label;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(
        width: 8,
        height: 8,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      ),
      const SizedBox(width: 6),
      Text(
        label,
        style: TextStyle(fontSize: 12, color: JarvisColors.of(context).inkSoft),
      ),
    ],
  );
}

class _UsageChartPainter extends CustomPainter {
  _UsageChartPainter(this.days, this.colors);

  final List<Map<String, dynamic>> days;
  final JarvisColors colors;

  @override
  void paint(Canvas canvas, Size size) {
    if (days.isEmpty) return;
    var maxTokens = 1;
    for (final day in days) {
      final total =
          asJsonInt(day['codexTokens']) + asJsonInt(day['openRouterTokens']);
      if (total > maxTokens) maxTokens = total;
    }
    final slot = size.width / days.length;
    final barWidth = math.min(10.0, slot * 0.55);
    final codexPaint = Paint()..color = colors.accent;
    final openRouterPaint = Paint()..color = colors.violet;
    for (final (index, day) in days.indexed) {
      final codex =
          asJsonInt(day['codexTokens']) / maxTokens * (size.height - 4);
      final openRouter =
          asJsonInt(day['openRouterTokens']) / maxTokens * (size.height - 4);
      final x = slot * index + (slot - barWidth) / 2;
      if (codex > 0) {
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            Rect.fromLTWH(x, size.height - codex, barWidth, codex),
            const Radius.circular(3),
          ),
          codexPaint,
        );
      }
      if (openRouter > 0) {
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            Rect.fromLTWH(
              x,
              size.height - codex - openRouter,
              barWidth,
              openRouter,
            ),
            const Radius.circular(3),
          ),
          openRouterPaint,
        );
      }
    }
  }

  @override
  bool shouldRepaint(_UsageChartPainter oldDelegate) =>
      oldDelegate.days != days || oldDelegate.colors != colors;
}
