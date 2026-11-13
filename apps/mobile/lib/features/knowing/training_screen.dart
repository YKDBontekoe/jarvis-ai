import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Train Jarvis: one thing it believes about you at a time. Swipe right for
/// true, left for wrong, or use the buttons (Outdated is a button only).
class TrainingScreen extends StatefulWidget {
  const TrainingScreen({required this.http, super.key});

  final Dio http;

  @override
  State<TrainingScreen> createState() => _TrainingScreenState();
}

class _TrainingScreenState extends State<TrainingScreen> {
  List<Map<String, dynamic>> _cards = const [];
  Map<String, dynamic>? _stats;
  int _index = 0;
  int _answered = 0;
  int _right = 0;
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
      final response = await widget.http.get<dynamic>(
        '/api/v1/knowing/training',
      );
      final data = jsonObject(response.data);
      if (!mounted) return;
      setState(() {
        _cards = jsonMaps(data?['cards']);
        _stats = jsonObject(data?['stats']);
        _index = 0;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load the cards.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Could not load the cards.';
      });
    }
  }

  Future<void> _answer(String verdict) async {
    if (_busy || _index >= _cards.length) return;
    final id = asJsonString(_cards[_index]['memoryId']);
    if (id == null) return;
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/knowing/training/$id',
        data: {'verdict': verdict},
      );
      unawaited(
        verdict == 'right'
            ? HapticFeedback.lightImpact()
            : HapticFeedback.selectionClick(),
      );
      if (!mounted) return;
      setState(() {
        _stats = jsonObject(jsonObject(response.data)?['stats']) ?? _stats;
        _answered++;
        if (verdict == 'right') _right++;
        _index++;
      });
    } on DioException catch (error) {
      _show(
        firstProblemMessage(error.response?.data) ??
            'Could not save the answer.',
      );
    } catch (_) {
      _show('Could not save the answer.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final done = !_loading && _index >= _cards.length;
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Train Jarvis')),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: false,
        onRetry: _load,
        empty: const SizedBox.shrink(),
        child: SafeArea(
          top: false,
          child: ContentWidth(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
              child: _loading || _error != null
                  ? const SizedBox.shrink()
                  : done
                  ? _summary()
                  : _deck(),
            ),
          ),
        ),
      ),
    );
  }

  Widget _deck() {
    final colors = JarvisColors.of(context);
    final card = _cards[_index];
    final id = asJsonString(card['memoryId']) ?? '$_index';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Card ${_index + 1} of ${_cards.length}'.toUpperCase(),
          style: JarvisType.capsOf(context),
        ),
        const SizedBox(height: 10),
        Expanded(
          child: Align(
            alignment: const Alignment(0, -.35),
            child: Dismissible(
              key: Key('training-card-$id'),
              direction: _busy
                  ? DismissDirection.none
                  : DismissDirection.horizontal,
              confirmDismiss: (direction) async {
                await _answer(
                  direction == DismissDirection.startToEnd ? 'right' : 'wrong',
                );
                return false;
              },
              background: _SwipeHint(
                label: 'True',
                icon: PhosphorIconsRegular.check,
                color: colors.accent,
                alignment: Alignment.centerLeft,
              ),
              secondaryBackground: _SwipeHint(
                label: 'Wrong',
                icon: PhosphorIconsRegular.x,
                color: colors.danger,
                alignment: Alignment.centerRight,
              ),
              child: SurfaceCard(
                padding: const EdgeInsets.fromLTRB(22, 22, 22, 22),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('JARVIS BELIEVES', style: JarvisType.capsOf(context)),
                    const SizedBox(height: 14),
                    // A card holds at most 300 characters, so a capped text
                    // block fits; a scroll view here would take all the height.
                    Text(
                      asJsonString(card['content']) ?? '',
                      key: const Key('training-content'),
                      maxLines: 8,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontFamily: 'Geist',
                        fontSize: 22,
                        height: 1.3,
                        fontWeight: FontWeight.w600,
                        letterSpacing: -.4,
                        color: colors.ink,
                      ),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      'About ${asJsonInt(card['confidencePercent'])}% sure · '
                      '${asJsonString(card['kind']) ?? 'memory'}',
                      style: TextStyle(
                        fontFamily: 'Geist',
                        fontSize: 13,
                        color: colors.inkSoft,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
        const SizedBox(height: 16),
        Wrap(
          alignment: WrapAlignment.center,
          spacing: 10,
          runSpacing: 10,
          children: [
            PillAction(
              key: const Key('training-wrong'),
              label: 'Wrong',
              icon: PhosphorIconsRegular.x,
              onPressed: _busy ? null : () => unawaited(_answer('wrong')),
            ),
            PillAction(
              key: const Key('training-outdated'),
              label: 'Outdated',
              icon: PhosphorIconsRegular.clockCounterClockwise,
              onPressed: _busy ? null : () => unawaited(_answer('outdated')),
            ),
            PillAction(
              key: const Key('training-right'),
              label: 'True',
              icon: PhosphorIconsRegular.check,
              primary: true,
              onPressed: _busy ? null : () => unawaited(_answer('right')),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          'Wrong deletes it. Outdated keeps it as history.',
          textAlign: TextAlign.center,
          style: TextStyle(
            fontFamily: 'Geist',
            fontSize: 12.5,
            color: colors.muted,
          ),
        ),
      ],
    );
  }

  Widget _summary() {
    final accuracy = _stats?['accuracyPercent'];
    final streak = asJsonInt(_stats?['streakDays']);
    return ListView(
      children: [
        HeroCard(
          key: const Key('training-summary'),
          label: _answered == 0 ? 'All caught up' : 'Round done',
          title: _answered == 0
              ? 'Nothing to check'
              : 'Right $_right of $_answered',
          subtitle: [
            if (accuracy is num) 'Overall ${accuracy.round()}% right',
            if (streak > 0) streak == 1 ? '1 day streak' : '$streak day streak',
          ].join(' · '),
          footer: const HeroNote(
            'Jarvis now relies on what you confirmed and has forgotten what '
            'you said was wrong.',
          ),
        ),
        const SizedBox(height: 16),
        Center(
          child: InkButton(
            onPressed: () => Navigator.of(context).maybePop(),
            child: const Text('Done'),
          ),
        ),
      ],
    );
  }
}

class _SwipeHint extends StatelessWidget {
  const _SwipeHint({
    required this.label,
    required this.icon,
    required this.color,
    required this.alignment,
  });

  final String label;
  final IconData icon;
  final Color color;
  final Alignment alignment;

  @override
  Widget build(BuildContext context) => Container(
    alignment: alignment,
    padding: const EdgeInsets.symmetric(horizontal: 28),
    decoration: BoxDecoration(
      color: color.withValues(alpha: .12),
      borderRadius: BorderRadius.circular(JarvisRadii.lg),
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, color: color, size: 28),
        const SizedBox(height: 4),
        Text(
          label,
          style: TextStyle(
            fontFamily: 'Geist',
            fontWeight: FontWeight.w600,
            color: color,
          ),
        ),
      ],
    ),
  );
}
