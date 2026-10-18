import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Your home time zone: where your day starts and ends. A phone's zone is only
/// offered, never applied on its own, and changing it moves nothing that is
/// already scheduled.
class HomeZoneScreen extends StatefulWidget {
  const HomeZoneScreen({required this.http, super.key});

  final Dio http;

  @override
  State<HomeZoneScreen> createState() => _HomeZoneScreenState();
}

class _HomeZoneScreenState extends State<HomeZoneScreen> {
  Map<String, dynamic>? _view;
  String? _device;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  static const _common = [
    ('Europe/Amsterdam', 'Amsterdam'),
    ('Europe/London', 'London'),
    ('Europe/Berlin', 'Berlin'),
    ('Europe/Paris', 'Paris'),
    ('Europe/Lisbon', 'Lisbon'),
    ('America/New_York', 'New York'),
    ('America/Los_Angeles', 'Los Angeles'),
    ('Asia/Tokyo', 'Tokyo'),
    ('Australia/Sydney', 'Sydney'),
    ('UTC', 'UTC'),
  ];

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/preferences');
      final device = await deviceTimeZoneLookup();
      if (!mounted) return;
      setState(() {
        _view = jsonObject(response.data);
        _device = device;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your preferences.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _choose(String id) async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/preferences/home-zone/preview',
        data: {'timeZoneId': id},
      );
      final preview = jsonObject(response.data);
      if (!mounted || preview == null) return;
      final confirmed = await showModalBottomSheet<bool>(
        context: context,
        showDragHandle: true,
        isScrollControlled: true,
        builder: (_) => _PreviewSheet(preview: preview),
      );
      if (confirmed != true || !mounted) return;
      await _save(id);
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _save(String? id) async {
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/preferences/home-zone',
        data: {'timeZoneId': id},
      );
      if (mounted) setState(() => _view = jsonObject(response.data) ?? _view);
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'Could not save it.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final view = _view ?? const <String, dynamic>{};
    final current = asJsonString(view['homeTimeZone']) ?? 'UTC';
    final chosen = view['chosen'] == true;
    final offerDevice = _device != null && _device != current;
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Time zone')),
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
                          key: const Key('zone-summary'),
                          child: HeroCard(
                            label: chosen
                                ? 'Your home time zone'
                                : 'Not chosen yet',
                            title: current.replaceAll('_', ' '),
                            subtitle:
                                asJsonString(view['explanation']) ??
                                'Your day starts and ends here.',
                            footer: HeroNote(
                              'It is ${asJsonString(view['localNow']) ?? ''} there.',
                            ),
                          ),
                        ),
                      ),
                      if (offerDevice) ...[
                        const GroupLabel('This phone'),
                        FeatureRow(
                          key: const Key('zone-device'),
                          icon: PhosphorIconsRegular.deviceMobile,
                          title: _device!.replaceAll('_', ' '),
                          subtitle: 'Your phone says this. Jarvis only changes your home zone when you say so.',
                          actions: [
                            PillAction(
                              key: const Key('zone-use-device'),
                              compact: true,
                              primary: true,
                              label: 'Use it',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_choose(_device!)),
                            ),
                          ],
                        ),
                      ],
                      const GroupLabel('Choose'),
                      for (final (i, (id, name)) in _common.indexed)
                        FeatureRow(
                          key: Key('zone-$id'),
                          index: i,
                          icon: id == current
                              ? PhosphorIconsRegular.checkCircle
                              : PhosphorIconsRegular.globeSimple,
                          title: name,
                          subtitle: id,
                          quiet: false,
                          onTap: _busy || id == current
                              ? null
                              : () => unawaited(_choose(id)),
                        ),
                      if (chosen) ...[
                        const SizedBox(height: 8),
                        Align(
                          alignment: Alignment.centerLeft,
                          child: PillAction(
                            key: const Key('zone-clear'),
                            label: 'Go back to automatic',
                            onPressed: _busy
                                ? null
                                : () => unawaited(_save(null)),
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}

class _PreviewSheet extends StatelessWidget {
  const _PreviewSheet({required this.preview});

  final Map<String, dynamic> preview;

  @override
  Widget build(BuildContext context) {
    final effects = jsonMaps(preview['effects']);
    return SafeArea(
      child: SingleChildScrollView(
        key: const Key('zone-preview'),
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Switch to ${(asJsonString(preview['proposed']) ?? '').replaceAll('_', ' ')}?',
              style: JarvisType.displayOf(context).copyWith(fontSize: 26),
            ),
            const SizedBox(height: 4),
            Text(
              'It is ${asJsonString(preview['nowThere']) ?? ''} there right now.',
            ),
            const SizedBox(height: 10),
            for (final effect in effects)
              FeatureRow(
                icon: PhosphorIconsRegular.info,
                caption: asJsonString(effect['area']),
                title: asJsonString(effect['text']) ?? '',
              ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: [
                PillAction(
                  key: const Key('zone-confirm'),
                  primary: true,
                  label: 'Switch',
                  onPressed: () => Navigator.pop(context, true),
                ),
                PillAction(
                  label: 'Not now',
                  onPressed: () => Navigator.pop(context, false),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
