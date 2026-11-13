import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'training_screen.dart';

/// How well Jarvis knows you: train its memory, clear the map of the unknown
/// one question at a time, and see which kinds of action it has earned trust in.
class KnowingScreen extends StatefulWidget {
  const KnowingScreen({required this.http, super.key});

  final Dio http;

  @override
  State<KnowingScreen> createState() => _KnowingScreenState();
}

class _KnowingScreenState extends State<KnowingScreen> {
  Map<String, dynamic>? _training;
  Map<String, dynamic>? _map;
  List<Map<String, dynamic>> _trust = const [];
  bool _loading = true;
  String? _error;
  bool _saving = false;
  final _answer = TextEditingController();
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _answer.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final revision = ++_revision;
    try {
      final results = await Future.wait([
        widget.http.get<dynamic>('/api/v1/knowing/training'),
        widget.http.get<dynamic>('/api/v1/knowing/map'),
        widget.http.get<dynamic>('/api/v1/knowing/trust'),
      ]);
      if (!mounted || revision != _revision) return;
      setState(() {
        _training = jsonObject(results[0].data);
        _map = jsonObject(results[1].data);
        _trust = jsonMaps(results[2].data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load what Jarvis knows.';
      });
    } catch (_) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load what Jarvis knows.';
      });
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _train() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(builder: (_) => TrainingScreen(http: widget.http)),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _answerGap(Map<String, dynamic> gap, {bool skip = false}) async {
    final text = _answer.text.trim();
    if (!skip && text.isEmpty) return;
    setState(() => _saving = true);
    try {
      final response = await widget.http.post<dynamic>(
        skip ? '/api/v1/knowing/map/skip' : '/api/v1/knowing/map/answer',
        data: {
          'entityId': gap['entityId'],
          'predicate': gap['predicate'],
          if (!skip) 'answer': text,
        },
      );
      final data = jsonObject(response.data);
      _answer.clear();
      unawaited(HapticFeedback.lightImpact());
      if (!mounted) return;
      setState(() => _map = skip ? data : jsonObject(data?['map']) ?? _map);
      _show(
        skip ? 'Jarvis won’t ask that again.' : 'Saved. A little less fog.',
      );
    } on DioException catch (error) {
      _show(
        firstProblemMessage(error.response?.data) ?? 'Could not save that.',
      );
    } catch (_) {
      _show('Could not save that.');
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _handOver(Map<String, dynamic> rung) async {
    final category = asJsonString(rung['category']);
    if (category == null) return;
    final label = asJsonString(rung['label']) ?? 'this';
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Hand this over?',
      message:
          'For the next 90 days Jarvis does “$label” without asking. You '
          'can take it back here any time, and one decline would have '
          'started the climb again.',
      cancelLabel: 'Keep asking',
      confirmLabel: 'Hand it over',
      icon: PhosphorIconsRegular.handPointing,
    );
    if (!confirmed) return;
    await _trustCall(
      () => widget.http.post<dynamic>(
        '/api/v1/knowing/trust/${Uri.encodeComponent(category)}/hand-over',
      ),
      'Handed over for 90 days.',
    );
  }

  Future<void> _takeBack(Map<String, dynamic> rung) async {
    final category = asJsonString(rung['category']);
    if (category == null) return;
    await _trustCall(
      () => widget.http.delete<dynamic>(
        '/api/v1/knowing/trust/${Uri.encodeComponent(category)}',
      ),
      'Jarvis asks first again.',
    );
  }

  Future<void> _trustCall(
    Future<Response<dynamic>> Function() call,
    String done,
  ) async {
    try {
      await call();
      unawaited(HapticFeedback.selectionClick());
      _show(done);
      await _load();
    } on DioException catch (error) {
      _show(
        firstProblemMessage(error.response?.data) ?? 'Could not change that.',
      );
    } catch (_) {
      _show('Could not change that.');
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const PageTitle('Know me')),
    body: ListScreenBody(
      loading: _loading,
      error: _training == null ? _error : null,
      isEmpty: false,
      onRetry: _load,
      empty: const SizedBox.shrink(),
      child: OrbRefresh(
        onRefresh: _load,
        child: ListView(
          key: const Key('knowing-list'),
          keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
          padding: EdgeInsets.fromLTRB(
            16,
            8,
            16,
            32 + MediaQuery.paddingOf(context).bottom,
          ),
          children: [
            ContentWidth(child: _hero()),
            const ContentWidth(child: GroupLabel('Train Jarvis')),
            ContentWidth(child: _trainRow()),
            const ContentWidth(child: GroupLabel('Map of the unknown')),
            ContentWidth(child: _mapCard()),
            ContentWidth(
              child: GroupLabel('Trust ladder', count: _trust.length),
            ),
            if (_trust.isEmpty)
              const ContentWidth(
                child: _Quiet(
                  'Every approval you give teaches Jarvis. Approve one kind '
                  'of action ten times in a row and you can hand it over.',
                ),
              ),
            for (final (i, rung) in _trust.indexed)
              ContentWidth(
                child: _TrustRow(
                  rung: rung,
                  index: i,
                  onHandOver: () => unawaited(_handOver(rung)),
                  onTakeBack: () => unawaited(_takeBack(rung)),
                ),
              ),
          ],
        ),
      ),
    ),
  );

  Widget _hero() {
    final stats = jsonObject(_training?['stats']) ?? const <String, dynamic>{};
    final accuracy = stats['accuracyPercent'];
    final today = asJsonInt(stats['today']);
    final goal = asJsonInt(stats['dailyGoal'], 5);
    final streak = asJsonInt(stats['streakDays']);
    final fog = asJsonInt(_map?['fogPercent']);
    final expected = asJsonInt(_map?['expected']);
    return HeroCard(
      key: const Key('knowing-hero'),
      label: 'Jarvis knows you',
      title: accuracy is num ? '${accuracy.round()}% right' : 'Not tested yet',
      maxTitleScale: 1.6,
      subtitle: [
        if (expected > 0) '${100 - fog}% of the basics known',
        if (streak > 0) streak == 1 ? '1 day streak' : '$streak day streak',
        '$today of $goal cards today',
      ].join(' · '),
      segments: goal,
      doneSegments: today.clamp(0, goal),
      footer: const HeroNote(
        'Right is how often Jarvis had it right when you checked. Wrong '
        'memories are deleted; outdated ones are ended.',
      ),
    );
  }

  Widget _trainRow() {
    final cards = jsonMaps(_training?['cards']);
    final stats = jsonObject(_training?['stats']) ?? const <String, dynamic>{};
    final left = asJsonInt(stats['reviewableLeft']);
    return FeatureRow(
      key: const Key('knowing-train'),
      icon: PhosphorIconsRegular.brain,
      title: cards.isEmpty
          ? 'Nothing to check right now'
          : 'Check ${cards.length == 1 ? '1 thing' : '${cards.length} things'} Jarvis believes',
      subtitle: cards.isEmpty
          ? 'New cards appear as Jarvis learns more about you.'
          : 'True, wrong or outdated, one swipe each. '
                '${left == 1 ? '1 card' : '$left cards'} waiting.',
      onTap: cards.isEmpty ? null : () => unawaited(_train()),
      actions: cards.isEmpty
          ? null
          : [
              PillAction(
                key: const Key('knowing-train-start'),
                label: 'Start',
                icon: PhosphorIconsRegular.play,
                primary: true,
                compact: true,
                onPressed: () => unawaited(_train()),
              ),
            ],
    );
  }

  Widget _mapCard() {
    final colors = JarvisColors.of(context);
    final map = _map ?? const <String, dynamic>{};
    final fog = asJsonInt(map['fogPercent']);
    final known = asJsonInt(map['known']);
    final expected = asJsonInt(map['expected']);
    final gap = jsonObject(map['question']);
    final entities = jsonMaps(map['entities']);
    return SurfaceCard(
      key: const Key('knowing-map'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            expected == 0
                ? 'Jarvis hasn’t met anyone yet'
                : '$fog% still in fog',
            style: TextStyle(
              fontFamily: 'Geist',
              fontSize: 15.5,
              fontWeight: FontWeight.w600,
              letterSpacing: -.25,
              color: colors.ink,
            ),
          ),
          Text(
            expected == 0
                ? 'As people and projects come up in chat, this map fills in.'
                : '$known of $expected basics known about the people and '
                      'projects Jarvis knows best.',
            style: TextStyle(
              fontFamily: 'Geist',
              fontSize: 13.5,
              height: 1.35,
              color: colors.inkSoft,
            ),
          ),
          if (entities.isNotEmpty) ...[
            const SizedBox(height: 12),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final entity in entities.take(12))
                  _FogChip(
                    name: asJsonString(entity['name']) ?? '',
                    known: asJsonInt(entity['known']),
                    expected: asJsonInt(entity['expected'], 1),
                  ),
              ],
            ),
          ],
          if (gap != null) ...[
            const SizedBox(height: 16),
            Text(
              'TODAY’S QUESTION',
              style: JarvisType.capsOf(context).copyWith(fontSize: 10.5),
            ),
            const SizedBox(height: 4),
            Text(
              asJsonString(gap['question']) ?? '',
              key: const Key('knowing-question'),
              style: TextStyle(
                fontFamily: 'Geist',
                fontSize: 16,
                fontWeight: FontWeight.w600,
                color: colors.ink,
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              key: const Key('knowing-answer'),
              controller: _answer,
              maxLength: 200,
              buildCounter: quietCounter,
              textCapitalization: TextCapitalization.sentences,
              textInputAction: TextInputAction.done,
              onSubmitted: (_) => unawaited(_answerGap(gap)),
              decoration: const InputDecoration(hintText: 'Your answer'),
            ),
            const SizedBox(height: 4),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                PillAction(
                  key: const Key('knowing-answer-save'),
                  label: 'Save',
                  primary: true,
                  compact: true,
                  onPressed: _saving ? null : () => unawaited(_answerGap(gap)),
                ),
                PillAction(
                  key: const Key('knowing-answer-skip'),
                  label: 'I don’t know',
                  compact: true,
                  onPressed: _saving
                      ? null
                      : () => unawaited(_answerGap(gap, skip: true)),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

/// One person or project on the map: filled dots for what is known.
class _FogChip extends StatelessWidget {
  const _FogChip({
    required this.name,
    required this.known,
    required this.expected,
  });

  final String name;
  final int known;
  final int expected;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final clear = known >= expected;
    return Semantics(
      label: '$name: $known of $expected known',
      excludeSemantics: true,
      child: Container(
        padding: const EdgeInsets.fromLTRB(10, 6, 10, 6),
        decoration: BoxDecoration(
          color: clear ? colors.accentSoft : colors.surfaceMuted,
          borderRadius: BorderRadius.circular(999),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Flexible(
              child: Text(
                name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontFamily: 'Geist',
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                  color: clear ? colors.accentDeep : colors.ink,
                ),
              ),
            ),
            const SizedBox(width: 6),
            for (var i = 0; i < expected; i++)
              Container(
                width: 6,
                height: 6,
                margin: const EdgeInsets.only(left: 2),
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: i < known ? colors.accent : colors.outlineStrong,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _TrustRow extends StatelessWidget {
  const _TrustRow({
    required this.rung,
    required this.index,
    required this.onHandOver,
    required this.onTakeBack,
  });

  final Map<String, dynamic> rung;
  final int index;
  final VoidCallback onHandOver;
  final VoidCallback onTakeBack;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final level = asJsonString(rung['level']) ?? 'new';
    final streak = asJsonInt(rung['streak']);
    final next = asJsonInt(rung['nextAt'], 3);
    final declined = asJsonInt(rung['declined']);
    final handsFree = level == 'hands_free';
    final canHandOver = rung['canHandOver'] == true;
    final levelLabel = switch (level) {
      'hands_free' => 'Hands-free',
      'trusted' => 'Trusted',
      'learning' => 'Learning',
      _ => 'New',
    };
    final subtitle = handsFree
        ? 'Jarvis does this without asking.'
        : level == 'trusted'
        ? (canHandOver
              ? '$streak approvals in a row. You can hand it over.'
              : '$streak approvals in a row. This kind of action always asks.')
        : '$streak of $next approvals in a row to reach '
              '${level == 'learning' ? 'Trusted' : 'Learning'}'
              '${declined > 0 ? ' · a decline restarts the climb' : ''}';
    return FeatureRow(
      key: Key('knowing-trust-${rung['category']}'),
      index: index,
      icon: handsFree
          ? PhosphorIconsRegular.sealCheck
          : PhosphorIconsRegular.shieldCheck,
      badgeColor: level == 'new' ? colors.muted : colors.accent,
      caption: levelLabel,
      title: asJsonString(rung['label']) ?? 'Action',
      subtitle: subtitle,
      extra: handsFree || level == 'trusted'
          ? null
          : ClipRRect(
              borderRadius: BorderRadius.circular(3),
              child: LinearProgressIndicator(
                value: next == 0 ? 0 : (streak / next).clamp(0, 1).toDouble(),
                minHeight: 6,
                backgroundColor: colors.surfaceMuted,
                color: colors.accent,
              ),
            ),
      actions: [
        if (canHandOver)
          PillAction(
            key: Key('knowing-hand-over-${rung['category']}'),
            label: 'Hand it over',
            primary: true,
            compact: true,
            onPressed: onHandOver,
          ),
        if (handsFree)
          PillAction(
            key: Key('knowing-take-back-${rung['category']}'),
            label: 'Take it back',
            compact: true,
            onPressed: onTakeBack,
          ),
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
