import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Describe a small tool and Jarvis builds it. Asks only the contract: what
/// you give it, where its data comes from, what you get back, and whether it
/// may change anything outside Jarvis. You review the code before it runs.
class BuildAppScreen extends StatefulWidget {
  const BuildAppScreen({required this.http, this.onAskInChat, super.key});

  final Dio http;
  final ValueChanged<String>? onAskInChat;

  @override
  State<BuildAppScreen> createState() => _BuildAppScreenState();
}

class _BuildAppScreenState extends State<BuildAppScreen> {
  List<Map<String, dynamic>> _examples = const [];
  final _purpose = TextEditingController();
  final _inputs = TextEditingController();
  final _data = TextEditingController();
  final _result = TextEditingController();
  bool _changesOutside = false;
  bool _busy = false;
  String? _problem;

  static const _icons = {
    'tracker': PhosphorIconsRegular.chartLine,
    'lookup': PhosphorIconsRegular.magnifyingGlass,
    'helper': PhosphorIconsRegular.listChecks,
    'calculator': PhosphorIconsRegular.table,
  };

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _purpose.dispose();
    _inputs.dispose();
    _data.dispose();
    _result.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/custom-mcp-servers/starters',
      );
      if (mounted) setState(() => _examples = jsonMaps(response.data));
    } on DioException {
      // The examples are a help; the form works without them.
    }
  }

  Future<void> _build() async {
    setState(() {
      _busy = true;
      _problem = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/custom-mcp-servers/starters/compose',
        data: {
          'purpose': _purpose.text.trim(),
          'inputs': _inputs.text.trim(),
          'data': _data.text.trim(),
          'result': _result.text.trim(),
          'changesOutside': _changesOutside,
        },
      );
      final prompt = asJsonString(jsonObject(response.data)?['prompt']);
      if (!mounted || prompt == null) return;
      Navigator.of(context).pop();
      widget.onAskInChat?.call(prompt);
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _problem =
              firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _field(
    String key,
    String label,
    TextEditingController controller,
    String hint,
  ) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(2, 16, 0, 6),
        child: Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
      ),
      TextField(
        key: Key('build-$key'),
        controller: controller,
        maxLines: null,
        decoration: InputDecoration(hintText: hint),
      ),
    ],
  );

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Build an app')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const FadeSlideIn(
                  child: HeroCard(
                    label: 'Your own tool',
                    title: 'Describe it, Jarvis builds it',
                    subtitle:
                        'You see the code and a test before anything runs. '
                        'It cannot change things outside Jarvis unless you say so.',
                  ),
                ),
                if (_examples.isNotEmpty) const GroupLabel('Ideas'),
                for (var i = 0; i < _examples.length; i++)
                  FeatureRow(
                    key: Key('build-example-${_examples[i]['id']}'),
                    index: i,
                    icon:
                        _icons[_examples[i]['id']] ??
                        PhosphorIconsRegular.puzzlePiece,
                    title: asJsonString(_examples[i]['title']) ?? '',
                    subtitle: asJsonString(_examples[i]['purpose']),
                    onTap: () => setState(
                      () => _purpose.text =
                          asJsonString(_examples[i]['starter']) ?? '',
                    ),
                  ),
                const GroupLabel('What it should do'),
                _field(
                  'purpose',
                  'What should it do?',
                  _purpose,
                  'For example: a tracker for my water intake.',
                ),
                _field(
                  'inputs',
                  'What will you give it? (optional)',
                  _inputs,
                  'A number, a date, some text.',
                ),
                _field(
                  'data',
                  'Where does its data come from? (optional)',
                  _data,
                  'A website or API you use. Leave empty and Jarvis checks the documentation.',
                ),
                _field(
                  'result',
                  'What do you want back? (optional)',
                  _result,
                  'A weekly total, a table, a yes or no.',
                ),
                SurfaceCard(
                  margin: const EdgeInsets.only(top: 16),
                  padding: const EdgeInsets.symmetric(
                    horizontal: 14,
                    vertical: 2,
                  ),
                  child: SwitchListTile(
                    key: const Key('build-outside'),
                    contentPadding: EdgeInsets.zero,
                    title: const Text('It may change things outside Jarvis'),
                    subtitle: const Text(
                      'Sending, posting or deleting. Each such action asks you first.',
                    ),
                    value: _changesOutside,
                    onChanged: (value) =>
                        setState(() => _changesOutside = value),
                  ),
                ),
                if (_problem != null)
                  InlineNotice(
                    key: const Key('build-problem'),
                    tone: NoticeTone.warning,
                    message: _problem!,
                    margin: const EdgeInsets.only(top: 14),
                  ),
                const SizedBox(height: 18),
                Align(
                  alignment: Alignment.centerLeft,
                  child: PillAction(
                    key: const Key('build-start'),
                    primary: true,
                    icon: PhosphorIconsRegular.sparkle,
                    label: 'Ask Jarvis to build it',
                    onPressed: _busy ? null : () => unawaited(_build()),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.only(top: 10),
                  child: Text(
                    'This opens a chat with your description. Jarvis writes and tests the app there; you switch it on from the app page.',
                    style: TextStyle(color: colors.muted, fontSize: 13),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
