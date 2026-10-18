import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/feature_kit.dart';
import '../../ui/phosphor_icons.dart';

/// One tap of feedback about something Jarvis made. Saying nothing is never
/// counted as a yes; each button is stored as exactly what it says.
class OutcomeFeedbackRow extends StatefulWidget {
  const OutcomeFeedbackRow({
    required this.http,
    required this.artifactKind,
    this.artifactId,
    this.profileId,
    super.key,
  });

  final Dio http;
  final String artifactKind;
  final String? artifactId;
  final String? profileId;

  @override
  State<OutcomeFeedbackRow> createState() => _OutcomeFeedbackRowState();
}

class _OutcomeFeedbackRowState extends State<OutcomeFeedbackRow> {
  String? _message;
  bool _busy = false;

  Future<void> _send(String signal, {String? aspect}) async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/outcomes/feedback',
        data: {
          'artifactKind': widget.artifactKind,
          'artifactId': ?widget.artifactId,
          'signal': signal,
          'aspect': ?aspect,
          'profileId': ?widget.profileId,
        },
      );
      if (mounted) {
        setState(
          () => _message =
              asJsonString(jsonObject(response.data)?['message']) ?? 'Noted.',
        );
      }
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _message =
              firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => FeatureRow(
    key: const Key('feedback-row'),
    icon: PhosphorIconsRegular.chatCircle,
    caption: 'HOW WAS THIS?',
    title: _message ?? 'Your answer helps Jarvis help you better.',
    subtitle: _message == null
        ? 'Nothing changes by itself; you review any change first.'
        : null,
    actions: _message != null
        ? null
        : [
            PillAction(
              key: const Key('feedback-useful'),
              compact: true,
              primary: true,
              label: 'Useful',
              onPressed: _busy ? null : () => unawaited(_send('accepted')),
            ),
            PillAction(
              key: const Key('feedback-long'),
              compact: true,
              label: 'Too long',
              onPressed: _busy
                  ? null
                  : () => unawaited(_send('revised', aspect: 'too_long')),
            ),
            PillAction(
              key: const Key('feedback-short'),
              compact: true,
              label: 'Too short',
              onPressed: _busy
                  ? null
                  : () => unawaited(_send('revised', aspect: 'too_short')),
            ),
            PillAction(
              key: const Key('feedback-not-useful'),
              compact: true,
              label: 'Not useful',
              onPressed: _busy ? null : () => unawaited(_send('not_useful')),
            ),
          ],
  );
}
