import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';

/// Where things stand with one person: the rhythm, birthday and open promises
/// and whether suggestions for them are on. Read from
/// `GET /people/{id}/follow-through`.
class FollowThroughData {
  const FollowThroughData({
    required this.hasData,
    required this.cadenceReason,
    required this.cadenceStatus,
    required this.promises,
    required this.reasons,
    required this.mode,
    required this.quiet,
    this.until,
    this.notUsefulCount = 0,
    this.birthday,
  });

  final bool hasData;
  final String cadenceReason;

  /// `due`, `ok` or `none`.
  final String cadenceStatus;
  final List<String> promises;
  final List<String> reasons;

  /// `on`, `snoozed` or `muted`.
  final String mode;
  final bool quiet;
  final DateTime? until;
  final int notUsefulCount;
  final String? birthday;

  static FollowThroughData? fromJson(dynamic data) {
    final json = jsonObject(data);
    if (json == null) return null;
    final cadence = jsonObject(json['cadence']) ?? const {};
    final nudge = jsonObject(json['nudge']) ?? const {};
    final birthday = jsonObject(json['birthday']);
    final days = birthday == null ? null : asJsonInt(birthday['daysUntil'], -1);
    return FollowThroughData(
      hasData: asJsonBool(json['hasData']),
      cadenceReason: jsonString(cadence, 'reason') ?? '',
      cadenceStatus: jsonString(cadence, 'status') ?? 'none',
      promises: [
        for (final promise in jsonMaps(json['promises']))
          '${promise['direction'] == 'i_owe' ? 'You owe' : 'They owe you'}: '
              '${jsonString(promise, 'description') ?? ''}',
      ],
      reasons: jsonStrings(json['reasons']),
      mode: jsonString(nudge, 'mode') ?? 'on',
      quiet: asJsonBool(nudge['quiet']),
      until: jsonDate(nudge['until']),
      notUsefulCount: asJsonInt(nudge['notUsefulCount']),
      birthday: days == null || days < 0 || days > 60
          ? null
          : days == 0
          ? 'Birthday today'
          : 'Birthday in $days day${days == 1 ? '' : 's'}',
    );
  }
}

class FollowThroughCard extends StatefulWidget {
  const FollowThroughCard({
    required this.http,
    required this.personId,
    super.key,
  });

  final Dio http;
  final String personId;

  @override
  State<FollowThroughCard> createState() => _FollowThroughCardState();
}

class _FollowThroughCardState extends State<FollowThroughCard> {
  FollowThroughData? _data;
  bool _busy = false;

  String get _path => '/api/v1/people/${widget.personId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  // A bonus on the person page: without it there is simply no card.
  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('$_path/follow-through');
      if (!mounted) return;
      setState(() => _data = FollowThroughData.fromJson(response.data));
    } on DioException {
      // Keep whatever is shown.
    }
  }

  Future<void> _send(Future<Response<dynamic>> Function() call) async {
    setState(() => _busy = true);
    try {
      final response = await call();
      if (!mounted) return;
      setState(() => _data = FollowThroughData.fromJson(response.data));
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ?? 'That did not work.',
          ),
        ),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _mode(String mode, {int? days}) => _send(
    () => widget.http.put<dynamic>(
      '$_path/nudges',
      data: {'mode': mode, 'snoozeDays': ?days},
    ),
  );

  @override
  Widget build(BuildContext context) {
    final data = _data;
    if (data == null) return const SizedBox.shrink();
    final colors = JarvisColors.of(context);
    return Column(
      key: const Key('person-follow-through'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          'Follow-through',
          trailing: PopupMenuButton<String>(
            key: const Key('follow-through-menu'),
            tooltip: 'Suggestions for this person',
            enabled: !_busy,
            onSelected: (value) => unawaited(switch (value) {
              'week' => _mode('snoozed', days: 7),
              'month' => _mode('snoozed', days: 30),
              'mute' => _mode('muted'),
              _ => _mode('on'),
            }),
            itemBuilder: (_) => [
              if (data.mode != 'on')
                const PopupMenuItem(
                  value: 'on',
                  child: Text('Turn suggestions on'),
                ),
              const PopupMenuItem(
                value: 'week',
                child: Text('Snooze for a week'),
              ),
              const PopupMenuItem(
                value: 'month',
                child: Text('Snooze for a month'),
              ),
              if (data.mode != 'muted')
                const PopupMenuItem(
                  value: 'mute',
                  child: Text('Mute suggestions'),
                ),
            ],
          ),
        ),
        SurfaceCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                data.cadenceReason,
                key: const Key('follow-through-cadence'),
                style: TextStyle(
                  color: data.cadenceStatus == 'due'
                      ? colors.danger
                      : colors.inkSoft,
                  fontSize: 14,
                ),
              ),
              if (data.birthday != null)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(
                    data.birthday!,
                    style: TextStyle(color: colors.inkSoft, fontSize: 14),
                  ),
                ),
              for (final promise in data.promises)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(promise, style: const TextStyle(fontSize: 14)),
                ),
              if (!data.hasData)
                Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(
                    'Set a rhythm or a birthday and Jarvis can help you stay in touch.',
                    style: TextStyle(color: colors.muted, fontSize: 13),
                  ),
                ),
              if (data.quiet)
                Padding(
                  key: const Key('follow-through-quiet'),
                  padding: const EdgeInsets.only(top: 10),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          data.mode == 'muted'
                              ? 'Suggestions are muted.'
                              : 'Suggestions are snoozed'
                                    '${data.until == null ? '' : ' until ${data.until!.day}/${data.until!.month}'}.',
                          style: TextStyle(color: colors.muted, fontSize: 13),
                        ),
                      ),
                      TextButton(
                        key: const Key('follow-through-undo'),
                        onPressed: _busy ? null : () => unawaited(_mode('on')),
                        child: const Text('Turn on'),
                      ),
                    ],
                  ),
                )
              else if (data.reasons.isNotEmpty)
                Align(
                  alignment: Alignment.centerRight,
                  child: TextButton(
                    key: const Key('follow-through-not-useful'),
                    onPressed: _busy
                        ? null
                        : () => unawaited(
                            _send(
                              () => widget.http.post<dynamic>(
                                '$_path/nudges/not-useful',
                              ),
                            ),
                          ),
                    child: const Text('Not useful'),
                  ),
                ),
            ],
          ),
        ),
      ],
    );
  }
}
