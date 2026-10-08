import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'device_telemetry.dart';

/// Toggles which capabilities this phone or computer will honor as a device node.
class DevicesScreen extends StatefulWidget {
  const DevicesScreen({required this.http, super.key});

  final Dio http;

  @override
  State<DevicesScreen> createState() => _DevicesScreenState();
}

class _DevicesScreenState extends State<DevicesScreen> {
  Map<String, dynamic> _settings = const {};
  List<Map<String, dynamic>> _online = const [];
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/settings/devices',
      );
      if (!mounted || revision != _requestRevision) return;
      final data = jsonObject(response.data) ?? const {};
      setState(() {
        _settings = data;
        _online = jsonMaps(data['online']);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load device settings.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load device settings.';
      });
    }
  }

  Future<void> _set(String key, bool value) async {
    final previous = _settings;
    final next = {..._settings, key: value};
    setState(() {
      _settings = next;
      _error = null;
    });
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/settings/devices',
        data: {
          'location': asJsonBool(next['location']),
          'battery': asJsonBool(next['battery'], true),
          'clipboard': asJsonBool(next['clipboard']),
          'openUrl': asJsonBool(next['openUrl'], true),
          'notify': asJsonBool(next['notify'], true),
        },
      );
      if (!mounted) return;
      final data = jsonObject(response.data) ?? next;
      setState(() {
        _settings = data;
        _online = jsonMaps(data['online']);
      });
      if (asJsonBool(data['location']) || asJsonBool(data['battery'], true)) {
        unawaited(postDeviceTelemetry(widget.http));
      }
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _settings = previous;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save device settings.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _settings = previous;
        _error = 'Could not save device settings.';
      });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('This device')),
    body: _loading
        ? const SkeletonList()
        : ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            children: [
              if (_error != null)
                InlineNotice(
                  message: _error!,
                  tone: NoticeTone.danger,
                  margin: const EdgeInsets.only(bottom: 12),
                ),
              ContentWidth(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text(
                      'Jarvis can use this phone or computer as a node: location, clipboard, opening links, and local notifications. Turn off anything you do not want it to request.',
                    ),
                    const SizedBox(height: 12),
                    SurfaceCard(
                      padding: const EdgeInsets.symmetric(vertical: 6),
                      child: Column(
                        children: [
                          _toggle(
                            'Location',
                            'Share this device’s location when asked',
                            PhosphorIconsRegular.mapPin,
                            'location',
                          ),
                          _toggle(
                            'Battery',
                            'Report charge level',
                            PhosphorIconsRegular.batteryFull,
                            'battery',
                          ),
                          _toggle(
                            'Clipboard',
                            'Read text you copied, only after you approve',
                            PhosphorIconsRegular.clipboardText,
                            'clipboard',
                          ),
                          _toggle(
                            'Open links',
                            'Open a URL in the default browser',
                            PhosphorIconsRegular.globe,
                            'openUrl',
                          ),
                          _toggle(
                            'Notifications',
                            'Show a local alert on this device',
                            PhosphorIconsRegular.bell,
                            'notify',
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 20),
                    Text(
                      'ONLINE NOW',
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        color: JarvisColors.of(context).muted,
                        letterSpacing: .8,
                      ),
                    ),
                    const SizedBox(height: 8),
                    if (_online.isEmpty)
                      const Text('Open Jarvis on a device to bring it online.')
                    else
                      for (final device in _online)
                        ListTile(
                          leading: const Icon(
                            PhosphorIconsRegular.deviceMobile,
                          ),
                          title: Text(asJsonString(device['name']) ?? 'Device'),
                          subtitle: Text(
                            jsonStrings(device['capabilities']).join(', '),
                          ),
                        ),
                  ],
                ),
              ),
            ],
          ),
  );

  Widget _toggle(String title, String subtitle, IconData icon, String key) =>
      SwitchListTile(
        secondary: Icon(icon),
        title: Text(title),
        subtitle: Text(subtitle),
        value: asJsonBool(
          _settings[key],
          key != 'location' && key != 'clipboard',
        ),
        onChanged: (value) => unawaited(_set(key, value)),
      );
}
