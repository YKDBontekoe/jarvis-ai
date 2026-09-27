import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Hands-free listening and live captions for realtime voice sessions.
class VoiceSettingsScreen extends StatefulWidget {
  const VoiceSettingsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<VoiceSettingsScreen> createState() => _VoiceSettingsScreenState();
}

class _VoiceSettingsScreenState extends State<VoiceSettingsScreen> {
  Map<String, dynamic> _settings = const {};
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<Map<String, dynamic>>(
        '/api/v1/settings/voice',
      );
      if (!mounted) return;
      setState(() {
        _settings = response.data ?? const {};
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load voice settings.';
      });
    }
  }

  Future<void> _set(String key, bool value) async {
    final next = {..._settings, key: value};
    setState(() => _settings = next);
    try {
      final response = await widget.http.put<Map<String, dynamic>>(
        '/api/v1/settings/voice',
        data: {
          'handsFree': asJsonBool(next['handsFree'], true),
          'captions': asJsonBool(next['captions'], true),
        },
      );
      if (mounted) setState(() => _settings = response.data ?? next);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save voice settings.',
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Voice')),
    body: _loading
        ? const LoadingState()
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
                      'Realtime voice stays in the LiveKit room. These options control how that session feels in the app: keep listening after you pause, and show live captions while you talk.',
                    ),
                    const SizedBox(height: 12),
                    SurfaceCard(
                      padding: const EdgeInsets.symmetric(vertical: 6),
                      child: Column(
                        children: [
                          SwitchListTile(
                            secondary: const Icon(
                              PhosphorIconsRegular.microphone,
                            ),
                            title: const Text('Hands-free'),
                            subtitle: const Text(
                              'Stay listening on this screen and interrupt at any time',
                            ),
                            value: asJsonBool(_settings['handsFree'], true),
                            onChanged: (value) =>
                                unawaited(_set('handsFree', value)),
                          ),
                          SwitchListTile(
                            secondary: const Icon(
                              PhosphorIconsRegular.waveform,
                            ),
                            title: const Text('Captions'),
                            subtitle: const Text(
                              'Show live transcripts while you talk with Jarvis',
                            ),
                            value: asJsonBool(_settings['captions'], true),
                            onChanged: (value) =>
                                unawaited(_set('captions', value)),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
  );
}
