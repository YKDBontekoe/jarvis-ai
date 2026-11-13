import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../game/game_format.dart';
import 'museum_screen.dart';

/// Discover: one collision a day between two unrelated things you kept, your
/// year as a museum, and duels with friends' Jarvises.
class DiscoverScreen extends StatefulWidget {
  const DiscoverScreen({required this.http, this.onAskInChat, super.key});

  final Dio http;
  final ValueChanged<String>? onAskInChat;

  @override
  State<DiscoverScreen> createState() => _DiscoverScreenState();
}

const duelMetricLabels = {
  'habit_checkins': 'Habit check-ins',
  'journal_days': 'Days journalled',
  'promises_kept': 'Promises kept',
};

class _DiscoverScreenState extends State<DiscoverScreen> {
  Map<String, dynamic>? _collision;
  List<Map<String, dynamic>> _duels = const [];
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        widget.http.get<dynamic>('/api/v1/discover/collision'),
        widget.http.get<dynamic>('/api/v1/duels'),
      ]);
      if (!mounted) return;
      setState(() {
        _collision = jsonObject(results[0].data);
        _duels = jsonMaps(results[1].data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load Discover.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Could not load Discover.';
      });
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _nextCollision() async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/discover/collision/next',
      );
      unawaited(HapticFeedback.selectionClick());
      if (mounted) setState(() => _collision = jsonObject(response.data));
    } catch (_) {
      _show('Could not find another one.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<List<Map<String, dynamic>>?> _agents() async {
    try {
      final agents = jsonMaps(
        (await widget.http.get<dynamic>('/api/v1/agents')).data,
      );
      if (agents.isEmpty) {
        _show(
          'Add your friend’s Jarvis under Settings → Agents first, with the '
          'token they gave you.',
        );
        return null;
      }
      return agents;
    } catch (_) {
      _show('Could not load your agents.');
      return null;
    }
  }

  Future<void> _challenge() async {
    final agents = await _agents();
    if (agents == null || !mounted) return;
    final body = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _DuelSheet(agents: agents, joining: false),
    );
    if (body == null) return;
    await _duelCall(
      () => widget.http.post<dynamic>('/api/v1/duels', data: body),
      (duel) =>
          'Duel on. Send your friend the code ${duel?['code'] ?? ''} '
          'to join.',
    );
  }

  Future<void> _join() async {
    final agents = await _agents();
    if (agents == null || !mounted) return;
    final body = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _DuelSheet(agents: agents, joining: true),
    );
    if (body == null) return;
    await _duelCall(
      () => widget.http.post<dynamic>('/api/v1/duels/join', data: body),
      (_) => 'Joined. May the best one win.',
    );
  }

  Future<void> _duelCall(
    Future<Response<dynamic>> Function() call,
    String Function(Map<String, dynamic>? duel) done,
  ) async {
    try {
      final response = await call();
      unawaited(HapticFeedback.lightImpact());
      _show(done(jsonObject(response.data)));
      await _load();
    } on DioException catch (error) {
      _show(
        firstProblemMessage(error.response?.data) ??
            'Could not start the duel.',
      );
    } catch (_) {
      _show('Could not start the duel.');
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const PageTitle('Discover')),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: false,
      onRetry: _load,
      empty: const SizedBox.shrink(),
      child: OrbRefresh(
        onRefresh: _load,
        child: ListView(
          key: const Key('discover-list'),
          padding: EdgeInsets.fromLTRB(
            16,
            8,
            16,
            32 + MediaQuery.paddingOf(context).bottom,
          ),
          children: [
            ContentWidth(child: _collisionHero()),
            const ContentWidth(child: GroupLabel('Your year')),
            ContentWidth(
              child: FeatureRow(
                key: const Key('discover-museum'),
                icon: PhosphorIconsRegular.image,
                title: 'Life museum',
                subtitle:
                    'Your year as rooms: best days, longest streaks, calls you '
                    'made, promises kept and where the money went.',
                onTap: () => unawaited(
                  Navigator.of(context).push<void>(
                    MaterialPageRoute(
                      builder: (_) => MuseumScreen(http: widget.http),
                    ),
                  ),
                ),
              ),
            ),
            ContentWidth(child: GroupLabel('Duels', count: _duels.length)),
            if (_duels.isEmpty)
              const ContentWidth(
                child: _Quiet(
                  'Challenge a friend’s Jarvis to a few days of habits, '
                  'journalling or kept promises. Only the two scores are '
                  'shared, never what is behind them.',
                ),
              ),
            for (final (i, duel) in _duels.indexed)
              ContentWidth(
                child: _DuelRow(duel: duel, index: i),
              ),
            ContentWidth(
              child: Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  PillAction(
                    key: const Key('discover-challenge'),
                    label: 'Challenge a friend',
                    icon: PhosphorIconsRegular.trophy,
                    primary: true,
                    onPressed: () => unawaited(_challenge()),
                  ),
                  PillAction(
                    key: const Key('discover-join'),
                    label: 'Join with a code',
                    onPressed: () => unawaited(_join()),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );

  Widget _collisionHero() {
    final collision = _collision;
    if (collision == null) {
      return const HeroCard(
        key: Key('discover-collision-empty'),
        label: 'Today’s collision',
        title: 'Nothing has collided yet',
        maxTitleScale: 1.6,
        subtitle:
            'Save pieces to the library and write in your journal. Jarvis '
            'links two things you kept months apart that share an unusual '
            'idea.',
      );
    }
    final left = jsonObject(collision['left']) ?? const <String, dynamic>{};
    final right = jsonObject(collision['right']) ?? const <String, dynamic>{};
    final shared = [
      for (final word in (collision['shared'] as List? ?? const []))
        if (word is String) word,
    ];
    return HeroCard(
      key: const Key('discover-collision'),
      label: 'Today’s collision',
      title: shared.isEmpty ? 'Two things you kept' : shared.join(' · '),
      maxTitleScale: 1.6,
      footer: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _CollisionSide(side: left),
          const SizedBox(height: 10),
          _CollisionSide(side: right),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              if (widget.onAskInChat != null)
                PillAction(
                  key: const Key('discover-collision-explore'),
                  label: 'Explore in chat',
                  icon: PhosphorIconsRegular.sparkle,
                  primary: true,
                  onDark: true,
                  onPressed: () {
                    final prompt = asJsonString(collision['prompt']);
                    if (prompt != null) widget.onAskInChat!(prompt);
                  },
                ),
              PillAction(
                key: const Key('discover-collision-next'),
                label: 'Another one',
                onDark: true,
                onPressed: _busy ? null : () => unawaited(_nextCollision()),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _CollisionSide extends StatelessWidget {
  const _CollisionSide({required this.side});

  final Map<String, dynamic> side;

  @override
  Widget build(BuildContext context) {
    final on = parseGameDate(side['on']);
    final kind = switch (asJsonString(side['kind'])) {
      'library' => 'Saved',
      'journal' => 'Journal',
      'memory' => 'Jarvis remembers',
      _ => 'Kept',
    };
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      decoration: BoxDecoration(
        color: heroForeground.withValues(alpha: .08),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            [kind, if (on != null) shortDay(on)].join(' · ').toUpperCase(),
            style: JarvisType.capsOf(context).copyWith(
              color: heroForeground.withValues(alpha: .6),
              fontSize: 10.5,
            ),
          ),
          const SizedBox(height: 3),
          Text(
            asJsonString(side['title']) ?? '',
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              fontFamily: 'Geist',
              fontSize: 14.5,
              fontWeight: FontWeight.w600,
              color: heroForeground,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            asJsonString(side['excerpt']) ?? '',
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontFamily: 'Geist',
              fontSize: 13,
              height: 1.35,
              color: heroForeground.withValues(alpha: .72),
            ),
          ),
        ],
      ),
    );
  }
}

class _DuelRow extends StatelessWidget {
  const _DuelRow({required this.duel, required this.index});

  final Map<String, dynamic> duel;
  final int index;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final mine = asJsonInt(duel['myScore']);
    final theirs = duel['theirScore'];
    final outcome = asJsonString(duel['outcome']);
    final status = asJsonString(duel['status']) ?? 'active';
    final ends = parseGameDate(duel['endsOn']);
    final peer = asJsonString(duel['peerName']) ?? 'Your friend';
    final error = asJsonString(duel['lastError']);
    final caption = switch (outcome) {
      'won' => 'You won',
      'lost' => '$peer won',
      'tied' => 'A tie',
      _ =>
        status == 'cancelled'
            ? 'Called off'
            : [
                'Code ${duel['code']}',
                if (ends != null) 'until ${shortDay(ends)}',
              ].join(' · '),
    };
    return FeatureRow(
      key: Key('discover-duel-${duel['id']}'),
      index: index,
      quiet: status != 'active',
      icon: outcome == 'won'
          ? PhosphorIconsRegular.trophy
          : PhosphorIconsRegular.users,
      badgeColor: outcome == 'lost' ? colors.muted : colors.accent,
      caption: caption,
      title: '${asJsonString(duel['metricLabel']) ?? 'Duel'} with $peer',
      subtitle: error != null && status == 'active'
          ? 'Their score: $error'
          : null,
      trailing: Text(
        '$mine – ${theirs is num ? theirs.round() : '?'}',
        style: TextStyle(
          fontFamily: 'Geist',
          fontSize: 18,
          fontWeight: FontWeight.w700,
          color: colors.ink,
          fontFeatures: const [FontFeature.tabularFigures()],
        ),
      ),
    );
  }
}

class _DuelSheet extends StatefulWidget {
  const _DuelSheet({required this.agents, required this.joining});

  final List<Map<String, dynamic>> agents;
  final bool joining;

  @override
  State<_DuelSheet> createState() => _DuelSheetState();
}

class _DuelSheetState extends State<_DuelSheet> {
  String? _agent;
  String _metric = 'habit_checkins';
  int _days = 7;
  final _code = TextEditingController();

  @override
  void initState() {
    super.initState();
    if (widget.agents.length == 1) {
      _agent = asJsonString(widget.agents[0]['id']);
    }
  }

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  bool get _ready =>
      _agent != null &&
      (!widget.joining ||
          _code.text.replaceAll(RegExp('[^A-Za-z0-9]'), '').length == 8);

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              widget.joining ? 'Join a duel' : 'Challenge a friend',
              style: TextStyle(
                fontFamily: 'Geist',
                fontSize: 20,
                fontWeight: FontWeight.w600,
                letterSpacing: -.5,
                color: colors.ink,
              ),
            ),
            const SizedBox(height: 12),
            Text('THEIR JARVIS', style: JarvisType.capsOf(context)),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final agent in widget.agents)
                  ChoiceChip(
                    label: Text(asJsonString(agent['name']) ?? ''),
                    selected: _agent == agent['id'],
                    onSelected: (_) =>
                        setState(() => _agent = asJsonString(agent['id'])),
                  ),
              ],
            ),
            const SizedBox(height: 16),
            if (widget.joining)
              TextField(
                key: const Key('duel-code'),
                controller: _code,
                maxLength: 9,
                buildCounter: quietCounter,
                textCapitalization: TextCapitalization.characters,
                onChanged: (_) => setState(() {}),
                decoration: const InputDecoration(
                  labelText: 'Their duel code',
                  hintText: 'ABCD2345',
                ),
              )
            else ...[
              Text('COMPETE ON', style: JarvisType.capsOf(context)),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final entry in duelMetricLabels.entries)
                    ChoiceChip(
                      label: Text(entry.value),
                      selected: _metric == entry.key,
                      onSelected: (_) => setState(() => _metric = entry.key),
                    ),
                ],
              ),
              const SizedBox(height: 16),
              Text('FOR', style: JarvisType.capsOf(context)),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  for (final days in const [3, 7, 14])
                    ChoiceChip(
                      label: Text('$days days'),
                      selected: _days == days,
                      onSelected: (_) => setState(() => _days = days),
                    ),
                ],
              ),
            ],
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: PillAction(
                key: const Key('duel-start'),
                label: widget.joining ? 'Join' : 'Start the duel',
                primary: true,
                onPressed: _ready
                    ? () => Navigator.of(context).pop(
                        widget.joining
                            ? {
                                'remoteAgentId': _agent,
                                'code': _code.text.trim(),
                              }
                            : {
                                'remoteAgentId': _agent,
                                'metric': _metric,
                                'days': _days,
                              },
                      )
                    : null,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Quiet extends StatelessWidget {
  const _Quiet(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(4, 0, 4, 12),
    child: Text(
      text,
      style: TextStyle(
        fontFamily: 'Geist',
        fontSize: 14,
        height: 1.4,
        color: JarvisColors.of(context).inkSoft,
      ),
    ),
  );
}
