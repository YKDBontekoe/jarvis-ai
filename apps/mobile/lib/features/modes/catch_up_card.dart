import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/phosphor_icons.dart';

/// What a quiet mode held back, grouped by kind, so coming back is one glance
/// instead of a pile. The owner decides which kinds belong in the recap;
/// approvals and problems always do. Leaving a kind out only hides it here and
/// from the summary push: the notifications themselves stay where they are.
class CatchUpCard extends StatefulWidget {
  const CatchUpCard({
    required this.http,
    required this.data,
    required this.onChanged,
    super.key,
  });

  final Dio http;
  final Map<String, dynamic> data;
  final Future<void> Function() onChanged;

  @override
  State<CatchUpCard> createState() => _CatchUpCardState();
}

class _CatchUpCardState extends State<CatchUpCard> {
  bool _busy = false;

  Future<void> _call(Future<Response<dynamic>> Function() request) async {
    setState(() => _busy = true);
    try {
      await request();
      await widget.onChanged();
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

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final groups = jsonMaps(widget.data['groups']);
    if (groups.isEmpty) return const SizedBox.shrink();
    final total = asJsonInt(widget.data['total']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        GroupLabel('While you were away', count: total == 0 ? null : total),
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
          child: Text(
            asJsonString(widget.data['summary']) ?? '',
            key: const Key('catch-up-summary'),
            style: TextStyle(color: colors.inkSoft),
          ),
        ),
        for (final (i, group) in groups.indexed)
          FeatureRow(
            key: Key('catch-up-${group['domain']}'),
            index: i,
            icon: PhosphorIconsRegular.bellSlash,
            quiet: !asJsonBool(group['included']),
            caption: asJsonBool(group['included'])
                ? '${asJsonInt(group['count'])} HELD'
                : 'LEFT OUT OF THE RECAP',
            title: asJsonString(group['title']) ?? '',
            subtitle: asJsonBool(group['included'])
                ? [
                    for (final item in jsonMaps(group['items']))
                      asJsonInt(item['count']) > 1
                          ? '${asJsonString(item['title'])} ×${asJsonInt(item['count'])}'
                          : asJsonString(item['title']) ?? '',
                  ].join('\n')
                : 'Still in your notifications.',
            actions: asJsonBool(group['locked'])
                ? null
                : [
                    PillAction(
                      key: Key('catch-up-toggle-${group['domain']}'),
                      compact: true,
                      label: asJsonBool(group['included'])
                          ? 'Leave out'
                          : 'Include',
                      onPressed: _busy
                          ? null
                          : () => unawaited(
                              _call(
                                () => widget.http.put<dynamic>(
                                  '/api/v1/modes/catch-up/domains',
                                  data: {
                                    'domain': group['domain'],
                                    'included': !asJsonBool(group['included']),
                                  },
                                ),
                              ),
                            ),
                    ),
                  ],
          ),
        if (!asJsonBool(widget.data['stillHolding']) && total > 0)
          Padding(
            padding: const EdgeInsets.only(top: 4, bottom: 16),
            child: PillAction(
              key: const Key('catch-up-clear'),
              icon: PhosphorIconsRegular.checkCircle,
              label: 'Got it, clear',
              onPressed: _busy
                  ? null
                  : () => unawaited(
                      _call(
                        () => widget.http.post<dynamic>(
                          '/api/v1/modes/catch-up/clear',
                        ),
                      ),
                    ),
            ),
          ),
      ],
    );
  }
}
