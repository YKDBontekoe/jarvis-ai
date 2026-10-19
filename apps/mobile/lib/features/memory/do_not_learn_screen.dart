import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Topics Jarvis must never learn from. A message that mentions one is not
/// looked at for memories at all, and a memory about one is never saved. What
/// you ask Jarvis to remember on purpose is not blocked by this list.
class DoNotLearnScreen extends StatefulWidget {
  const DoNotLearnScreen({required this.http, super.key});

  final Dio http;

  @override
  State<DoNotLearnScreen> createState() => _DoNotLearnScreenState();
}

class _DoNotLearnScreenState extends State<DoNotLearnScreen> {
  final _topic = TextEditingController();
  List<String> _topics = const [];
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _topic.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/memories/do-not-learn',
      );
      if (!mounted) return;
      setState(() {
        _topics = jsonStrings(jsonObject(response.data)?['topics']);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this list.';
      });
    }
  }

  Future<void> _save(List<String> topics) async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/memories/do-not-learn',
        data: {'topics': topics},
      );
      if (mounted) {
        setState(
          () => _topics = jsonStrings(jsonObject(response.data)?['topics']),
        );
      }
    } on DioException catch (error) {
      if (mounted) {
        ScaffoldMessenger.maybeOf(context)
          ?..hideCurrentSnackBar()
          ..showSnackBar(
            SnackBar(
              content: Text(
                firstProblemMessage(error.response?.data) ??
                    'That did not work.',
              ),
            ),
          );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _add() async {
    final text = _topic.text.trim();
    if (text.isEmpty) return;
    _topic.clear();
    await _save([..._topics, text]);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Never learn about')),
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
                      const HeroCard(
                        key: Key('dnl-hero'),
                        label: 'YOUR RULE',
                        title: 'Some things stay unlearned',
                        subtitle: 'A message that mentions one of these is not looked at for memories, and nothing about it is saved. Asking Jarvis to remember something on purpose still works.',
                      ),
                      const SizedBox(height: 16),
                      TextField(
                        key: const Key('dnl-topic'),
                        controller: _topic,
                        maxLength: 60,
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => unawaited(_add()),
                        decoration: const InputDecoration(
                          labelText: 'A topic',
                          hintText: 'My health',
                        ),
                      ),
                      PillAction(
                        key: const Key('dnl-add'),
                        primary: true,
                        icon: PhosphorIconsRegular.plus,
                        label: 'Never learn about this',
                        onPressed: _busy ? null : () => unawaited(_add()),
                      ),
                      const GroupLabel('Your list'),
                      if (_topics.isEmpty)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
                          child: Text(
                            'Nothing yet. Jarvis learns from what you say, within your other settings.',
                            key: const Key('dnl-empty'),
                            style: TextStyle(color: colors.muted),
                          ),
                        ),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          for (final topic in _topics)
                            InputChip(
                              key: Key('dnl-chip-$topic'),
                              label: Text(topic),
                              deleteIcon: Icon(
                                Icons.close,
                                size: 18,
                                key: Key('dnl-remove-$topic'),
                              ),
                              deleteButtonTooltipMessage: 'Remove $topic',
                              onDeleted: _busy
                                  ? null
                                  : () => unawaited(
                                      _save([
                                        for (final t in _topics)
                                          if (t != topic) t,
                                      ]),
                                    ),
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
