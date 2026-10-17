import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// A short lesson built only from one saved item: quoted sentences with
/// where they came from, fill-in questions and an explain-it-back prompt that
/// are checked against that same text, and a small set of cards you choose to
/// keep for spaced review. Not knowing is fine; a grade can be corrected.
class LessonScreen extends StatefulWidget {
  const LessonScreen({required this.http, required this.itemId, super.key});

  final Dio http;
  final String itemId;

  @override
  State<LessonScreen> createState() => _LessonScreenState();
}

class _LessonScreenState extends State<LessonScreen> {
  Map<String, dynamic>? _lesson;
  bool _loading = true;
  String? _error;
  final _answers = <String, TextEditingController>{};
  final _grades = <String, Map<String, dynamic>>{};
  final _corrected = <String>{};
  final _keep = <String>{};
  final _checking = <String>{};
  bool _saving = false;

  String get _path => '/api/v1/library/${widget.itemId}/lesson';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    for (final controller in _answers.values) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      if (!mounted) return;
      setState(() {
        _lesson = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not build a lesson.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _check(String id) async {
    setState(() => _checking.add(id));
    try {
      final response = await widget.http.post<dynamic>(
        '$_path/check',
        data: {'questionId': id, 'answer': _answers[id]?.text ?? ''},
      );
      if (!mounted) return;
      setState(() {
        _grades[id] = jsonObject(response.data) ?? const {};
        _corrected.remove(id);
      });
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'Could not check that.',
        );
      }
    } finally {
      if (mounted) setState(() => _checking.remove(id));
    }
  }

  Future<void> _saveCards() async {
    setState(() => _saving = true);
    try {
      final response = await widget.http.post<dynamic>(
        '$_path/cards',
        data: {'questionIds': _keep.toList()},
      );
      final result = jsonObject(response.data) ?? const {};
      if (!mounted) return;
      final added = asJsonInt(result['added']);
      final skipped = asJsonInt(result['skipped']);
      _toast(
        'Kept $added card${added == 1 ? '' : 's'}'
        '${skipped > 0 ? ' ($skipped already existed)' : ''}. They show up in your due cards.',
      );
      setState(_keep.clear);
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ??
              'Could not keep those cards.',
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final lesson = _lesson;
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(asJsonString(lesson?['title']) ?? 'Lesson'),
      ),
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
                      FadeSlideIn(
                        child: HeroCard(
                          label:
                              'About ${asJsonInt(lesson?['minutes'])} minutes',
                          title: asJsonString(lesson?['overview']) ?? '',
                          footer: Text(
                            asJsonString(lesson?['note']) ?? '',
                            key: const Key('lesson-note'),
                            style: TextStyle(
                              fontFamily: 'Geist',
                              color: colors.onInk.withValues(alpha: .62),
                              fontSize: 13,
                              height: 1.35,
                            ),
                          ),
                        ),
                      ),
                      const GroupLabel('From your text'),
                      for (final (i, point) in jsonMaps(
                        lesson?['points'],
                      ).indexed)
                        FeatureRow(
                          index: i,
                          icon: PhosphorIconsRegular.bookmarkSimple,
                          title: '“${asJsonString(point['text']) ?? ''}”',
                        ),
                      const GroupLabel('Check yourself'),
                      for (final question in jsonMaps(lesson?['questions']))
                        _question(question, colors),
                      if (_keep.isNotEmpty)
                        Align(
                          alignment: Alignment.centerLeft,
                          child: PillAction(
                            key: const Key('lesson-keep-cards'),
                            primary: true,
                            icon: PhosphorIconsRegular.bookmarkSimple,
                            label:
                                'Keep ${_keep.length} card${_keep.length == 1 ? '' : 's'} for review',
                            onPressed: _saving
                                ? null
                                : () => unawaited(_saveCards()),
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _question(Map<String, dynamic> question, JarvisColors colors) {
    final id = asJsonString(question['id']) ?? '';
    final controller = _answers.putIfAbsent(id, TextEditingController.new);
    final grade = _grades[id];
    final isBlank = question['kind'] == 'blank';
    final verdict = _corrected.contains(id)
        ? 'correct'
        : asJsonString(grade?['verdict']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: SurfaceCard(
        key: Key('lesson-q-$id'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              asJsonString(question['prompt']) ?? '',
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
            Text(
              asJsonString(question['hint']) ?? '',
              style: TextStyle(color: colors.muted, fontSize: 13),
            ),
            TextField(
              key: Key('lesson-answer-$id'),
              controller: controller,
              decoration: const InputDecoration(
                hintText: 'Your answer (or leave it empty if you do not know)',
              ),
            ),
            Align(
              alignment: Alignment.centerLeft,
              child: SoftButton(
                key: Key('lesson-check-$id'),
                onPressed: _checking.contains(id)
                    ? null
                    : () => unawaited(_check(id)),
                child: const Text('Check'),
              ),
            ),
            if (grade != null) ...[
              Text(
                '${_label(verdict)} ${asJsonString(grade['message']) ?? ''}',
                key: Key('lesson-grade-$id'),
              ),
              const SizedBox(height: 4),
              Text(
                'Your text: “${asJsonString(jsonObject(grade['source'])?['quote']) ?? ''}”',
                style: TextStyle(color: colors.inkSoft, fontSize: 13),
              ),
              if (verdict != 'correct' && verdict != 'unsure')
                SoftButton(
                  key: Key('lesson-correct-$id'),
                  onPressed: () => setState(() => _corrected.add(id)),
                  child: const Text('I was right, correct this'),
                ),
            ],
            if (isBlank)
              CheckboxListTile(
                key: Key('lesson-card-$id'),
                contentPadding: EdgeInsets.zero,
                controlAffinity: ListTileControlAffinity.leading,
                title: const Text('Keep as a card'),
                value: _keep.contains(id),
                onChanged: (on) => setState(() {
                  on == true ? _keep.add(id) : _keep.remove(id);
                }),
              ),
          ],
        ),
      ),
    );
  }

  String _label(String? verdict) => switch (verdict) {
    'correct' => 'Correct.',
    'close' => 'Close.',
    'incorrect' => 'Not quite.',
    'unsure' => 'No problem.',
    _ => '',
  };
}
