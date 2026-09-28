import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

part 'openrouter_model_picker.dart';
part 'codex_model_picker.dart';
part 'model_settings_cards.dart';

/// Chooses between the host's ChatGPT (Codex) session and the owner's own OpenRouter key.
class ModelSettingsScreen extends StatefulWidget {
  const ModelSettingsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ModelSettingsScreen> createState() => _ModelSettingsScreenState();
}

/// Holds model-settings fields so the cards mixin can share state.
abstract class _ModelSettingsController extends State<ModelSettingsScreen> {
  final _key = TextEditingController();
  final _chat = TextEditingController();
  final _fast = TextEditingController();
  final _embedding = TextEditingController();
  String _provider = 'codex';
  bool _keyConfigured = false;
  bool _loading = true;
  bool _saving = false;
  bool _testing = false;
  bool _codexLoading = true;
  bool _updating = false;
  bool _updateAvailable = false;
  bool _canUpdate = false;
  String? _installedVersion;
  String? _latestVersion;
  String? _updateBlocked;
  String? _codexError;
  List<Map<String, dynamic>> _codexModels = const [];
  String? _error;
  String? _notice;
  Map<String, dynamic>? _test;
  int _requestRevision = 0;

  Future<void> _updateCodex();
  Future<void> _saveKey();
  Future<void> _removeKey();
  Future<void> _pickCodex();
  Future<void> _pick(TextEditingController controller, String title);
  List<Map<String, dynamic>> get _visibleCodexModels;
}

class _ModelSettingsScreenState extends _ModelSettingsController
    with _ModelSettingsCards {
  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _key.dispose();
    _chat.dispose();
    _fast.dispose();
    _embedding.dispose();
    super.dispose();
  }

  void _apply(Map<String, dynamic> data, {bool keyOnly = false}) {
    _keyConfigured = asJsonBool(data['openRouterKeyConfigured']);
    if (keyOnly) return;
    _provider = asJsonString(data['provider']) ?? 'codex';
    _chat.text = asJsonString(data['chatModel']) ?? '';
    _fast.text = asJsonString(data['fastModel']) ?? '';
    _embedding.text = asJsonString(data['embeddingModel']) ?? '';
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/settings/models',
      );
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _apply(jsonObject(response.data) ?? const {});
        _loading = false;
        _error = null;
      });
      await _loadCodex(revision);
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _codexLoading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load model settings.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _codexLoading = false;
        _error = 'Could not load model settings.';
      });
    }
  }

  void _applyCodex(Map<String, dynamic> data) {
    _installedVersion = asJsonString(data['installedVersion']);
    _latestVersion = asJsonString(data['latestVersion']);
    _updateAvailable = asJsonBool(data['updateAvailable']);
    _canUpdate = asJsonBool(data['canUpdate']);
    _updateBlocked = asJsonString(data['updateBlockedReason']);
    _codexError = asJsonString(data['error']);
    _codexModels = jsonMaps(data['models']);
  }

  Future<void> _loadCodex([int? revision]) async {
    final expected = revision ?? _requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/settings/models/codex',
      );
      if (!mounted || expected != _requestRevision) return;
      final data = jsonObject(response.data);
      if (data == null) {
        setState(() {
          _codexLoading = false;
          _codexError =
              'Could not load the models supported by the installed Codex CLI.';
        });
        return;
      }
      setState(() {
        _applyCodex(data);
        _codexLoading = false;
      });
    } on DioException catch (error) {
      if (!mounted || expected != _requestRevision) return;
      setState(() {
        _codexLoading = false;
        _codexError =
            firstProblemMessage(error.response?.data) ??
            'Could not load the models supported by the installed Codex CLI.';
      });
    } catch (_) {
      if (!mounted || expected != _requestRevision) return;
      setState(() {
        _codexLoading = false;
        _codexError =
            'Could not load the models supported by the installed Codex CLI.';
      });
    }
  }

  @override
  Future<void> _updateCodex() async {
    setState(() {
      _updating = true;
      _error = null;
      _notice = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/settings/models/codex/update',
      );
      if (!mounted) return;
      final data = jsonObject(response.data) ?? const {};
      setState(() {
        _applyCodex(data);
        _notice =
            'Codex ${asJsonString(data['installedVersion']) ?? 'CLI'} is installed. New replies use it right away.';
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not update Codex.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Jarvis could not update Codex.');
    } finally {
      if (mounted) setState(() => _updating = false);
    }
  }

  @override
  List<Map<String, dynamic>> get _visibleCodexModels {
    final selected = _chat.text.trim();
    return [
      for (final model in _codexModels)
        if (!asJsonBool(model['hidden']) ||
            (asJsonString(model['model']) ?? asJsonString(model['id'])) ==
                selected)
          model,
    ];
  }

  @override
  Future<void> _pickCodex() async {
    final picked = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      builder: (_) => CodexModelPicker(
        models: _visibleCodexModels,
        selected: _chat.text.trim(),
      ),
    );
    if (picked != null && mounted) setState(() => _chat.text = picked);
  }

  Future<void> _run(
    Future<Response<dynamic>> Function() request,
    String success, {
    bool keyOnly = false,
  }) async {
    setState(() {
      _saving = true;
      _error = null;
      _notice = null;
    });
    try {
      final response = await request();
      if (!mounted) return;
      setState(() {
        _apply(jsonObject(response.data) ?? const {}, keyOnly: keyOnly);
        _notice = success;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not save that change.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Jarvis could not save that change.');
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Future<void> _saveKey() async {
    final value = _key.text.trim();
    if (value.isEmpty) return;
    await _run(
      () => widget.http.put<dynamic>(
        '/api/v1/settings/models/openrouter-key',
        data: {'value': value},
      ),
      'OpenRouter key saved securely.',
      keyOnly: true,
    );
    _key.clear();
  }

  @override
  Future<void> _removeKey() async {
    final embeddingOnly = _provider == 'codex';
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove OpenRouter key?',
      message: embeddingOnly
          ? 'Semantic memory search stops until you add a key and embedding model again.'
          : 'Jarvis switches back to ChatGPT (Codex) for chat and clears any OpenRouter embedding model.',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.key,
    );
    if (!confirmed) return;
    await _run(
      () =>
          widget.http.delete<dynamic>('/api/v1/settings/models/openrouter-key'),
      'OpenRouter key removed.',
    );
  }

  Future<void> _save() => _run(
    () => widget.http.put<dynamic>(
      '/api/v1/settings/models',
      data: {
        'provider': _provider,
        'chatModel': _chat.text.trim(),
        'fastModel': _fast.text.trim(),
        'embeddingModel': _embedding.text.trim(),
      },
    ),
    'Model settings saved. New replies use them right away.',
  );

  Future<void> _testConnection() async {
    setState(() {
      _testing = true;
      _test = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/settings/models/test',
      );
      if (mounted) setState(() => _test = jsonObject(response.data));
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _test = {
            'ok': false,
            'error':
                firstProblemMessage(error.response?.data) ??
                'The test request failed.',
          },
        );
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => _test = {'ok': false, 'error': 'The test request failed.'},
        );
      }
    } finally {
      if (mounted) setState(() => _testing = false);
    }
  }

  @override
  Future<void> _pick(TextEditingController controller, String title) async {
    final picked = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      builder: (_) => OpenRouterModelPicker(http: widget.http, title: title),
    );
    if (picked != null && mounted) setState(() => controller.text = picked);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Models')),
    body: _loading
        ? const LoadingState()
        : SafeArea(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _providerCard(),
                      const SizedBox(height: 16),
                      _codexCard(),
                      const SizedBox(height: 16),
                      _keyCard(embeddingOnly: _provider == 'codex'),
                      const SizedBox(height: 16),
                      _modelsCard(),
                      if (_error != null)
                        InlineNotice(
                          message: _error!,
                          tone: NoticeTone.danger,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      if (_notice != null)
                        InlineNotice(
                          message: _notice!,
                          tone: NoticeTone.success,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      const SizedBox(height: 16),
                      Row(
                        children: [
                          Expanded(
                            child: FilledButton.icon(
                              key: const Key('save-models'),
                              onPressed: _saving
                                  ? null
                                  : () => unawaited(_save()),
                              icon: const Icon(
                                PhosphorIconsRegular.check,
                                size: 18,
                              ),
                              label: const Text('Save'),
                            ),
                          ),
                          const SizedBox(width: 12),
                          Expanded(
                            child: OutlinedButton.icon(
                              key: const Key('test-models'),
                              onPressed: _testing
                                  ? null
                                  : () => unawaited(_testConnection()),
                              icon: _testing
                                  ? const SizedBox.square(
                                      dimension: 16,
                                      child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                      ),
                                    )
                                  : const Icon(
                                      PhosphorIconsRegular.lightning,
                                      size: 18,
                                    ),
                              label: const Text('Test'),
                            ),
                          ),
                        ],
                      ),
                      if (_test != null) _testResult(),
                    ],
                  ),
                ),
              ],
            ),
          ),
  );
}
