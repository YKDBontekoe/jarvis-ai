import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/mcp_setup.dart';

/// A short checklist that guides a new account through its first minutes. It
/// hides itself after a handful of messages, once every step is done, and when
/// usage cannot be read.
class GetStartedCard extends StatefulWidget {
  const GetStartedCard({
    required this.http,
    required this.briefing,
    required this.onSuggestion,
    this.refreshRevision = 0,
    super.key,
  });

  final Dio http;

  /// The home briefing, for which apps are connected. May still be loading.
  final Map<String, dynamic>? briefing;
  final ValueChanged<String>? onSuggestion;
  final int refreshRevision;

  @override
  State<GetStartedCard> createState() => _GetStartedCardState();
}

class _GetStartedCardState extends State<GetStartedCard> {
  Map<String, dynamic>? _usage;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(GetStartedCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.refreshRevision != widget.refreshRevision) {
      unawaited(_load());
    }
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/usage',
        queryParameters: const {'period': '7d'},
      );
      if (mounted) setState(() => _usage = jsonObject(response.data));
    } on DioException {
      if (mounted) setState(() => _usage = null);
    } catch (_) {
      if (mounted) setState(() => _usage = null);
    }
  }

  List<_Step>? get _steps {
    final usage = _usage;
    final suggest = widget.onSuggestion;
    if (usage == null || suggest == null) return null;
    final activity = jsonObject(usage['activity']) ?? const {};
    final sent = asJsonInt(jsonObject(activity['messagesSent'])?['total']);
    if (sent >= 20) return null;
    final memories = asJsonInt(
      jsonObject(usage['personalization'])?['activeMemories'],
    );
    final connected = jsonMaps(
      widget.briefing?['packs'],
    ).any((pack) => asJsonBool(pack['installed']));
    final steps = [
      _Step(
        'Say hello',
        'Ask what Jarvis can do',
        sent > 0,
        () => suggest('Hi Jarvis! What can you help me with?'),
      ),
      _Step(
        'Connect an app',
        'Calendar, mail, GitHub, and more',
        connected,
        () => suggest(mcpSetupPrompt),
      ),
      _Step(
        'Help Jarvis know you',
        'It remembers what matters to you',
        memories > 0,
        () => suggest(
          'Ask me a few questions so you can get to know me, and remember my answers.',
        ),
      ),
    ];
    return steps.every((step) => step.done) ? null : steps;
  }

  @override
  Widget build(BuildContext context) {
    final steps = _steps;
    if (steps == null) return const SizedBox.shrink();
    final colors = JarvisColors.of(context);
    final done = steps.where((step) => step.done).length;
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: SurfaceCard(
        key: const Key('home-get-started'),
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    'Get started',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                Text(
                  '$done of ${steps.length}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ),
            const SizedBox(height: 8),
            ClipRRect(
              borderRadius: BorderRadius.circular(4),
              child: LinearProgressIndicator(
                value: done / steps.length,
                minHeight: 4,
                backgroundColor: colors.surfaceMuted,
              ),
            ),
            const SizedBox(height: 6),
            for (final step in steps)
              InkWell(
                borderRadius: BorderRadius.circular(JarvisRadii.md),
                onTap: step.done ? null : step.onTap,
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 10),
                  child: Row(
                    children: [
                      Icon(
                        step.done
                            ? PhosphorIconsRegular.checkCircle
                            : PhosphorIconsRegular.circle,
                        size: 22,
                        color: step.done ? colors.accent : colors.muted,
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              step.label,
                              style: TextStyle(
                                fontWeight: FontWeight.w500,
                                color: step.done ? colors.muted : colors.ink,
                                decoration: step.done
                                    ? TextDecoration.lineThrough
                                    : null,
                              ),
                            ),
                            if (!step.done)
                              Text(
                                step.hint,
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                          ],
                        ),
                      ),
                      if (!step.done)
                        Icon(
                          PhosphorIconsRegular.caretRight,
                          size: 16,
                          color: colors.muted,
                        ),
                    ],
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _Step {
  const _Step(this.label, this.hint, this.done, this.onTap);

  final String label;
  final String hint;
  final bool done;
  final VoidCallback onTap;
}
