import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../l10n/l10n.dart';
import '../../ui/feature_kit.dart';
import '../../ui/phosphor_icons.dart';

/// A calm route back into work the owner explicitly enabled. Older servers omit the card.
class ProactiveHomeCard extends StatefulWidget {
  const ProactiveHomeCard({
    required this.http,
    required this.onOpen,
    this.revision = 0,
    super.key,
  });
  final Dio http;
  final ValueChanged<String> onOpen;
  final int revision;
  @override
  State<ProactiveHomeCard> createState() => _ProactiveHomeCardState();
}

class _ProactiveHomeCardState extends State<ProactiveHomeCard> {
  Map<String, dynamic>? _leading;
  int _request = 0;
  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(ProactiveHomeCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.revision != widget.revision) unawaited(_load());
  }

  Future<void> _load() async {
    final request = ++_request;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/proactivity/cases',
      );
      final active = jsonMaps(response.data)
          .where((c) => c['enabled'] == true && c['status'] == 'open');
      if (mounted && request == _request) {
        setState(() => _leading = active.firstOrNull);
      }
    } on DioException {
      if (mounted && request == _request) setState(() => _leading = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = _leading;
    if (value == null) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: FeatureRow(
        icon: PhosphorIconsRegular.sparkle,
        caption: context.l10n.proactiveTitle,
        title: asJsonString(value['title']) ?? '',
        subtitle: asJsonString(value['nextAction']),
        actions: [
          PillAction(
            label: context.l10n.proactiveOpen,
            onPressed: () => widget.onOpen('proactive:${value['id']}'),
          ),
        ],
      ),
    );
  }
}
