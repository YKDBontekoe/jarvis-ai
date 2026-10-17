import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What buzzes your phone, by kind of notification, and what the current mode
/// does about it. Everything stays in the app whether or not it buzzed.
class NotificationSettingsScreen extends StatefulWidget {
  const NotificationSettingsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<NotificationSettingsScreen> createState() =>
      _NotificationSettingsScreenState();
}

class _NotificationSettingsScreenState
    extends State<NotificationSettingsScreen> {
  Map<String, dynamic>? _view;
  bool _loading = true;
  String? _error;
  final Set<String> _busy = {};

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/notifications/preferences',
      );
      if (!mounted) return;
      setState(() {
        _view = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your notification settings.';
      });
    }
  }

  Future<void> _set(String category, bool on) async {
    setState(() => _busy.add(category));
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/notifications/preferences/$category',
        data: {'push': on ? 'on' : 'quiet'},
      );
      if (mounted) setState(() => _view = jsonObject(response.data) ?? _view);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ?? 'That did not work.',
            ),
          ),
        );
    } finally {
      if (mounted) setState(() => _busy.remove(category));
    }
  }

  static const _icons = {
    'approvals': PhosphorIconsRegular.shieldCheck,
    'reminders': PhosphorIconsRegular.alarm,
    'problems': PhosphorIconsRegular.warningCircle,
    'briefings': PhosphorIconsRegular.sunHorizon,
    'updates': PhosphorIconsRegular.sparkle,
  };

  @override
  Widget build(BuildContext context) {
    final view = _view ?? const <String, dynamic>{};
    final categories = jsonMaps(view['categories']);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Notifications')),
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
                        child: KeyedSubtree(
                          key: const Key('notif-summary'),
                          child: HeroCard(
                            label:
                                '${asJsonString(view['currentModeLabel']) ?? ''} mode',
                            title: asJsonString(view['summary']) ?? '',
                            subtitle: 'Everything stays in the app, whether or not it buzzes your phone.',
                          ),
                        ),
                      ),
                      const GroupLabel('Buzz my phone for'),
                      for (var i = 0; i < categories.length; i++)
                        _category(categories[i], i),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _category(Map<String, dynamic> category, int index) {
    final id = asJsonString(category['id']) ?? '';
    final locked = category['locked'] == true;
    final on = asJsonString(category['setting']) != 'quiet';
    final now = jsonObject(category['now']) ?? const {};
    final outcome = asJsonString(now['outcome']);
    final heldIn = [
      for (final mode in jsonMaps(category['modes']))
        if (mode['outcome'] == 'held') asJsonString(mode['label']) ?? '',
    ];
    final colors = JarvisColors.of(context);
    return FeatureRow(
      key: Key('notif-$id'),
      index: index,
      icon: _icons[id] ?? PhosphorIconsRegular.bell,
      caption: switch (outcome) {
        'held' => 'Held right now',
        'app_only' => 'In the app only',
        _ => null,
      },
      title: asJsonString(category['title']) ?? '',
      subtitle: asJsonString(category['description']),
      trailing: locked
          ? const Icon(
              PhosphorIconsRegular.lockSimple,
              size: 20,
              key: Key('notif-locked'),
            )
          : Switch(
              key: Key('notif-switch-$id'),
              value: on,
              onChanged: _busy.contains(id)
                  ? null
                  : (value) => unawaited(_set(id, value)),
            ),
      extra: heldIn.isEmpty
          ? null
          : Text(
              'Held in ${heldIn.join(', ')}.',
              key: Key('notif-modes-$id'),
              style: TextStyle(
                fontFamily: 'Geist',
                fontSize: 13,
                color: colors.muted,
              ),
            ),
    );
  }
}
