import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What a mission produced, as one page: the result, the sources it named,
/// what code checked, what is missing and what is left for you to decide.
class MissionResultScreen extends StatefulWidget {
  const MissionResultScreen({
    required this.http,
    required this.missionId,
    super.key,
  });

  final Dio http;
  final String missionId;

  @override
  State<MissionResultScreen> createState() => _MissionResultScreenState();
}

class _MissionResultScreenState extends State<MissionResultScreen> {
  Map<String, dynamic>? _result;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  String get _path => '/api/v1/missions/${widget.missionId}/deliverable';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      if (!mounted) return;
      setState(() {
        _result = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this result.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _save() async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>('$_path/save');
      if (!mounted) return;
      setState(() => _result = jsonObject(response.data) ?? _result);
      _toast('Saved privately to your Library.');
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'Could not save it.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _copy() async {
    final result = _result ?? const {};
    final text = [
      asJsonString(result['headline']) ?? '',
      for (final section in jsonMaps(result['sections']))
        '## ${asJsonString(section['title']) ?? ''}\n${asJsonString(section['text']) ?? ''}',
    ].join('\n\n');
    await Clipboard.setData(ClipboardData(text: text));
    if (mounted) _toast('Copied. Nothing was sent.');
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final result = _result ?? const <String, dynamic>{};
    final sections = jsonMaps(result['sections']);
    final evidence = jsonMaps(result['evidence']);
    final checks = jsonMaps(result['checks']);
    final limitations = jsonStrings(result['limitations']);
    final decisions = jsonStrings(result['nextDecisions']);
    final failedChecks = checks.where((c) => c['ok'] != true).length;
    final saved = asJsonString(result['libraryItemId']) != null;
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(asJsonString(result['title']) ?? 'Result'),
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
                          label: failedChecks == 0 ? 'Result' : 'Partly done',
                          title:
                              asJsonString(result['headline']) ??
                              'No result yet',
                          footer: HeroNote(
                            asJsonString(result['effort']) ?? '',
                          ),
                        ),
                      ),
                      const GroupLabel('What was checked'),
                      SurfaceCard(
                        padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
                        child: Column(
                          children: [
                            for (final (i, check) in checks.indexed)
                              Padding(
                                key: Key('result-check-$i'),
                                padding: const EdgeInsets.symmetric(
                                  vertical: 6,
                                ),
                                child: Row(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Icon(
                                      check['ok'] == true
                                          ? PhosphorIconsRegular.checkCircle
                                          : PhosphorIconsRegular.warningCircle,
                                      size: 20,
                                      color: check['ok'] == true
                                          ? colors.accent
                                          : colors.warning,
                                    ),
                                    const SizedBox(width: 10),
                                    Expanded(
                                      child: Text(
                                        asJsonString(check['label']) ?? '',
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 8),
                      Padding(
                        padding: const EdgeInsets.fromLTRB(4, 0, 4, 4),
                        child: Text(
                          asJsonString(result['reviewNote']) ?? '',
                          key: const Key('result-review-note'),
                          style: TextStyle(color: colors.muted, fontSize: 13),
                        ),
                      ),
                      if (limitations.isNotEmpty) ...[
                        const GroupLabel('Missing'),
                        for (final (i, limit) in limitations.indexed)
                          FeatureRow(
                            key: Key('result-limit-$i'),
                            index: i,
                            icon: PhosphorIconsRegular.info,
                            title: limit,
                          ),
                      ],
                      if (decisions.isNotEmpty) ...[
                        const GroupLabel('Left for you'),
                        for (final (i, decision) in decisions.indexed)
                          FeatureRow(
                            key: Key('result-decision-$i'),
                            index: i,
                            icon: PhosphorIconsRegular.handPointing,
                            title: decision,
                          ),
                      ],
                      const GroupLabel('The work'),
                      for (final (i, section) in sections.indexed)
                        FeatureRow(
                          key: Key('result-section-${section['stepKey']}'),
                          index: i,
                          icon: PhosphorIconsRegular.fileText,
                          caption: asJsonString(section['role']),
                          title: asJsonString(section['title']) ?? '',
                          subtitle: asJsonString(section['text']),
                        ),
                      if (evidence.isNotEmpty) ...[
                        const GroupLabel('Sources it named'),
                        for (final (i, item) in evidence.indexed)
                          FeatureRow(
                            key: Key('result-source-$i'),
                            index: i,
                            icon: PhosphorIconsRegular.link,
                            caption: asJsonString(item['host']),
                            title: asJsonString(item['url']) ?? '',
                            subtitle:
                                'From “${asJsonString(item['stepTitle']) ?? ''}”',
                          ),
                      ],
                      const SizedBox(height: 12),
                      Wrap(
                        spacing: 10,
                        runSpacing: 10,
                        children: [
                          PillAction(
                            key: const Key('result-save'),
                            primary: !saved,
                            icon: PhosphorIconsRegular.bookmarkSimple,
                            label: saved
                                ? 'Saved to your Library'
                                : 'Save to Library',
                            onPressed: _busy || saved
                                ? null
                                : () => unawaited(_save()),
                          ),
                          PillAction(
                            key: const Key('result-copy'),
                            icon: PhosphorIconsRegular.copy,
                            label: 'Copy the text',
                            onPressed: () => unawaited(_copy()),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}
