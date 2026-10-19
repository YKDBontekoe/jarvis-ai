import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'routine_screen.dart';

IconData routineIcon(String id) => switch (id) {
  'morning' => PhosphorIconsRegular.sunHorizon,
  'focus' => PhosphorIconsRegular.target,
  'leave' => PhosphorIconsRegular.key,
  'evening' => PhosphorIconsRegular.moon,
  _ => PhosphorIconsRegular.house,
};

/// Routines for the home: the four templates and the devices a source reports.
/// For now the devices are examples, so every run is a simulation; the screen
/// says so before anything else.
class HomeRoutinesScreen extends StatefulWidget {
  const HomeRoutinesScreen({required this.http, super.key});

  final Dio http;

  @override
  State<HomeRoutinesScreen> createState() => _HomeRoutinesScreenState();
}

class _HomeRoutinesScreenState extends State<HomeRoutinesScreen> {
  Map<String, dynamic>? _overview;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/home-routines');
      if (!mounted) return;
      setState(() {
        _overview = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load routines.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final routines = jsonMaps(_overview?['routines']);
    final devices = jsonMaps(_overview?['devices']);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Home routines')),
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
                      HeroCard(
                        key: const Key('routines-hero'),
                        label: 'EXAMPLE DEVICES',
                        title: 'Preview before anything runs',
                        subtitle:
                            asJsonString(_overview?['notice']) ??
                            'Nothing here changes a real home.',
                      ),
                      const GroupLabel('Routines'),
                      for (final (i, routine) in routines.indexed)
                        FeatureRow(
                          key: Key('routine-${routine['id']}'),
                          index: i,
                          icon: routineIcon(asJsonString(routine['id']) ?? ''),
                          title: asJsonString(routine['title']) ?? '',
                          subtitle: asJsonString(routine['description']),
                          caption:
                              '${jsonMaps(routine['steps']).length} actions',
                          onTap: () => unawaited(
                            Navigator.of(context).push<void>(
                              MaterialPageRoute(
                                builder: (_) => RoutineScreen(
                                  http: widget.http,
                                  routineId: routine['id'] as String,
                                ),
                              ),
                            ),
                          ),
                        ),
                      const GroupLabel('Devices'),
                      for (final (i, device) in devices.indexed)
                        FeatureRow(
                          key: Key('device-${device['id']}'),
                          index: i,
                          icon: PhosphorIconsRegular.lightbulb,
                          quiet: !asJsonBool(device['present']),
                          caption: asJsonString(device['room']),
                          title: asJsonString(device['name']) ?? '',
                          subtitle: asJsonBool(device['present'])
                              ? asJsonString(device['reading'])
                              : 'Not reachable',
                          trailing: StatusPill(
                            label: 'Example',
                            color: JarvisColors.of(context).muted,
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
