import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'game_format.dart';

/// Play: Jarvis's bets against you, this week's side quests, the race against
/// the you of four weeks ago, and the score that ties them together.
class GameScreen extends StatefulWidget {
  const GameScreen({required this.http, super.key});

  final Dio http;

  @override
  State<GameScreen> createState() => _GameScreenState();
}

class _GameScreenState extends State<GameScreen> {
  Map<String, dynamic>? _board;
  bool _loading = true;
  String? _error;
  final Set<String> _busy = {};
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_revision;
    setState(() => _loading = _board == null);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/game');
      if (!mounted || revision != _revision) return;
      setState(() {
        _board = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load the game.';
      });
    } catch (_) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load the game.';
      });
    }
  }

  Future<void> _act(String id, String path, String done, String failed) async {
    if (_busy.contains(id)) return;
    setState(() => _busy.add(id));
    try {
      await widget.http.post<dynamic>(path);
      unawaited(HapticFeedback.lightImpact());
      _show(done);
      await _load();
    } on DioException catch (error) {
      _show(firstProblemMessage(error.response?.data) ?? failed);
    } catch (_) {
      _show(failed);
    } finally {
      if (mounted) setState(() => _busy.remove(id));
    }
  }

  Future<void> _callOff(Map<String, dynamic> bet) async {
    final id = jsonId(bet);
    if (id == null) return;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Call off this bet?',
      message:
          'It won’t count for you or for Jarvis, and Jarvis won’t bet '
          'on this again for a week.',
      cancelLabel: 'Keep the bet',
      confirmLabel: 'Call it off',
      icon: PhosphorIconsRegular.target,
    );
    if (!confirmed || !mounted) return;
    await _act(
      id,
      '/api/v1/game/bets/$id/call-off',
      'Bet called off.',
      'Could not call off the bet.',
    );
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final board = _board;
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Play')),
      body: ListScreenBody(
        loading: _loading,
        error: board == null ? _error : null,
        isEmpty: false,
        onRetry: _load,
        empty: const SizedBox.shrink(),
        child: board == null
            ? const SizedBox.shrink()
            : OrbRefresh(onRefresh: _load, child: _content(board)),
      ),
    );
  }

  Widget _content(Map<String, dynamic> board) {
    final today = parseGameDate(board['today']) ?? DateTime.now();
    final bets = jsonMaps(board['bets']);
    final open = [
      for (final b in bets)
        if (asJsonString(b['status']) == 'open') b,
    ];
    final settled = [
      for (final b in bets)
        if (asJsonString(b['status']) != 'open') b,
    ];
    final quests = jsonMaps(board['quests']);
    final ghost = jsonObject(board['ghost']);
    var index = 0;
    return ListView(
      key: const Key('game-list'),
      padding: EdgeInsets.fromLTRB(
        16,
        8,
        16,
        32 + MediaQuery.paddingOf(context).bottom,
      ),
      children: [
        ContentWidth(
          child: _ScoreHero(score: jsonObject(board['score']) ?? const {}),
        ),
        ContentWidth(
          child: GroupLabel('Jarvis bets against you', count: open.length),
        ),
        if (open.isEmpty)
          ContentWidth(
            child: _Quiet(
              settled.isEmpty
                  ? 'No bets yet. Jarvis bets once it has a few weeks of '
                        'your habits or a budget to go on.'
                  : 'No open bets. Jarvis places new ones as it spots '
                        'patterns.',
            ),
          ),
        for (final bet in open)
          ContentWidth(
            child: _BetRow(
              bet: bet,
              today: today,
              index: index++,
              busy: _busy.contains(jsonId(bet)),
              onCallOff: () => unawaited(_callOff(bet)),
            ),
          ),
        for (final bet in settled.take(3))
          ContentWidth(
            child: _BetRow(bet: bet, today: today, index: index++),
          ),
        ContentWidth(
          child: GroupLabel(
            'This week’s quests',
            count: quests.where((q) => q['status'] == 'open').length,
          ),
        ),
        for (final quest in quests)
          ContentWidth(
            child: _QuestRow(
              quest: quest,
              index: index++,
              busy: _busy.contains(jsonId(quest)),
              onDone: () {
                final id = jsonId(quest);
                if (id == null) return;
                unawaited(
                  _act(
                    id,
                    '/api/v1/game/quests/$id/complete',
                    'Quest done. +${asJsonInt(quest['points'], 0)} points.',
                    'Could not finish the quest.',
                  ),
                );
              },
              onSkip: () {
                final id = jsonId(quest);
                if (id == null) return;
                unawaited(
                  _act(
                    id,
                    '/api/v1/game/quests/$id/skip',
                    'Skipped. It won’t come back this week.',
                    'Could not skip the quest.',
                  ),
                );
              },
            ),
          ),
        if (ghost != null) ...[
          const ContentWidth(child: GroupLabel('Race your ghost')),
          ContentWidth(child: _GhostCard(ghost: ghost)),
        ],
      ],
    );
  }
}

class _ScoreHero extends StatelessWidget {
  const _ScoreHero({required this.score});

  final Map<String, dynamic> score;

  @override
  Widget build(BuildContext context) {
    final points = asJsonInt(score['points'], 0);
    final level = asJsonInt(score['level'], 1);
    final start = asJsonInt(score['levelStart'], 0);
    final next = asJsonInt(score['nextLevelAt'], 100);
    final won = asJsonInt(score['betsWon'], 0);
    final lost = asJsonInt(score['betsLost'], 0);
    final quests = asJsonInt(score['questsDone'], 0);
    final title = asJsonString(score['title']) ?? 'Rookie';
    return HeroCard(
      key: const Key('game-score'),
      label: 'Level $level · $title',
      title: points == 1 ? '1 point' : '$points points',
      maxTitleScale: 1.6,
      subtitle:
          '${next - points} to level ${level + 1}. '
          'Bets: you $won, Jarvis $lost. '
          '${quests == 1 ? '1 quest' : '$quests quests'} done.',
      segments: 10,
      doneSegments: levelSegments(points, start, next),
      footer: const HeroNote(
        'Prove Jarvis’s bets wrong and finish quests to level up. '
        'Beating a confident bet pays more.',
      ),
    );
  }
}

class _BetRow extends StatelessWidget {
  const _BetRow({
    required this.bet,
    required this.today,
    required this.index,
    this.busy = false,
    this.onCallOff,
  });

  final Map<String, dynamic> bet;
  final DateTime today;
  final int index;
  final bool busy;
  final VoidCallback? onCallOff;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(bet['status']) ?? 'open';
    final points = asJsonInt(bet['points'], 0);
    final confidence = asJsonInt(bet['confidencePercent'], 0);
    final ends = parseGameDate(bet['endsOn']);
    final open = status == 'open';
    final caption = switch (status) {
      'won' => 'You proved Jarvis wrong · +$points',
      'lost' => 'Jarvis called it',
      _ => [
        'Jarvis $confidence% sure',
        if (ends != null) betDeadline(ends, today),
      ].join(' · '),
    };
    return FeatureRow(
      key: Key('game-bet-${jsonId(bet)}'),
      index: index,
      icon: status == 'won'
          ? PhosphorIconsRegular.trophy
          : PhosphorIconsRegular.target,
      badgeColor: status == 'lost' ? colors.muted : colors.accent,
      quiet: !open,
      caption: caption,
      title: asJsonString(bet['claim']) ?? 'Bet',
      subtitle: asJsonString(bet['evidence']),
      trailing: open ? _PointsPill(points: points, prefix: 'Beat it ') : null,
      actions: open && onCallOff != null
          ? [
              PillAction(
                key: Key('game-bet-call-off-${jsonId(bet)}'),
                label: 'Call it off',
                compact: true,
                onPressed: busy ? null : onCallOff,
              ),
            ]
          : null,
    );
  }
}

class _QuestRow extends StatelessWidget {
  const _QuestRow({
    required this.quest,
    required this.index,
    required this.busy,
    required this.onDone,
    required this.onSkip,
  });

  final Map<String, dynamic> quest;
  final int index;
  final bool busy;
  final VoidCallback onDone;
  final VoidCallback onSkip;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(quest['status']) ?? 'open';
    final boss = quest['isBoss'] == true;
    final points = asJsonInt(quest['points'], 0);
    final target = asJsonInt(quest['target'], 1);
    final progress = asJsonInt(quest['progress'], 0);
    final open = status == 'open';
    final kind = asJsonString(quest['kind']);
    final caption = switch (status) {
      'done' => 'Done · +$points',
      'skipped' => 'Skipped',
      _ => boss ? 'Boss · longest avoided' : null,
    };
    return FeatureRow(
      key: Key('game-quest-${jsonId(quest)}'),
      index: index,
      icon: boss
          ? PhosphorIconsRegular.fire
          : switch (kind) {
              'person' => PhosphorIconsRegular.handWaving,
              'habit' => PhosphorIconsRegular.repeat,
              'journal' => PhosphorIconsRegular.notePencil,
              _ => PhosphorIconsRegular.sealCheck,
            },
      badgeColor: boss && open ? colors.warning : colors.accent,
      quiet: !open,
      strike: status == 'done',
      caption: caption,
      title: asJsonString(quest['title']) ?? 'Quest',
      subtitle: asJsonString(quest['detail']),
      trailing: open ? _PointsPill(points: points, prefix: '+') : null,
      extra: open && target > 1
          ? _Progress(progress: progress, target: target)
          : null,
      actions: open
          ? [
              PillAction(
                key: Key('game-quest-done-${jsonId(quest)}'),
                label: 'Done',
                icon: PhosphorIconsRegular.check,
                primary: true,
                compact: true,
                onPressed: busy ? null : onDone,
              ),
              PillAction(
                key: Key('game-quest-skip-${jsonId(quest)}'),
                label: 'Not this one',
                compact: true,
                onPressed: busy ? null : onSkip,
              ),
            ]
          : null,
    );
  }
}

class _PointsPill extends StatelessWidget {
  const _PointsPill({required this.points, required this.prefix});

  final int points;
  final String prefix;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
      decoration: BoxDecoration(
        color: colors.accentSoft,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        '$prefix$points',
        style: TextStyle(
          fontFamily: 'Geist',
          fontSize: 12.5,
          fontWeight: FontWeight.w600,
          color: colors.accentDeep,
          fontFeatures: const [FontFeature.tabularFigures()],
        ),
      ),
    );
  }
}

class _Progress extends StatelessWidget {
  const _Progress({required this.progress, required this.target});

  final int progress;
  final int target;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Row(
      children: [
        Expanded(
          child: ClipRRect(
            borderRadius: BorderRadius.circular(3),
            child: TweenAnimationBuilder<double>(
              tween: Tween(end: target == 0 ? 0 : progress / target),
              duration: JarvisMotion.of(context, JarvisMotion.slow),
              curve: JarvisMotion.standard,
              builder: (context, value, _) => LinearProgressIndicator(
                value: value,
                minHeight: 6,
                backgroundColor: colors.surfaceMuted,
                color: colors.accent,
              ),
            ),
          ),
        ),
        const SizedBox(width: 10),
        Text(
          '$progress of $target',
          style: TextStyle(
            fontFamily: 'Geist',
            fontSize: 12.5,
            color: colors.inkSoft,
            fontFeatures: const [FontFeature.tabularFigures()],
          ),
        ),
      ],
    );
  }
}

/// This week against the same days four weeks ago. The ghost is drawn in a
/// neutral tone so the two never differ by hue alone.
class _GhostCard extends StatelessWidget {
  const _GhostCard({required this.ghost});

  final Map<String, dynamic> ghost;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final metrics = jsonMaps(ghost['metrics']);
    final from = parseGameDate(ghost['ghostFrom']);
    final to = parseGameDate(ghost['ghostTo']);
    final ahead = asJsonInt(ghost['ahead'], 0);
    final behind = asJsonInt(ghost['behind'], 0);
    return FadeSlideIn(
      child: SurfaceCard(
        key: const Key('game-ghost'),
        margin: const EdgeInsets.only(bottom: 10),
        padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                IconBadge(
                  icon: PhosphorIconsRegular.clockCounterClockwise,
                  color: colors.accent,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        metrics.isEmpty
                            ? 'Your ghost is waiting'
                            : ghostSummary(ahead, behind),
                        style: TextStyle(
                          fontFamily: 'Geist',
                          fontSize: 15.5,
                          fontWeight: FontWeight.w600,
                          letterSpacing: -.25,
                          color: colors.ink,
                        ),
                      ),
                      if (from != null && to != null)
                        Text(
                          'This week so far against you on '
                          '${shortDay(from)} – ${shortDay(to)}',
                          style: TextStyle(
                            fontFamily: 'Geist',
                            fontSize: 13,
                            color: colors.inkSoft,
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
            if (metrics.isEmpty) ...[
              const SizedBox(height: 10),
              Text(
                'Check in a habit, log spending or write in your journal, and '
                'the race starts.',
                style: TextStyle(
                  fontFamily: 'Geist',
                  fontSize: 14,
                  height: 1.4,
                  color: colors.inkSoft,
                ),
              ),
            ],
            for (final metric in metrics) ...[
              const SizedBox(height: 14),
              _GhostMetric(metric: metric),
            ],
          ],
        ),
      ),
    );
  }
}

class _GhostMetric extends StatelessWidget {
  const _GhostMetric({required this.metric});

  final Map<String, dynamic> metric;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final now = (metric['now'] as num?) ?? 0;
    final ghost = (metric['ghost'] as num?) ?? 0;
    final unit = asJsonString(metric['unit']) ?? '';
    final verdict = asJsonString(metric['verdict']) ?? 'even';
    final max = now > ghost ? now : ghost;
    final label = asJsonString(metric['label']) ?? '';
    final verdictLabel = switch (verdict) {
      'ahead' => 'Ahead',
      'behind' => 'Behind',
      _ => 'Even',
    };
    return Semantics(
      label:
          '$label: now ${ghostValue(now, unit)}, ghost '
          '${ghostValue(ghost, unit)}. $verdictLabel.',
      excludeSemantics: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  label,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontFamily: 'Geist',
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: colors.ink,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              StatusPill(
                label: verdictLabel,
                color: switch (verdict) {
                  'ahead' => colors.accent,
                  'behind' => colors.warning,
                  _ => colors.muted,
                },
              ),
            ],
          ),
          const SizedBox(height: 6),
          _Bar(
            caption: 'You',
            value: ghostValue(now, unit),
            fraction: max == 0 ? 0 : now / max,
            color: colors.accent,
          ),
          const SizedBox(height: 4),
          _Bar(
            caption: 'Ghost',
            value: ghostValue(ghost, unit),
            fraction: max == 0 ? 0 : ghost / max,
            color: colors.outlineStrong,
          ),
        ],
      ),
    );
  }
}

class _Bar extends StatelessWidget {
  const _Bar({
    required this.caption,
    required this.value,
    required this.fraction,
    required this.color,
  });

  final String caption;
  final String value;
  final double fraction;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final style = TextStyle(
      fontFamily: 'Geist',
      fontSize: 12.5,
      color: colors.inkSoft,
      fontFeatures: const [FontFeature.tabularFigures()],
    );
    return Row(
      children: [
        SizedBox(
          width: 44 * MediaQuery.textScalerOf(context).scale(1).clamp(1, 2),
          child: Text(caption, style: style, maxLines: 1),
        ),
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) => Align(
              alignment: Alignment.centerLeft,
              child: AnimatedContainer(
                duration: JarvisMotion.of(context, JarvisMotion.slow),
                curve: JarvisMotion.standard,
                height: 8,
                width: (constraints.maxWidth * fraction).clamp(
                  4,
                  constraints.maxWidth,
                ),
                decoration: BoxDecoration(
                  color: color,
                  borderRadius: BorderRadius.circular(4),
                ),
              ),
            ),
          ),
        ),
        const SizedBox(width: 10),
        Text(value, style: style.copyWith(color: colors.ink)),
      ],
    );
  }
}

class _Quiet extends StatelessWidget {
  const _Quiet(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(4, 0, 4, 10),
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
