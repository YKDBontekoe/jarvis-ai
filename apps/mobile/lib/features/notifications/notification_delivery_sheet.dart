import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/phosphor_icons.dart';

/// What actually happened to one notification, step by step. It never claims
/// more than Jarvis knows: a push handed to the provider is not a phone showing it.
class NotificationDeliverySheet extends StatefulWidget {
  const NotificationDeliverySheet({
    required this.http,
    required this.notificationId,
    super.key,
  });

  final Dio http;
  final String notificationId;

  @override
  State<NotificationDeliverySheet> createState() =>
      _NotificationDeliverySheetState();
}

class _NotificationDeliverySheetState extends State<NotificationDeliverySheet> {
  List<Map<String, dynamic>>? _steps;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/notifications/${widget.notificationId}/delivery',
      );
      if (mounted) {
        setState(() => _steps = jsonMaps(jsonObject(response.data)?['steps']));
      }
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not load how this was delivered.',
        );
      }
    }
  }

  IconData _icon(String state) => switch (state) {
    'created' => PhosphorIconsRegular.bell,
    'provider_accepted' || 'read' => PhosphorIconsRegular.checkCircle,
    'held' || 'app_only' => PhosphorIconsRegular.bellSlash,
    'failed' => PhosphorIconsRegular.warningCircle,
    _ => PhosphorIconsRegular.clock,
  };

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'How it was delivered',
              style: JarvisType.displayOf(context).copyWith(fontSize: 26),
            ),
            const SizedBox(height: 12),
            if (_error != null)
              Text(_error!, key: const Key('delivery-error'))
            else if (_steps == null)
              const Center(child: CircularProgressIndicator(strokeWidth: 2))
            else
              for (final (i, step) in _steps!.indexed)
                FeatureRow(
                  key: Key('delivery-${step['state']}-$i'),
                  index: i,
                  icon: _icon(asJsonString(step['state']) ?? ''),
                  badgeColor: asJsonString(step['state']) == 'failed'
                      ? colors.warning
                      : null,
                  title: asJsonString(step['text']) ?? '',
                  subtitle: jsonDate(step['at'], local: true) == null
                      ? null
                      : MaterialLocalizations.of(context).formatTimeOfDay(
                          TimeOfDay.fromDateTime(
                            jsonDate(step['at'], local: true)!,
                          ),
                        ),
                ),
          ],
        ),
      ),
    );
  }
}
