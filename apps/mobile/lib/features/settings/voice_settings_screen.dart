import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../voice/chat_gpt_voices.dart';

/// ChatGPT voice, hands-free listening, and live captions.
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
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>('/api/v1/settings/voice');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _settings = jsonObject(response.data) ?? const {};
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load voice settings.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load voice settings.';
      });
    }
  }

  String? get _voice => asJsonString(_settings['voice']);

  List<Map<String, dynamic>> get _voices => jsonMaps(_settings['voices']);

  Future<void> _save(Map<String, dynamic> next) async {
    final previous = _settings;
    setState(() {
      _settings = next;
      _error = null;
    });
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/settings/voice',
        data: {
          'handsFree': asJsonBool(next['handsFree'], true),
          'captions': asJsonBool(next['captions'], true),
          'voice':
              asJsonString(next['voice']) ?? asJsonString(next['defaultVoice']),
        },
      );
      if (mounted) {
        setState(() => _settings = jsonObject(response.data) ?? next);
      }
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _settings = previous;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save voice settings.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _settings = previous;
        _error = 'Could not save voice settings.';
      });
    }
  }

  Future<void> _set(String key, bool value) =>
      _save({..._settings, key: value});

  Future<void> _setVoice(String voice) => _save({..._settings, 'voice': voice});

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
                    Text(
                      _voices.isEmpty
                          ? 'Voice uses the ChatGPT voice model in the installed Codex CLI. Its voice list will appear here once that CLI can be reached.'
                          : 'These are the voice-mode voices from the installed Codex CLI. A new session uses the CLI default until you pick another.',
                    ),
                    const SizedBox(height: 12),
                    if (asJsonString(_settings['catalogError']) != null)
                      InlineNotice(
                        message: asJsonString(_settings['catalogError'])!,
                        tone: NoticeTone.warning,
                        margin: const EdgeInsets.only(bottom: 12),
                      ),
                    if (_voices.isNotEmpty)
                      SurfaceCard(
                        padding: const EdgeInsets.symmetric(vertical: 6),
                        child: Column(
                          children: [
                            for (final voice in _voices)
                              ListTile(
                                key: Key('voice-${asJsonString(voice['id'])}'),
                                leading: Icon(
                                  _voice == asJsonString(voice['id'])
                                      ? PhosphorIconsRegular.waveform
                                      : PhosphorIconsRegular.microphone,
                                  color: _voice == asJsonString(voice['id'])
                                      ? JarvisColors.of(context).ink
                                      : JarvisColors.of(context).muted,
                                ),
                                title: Text(
                                  asJsonString(voice['name']) ??
                                      voiceLabel(asJsonString(voice['id'])),
                                ),
                                subtitle: Text(
                                  asJsonBool(voice['isDefault'])
                                      ? 'Codex default'
                                      : 'Installed CLI',
                                ),
                                trailing: _voice == asJsonString(voice['id'])
                                    ? const Icon(PhosphorIconsRegular.check)
                                    : null,
                                onTap: asJsonString(voice['id']) == null
                                    ? null
                                    : () => unawaited(
                                        _setVoice(asJsonString(voice['id'])!),
                                      ),
                              ),
                          ],
                        ),
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
                              'Keep the microphone open and interrupt by speaking',
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
