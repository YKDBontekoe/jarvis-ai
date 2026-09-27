import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Chooses between the host's ChatGPT (Codex) session and the owner's own OpenRouter key.
class ModelSettingsScreen extends StatefulWidget {
  const ModelSettingsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ModelSettingsScreen> createState() => _ModelSettingsScreenState();
}

class _ModelSettingsScreenState extends State<ModelSettingsScreen> {
  final _key = TextEditingController();
  final _chat = TextEditingController();
  final _fast = TextEditingController();
  final _embedding = TextEditingController();
  String _provider = 'codex';
  bool _keyConfigured = false;
  bool _loading = true;
  bool _saving = false;
  bool _testing = false;
  String? _error;
  String? _notice;
  Map<String, dynamic>? _test;

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
    try {
      final response = await widget.http.get<Map<String, dynamic>>(
        '/api/v1/settings/models',
      );
      if (!mounted) return;
      setState(() {
        _apply(response.data ?? const {});
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load model settings.';
      });
    }
  }

  Future<void> _run(
    Future<Response<Map<String, dynamic>>> Function() request,
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
        _apply(response.data ?? const {}, keyOnly: keyOnly);
        _notice = success;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not save that change.',
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _saveKey() async {
    final value = _key.text.trim();
    if (value.isEmpty) return;
    await _run(
      () => widget.http.put<Map<String, dynamic>>(
        '/api/v1/settings/models/openrouter-key',
        data: {'value': value},
      ),
      'OpenRouter key saved securely.',
      keyOnly: true,
    );
    _key.clear();
  }

  Future<void> _removeKey() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove OpenRouter key?',
      message: 'Jarvis switches back to ChatGPT (Codex) for every request.',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.key,
    );
    if (!confirmed) return;
    await _run(
      () => widget.http.delete<Map<String, dynamic>>(
        '/api/v1/settings/models/openrouter-key',
      ),
      'OpenRouter key removed.',
    );
  }

  Future<void> _save() => _run(
    () => widget.http.put<Map<String, dynamic>>(
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
      final response = await widget.http.post<Map<String, dynamic>>(
        '/api/v1/settings/models/test',
      );
      if (mounted) setState(() => _test = response.data);
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
    } finally {
      if (mounted) setState(() => _testing = false);
    }
  }

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
                      if (_provider == 'openrouter') ...[
                        const SizedBox(height: 16),
                        _keyCard(),
                      ],
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

  Widget _providerCard() => SurfaceCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Provider', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 6),
        const Text(
          'ChatGPT uses the server’s signed-in Codex session. OpenRouter uses your own key and any of its hundreds of models.',
          style: TextStyle(color: JarvisColors.inkSoft, height: 1.4),
        ),
        const SizedBox(height: 14),
        SegmentedButton<String>(
          segments: const [
            ButtonSegment(
              value: 'codex',
              label: Text('ChatGPT (Codex)'),
              icon: Icon(PhosphorIconsRegular.sparkle, size: 16),
            ),
            ButtonSegment(
              value: 'openrouter',
              label: Text('OpenRouter'),
              icon: Icon(PhosphorIconsRegular.shareNetwork, size: 16),
            ),
          ],
          selected: {_provider},
          onSelectionChanged: (value) =>
              setState(() => _provider = value.first),
        ),
      ],
    ),
  );

  Widget _keyCard() => SurfaceCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const IconBadge(icon: PhosphorIconsRegular.key, size: 34),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                'OpenRouter API key',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            StatusPill(
              label: _keyConfigured ? 'Saved' : 'Not set',
              color: _keyConfigured ? JarvisColors.success : JarvisColors.muted,
            ),
          ],
        ),
        const SizedBox(height: 12),
        TextField(
          key: const Key('openrouter-key'),
          controller: _key,
          obscureText: true,
          autocorrect: false,
          enableSuggestions: false,
          decoration: InputDecoration(
            labelText: _keyConfigured ? 'Replace key' : 'sk-or-…',
            helperText:
                'Stored encrypted on your Jarvis server; never shown again.',
          ),
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            FilledButton.tonal(
              onPressed: _saving ? null : () => unawaited(_saveKey()),
              child: const Text('Save key'),
            ),
            const Spacer(),
            if (_keyConfigured)
              TextButton(
                onPressed: _saving ? null : () => unawaited(_removeKey()),
                style: TextButton.styleFrom(
                  foregroundColor: JarvisColors.danger,
                ),
                child: const Text('Remove'),
              ),
          ],
        ),
      ],
    ),
  );

  Widget _modelsCard() {
    final openRouter = _provider == 'openrouter';
    Widget field(
      TextEditingController controller,
      String label,
      String helper, {
      Key? key,
    }) => Padding(
      padding: const EdgeInsets.only(top: 12),
      child: TextField(
        key: key,
        controller: controller,
        autocorrect: false,
        decoration: InputDecoration(
          labelText: label,
          helperText: helper,
          helperMaxLines: 2,
          suffixIcon: openRouter || controller == _embedding
              ? IconButton(
                  tooltip: 'Browse OpenRouter models',
                  icon: const Icon(PhosphorIconsRegular.magnifyingGlass),
                  onPressed: () => unawaited(_pick(controller, label)),
                )
              : null,
        ),
      ),
    );
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Models', style: Theme.of(context).textTheme.titleMedium),
          field(
            _chat,
            'Chat model',
            openRouter
                ? 'Used for conversations and background tasks. Pick one that supports tools.'
                : 'Leave empty to use the ChatGPT account default.',
            key: const Key('chat-model'),
          ),
          if (openRouter)
            field(
              _fast,
              'Fast model (optional)',
              'Used for learning, memory extraction, and reranking. Defaults to the chat model.',
            ),
          field(
            _embedding,
            'Embedding model (optional)',
            'Enables semantic memory search through OpenRouter, e.g. openai/text-embedding-3-small.',
          ),
        ],
      ),
    );
  }

  Widget _testResult() {
    final ok = asJsonBool(_test!['ok']);
    final model = asJsonString(_test!['model']);
    final latency = asJsonInt(_test!['latencyMs']);
    return InlineNotice(
      margin: const EdgeInsets.only(top: 16),
      tone: ok ? NoticeTone.success : NoticeTone.danger,
      message: ok
          ? 'Connected${model == null ? '' : ' to $model'} in $latency ms.'
          : asJsonString(_test!['error']) ?? 'The model did not answer.',
    );
  }
}

/// Searchable OpenRouter catalog with context size, price, and tool support.
class OpenRouterModelPicker extends StatefulWidget {
  const OpenRouterModelPicker({
    required this.http,
    required this.title,
    super.key,
  });

  final Dio http;
  final String title;

  @override
  State<OpenRouterModelPicker> createState() => _OpenRouterModelPickerState();
}

class _OpenRouterModelPickerState extends State<OpenRouterModelPicker> {
  final _search = TextEditingController();
  List<Map<String, dynamic>> _models = const [];
  bool _loading = true;
  String? _error;
  Timer? _debounce;
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final revision = ++_revision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<List<dynamic>>(
        '/api/v1/settings/models/openrouter/catalog',
        queryParameters: {
          if (_search.text.trim().isNotEmpty) 'search': _search.text.trim(),
        },
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _models = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load OpenRouter models.';
      });
    }
  }

  String _price(dynamic value) {
    if (value is! num) return '—';
    return value == 0 ? 'free' : '\$${value.toStringAsFixed(2)}';
  }

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SizedBox(
      height: MediaQuery.of(context).size.height * .8,
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 8),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    widget.title,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                IconButton(
                  tooltip: 'Close',
                  onPressed: () => Navigator.pop(context),
                  icon: const Icon(PhosphorIconsRegular.x),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            child: TextField(
              controller: _search,
              autofocus: true,
              decoration: const InputDecoration(
                prefixIcon: Icon(PhosphorIconsRegular.magnifyingGlass),
                hintText: 'Search models, e.g. claude, gemini, llama',
              ),
              onChanged: (_) {
                _debounce?.cancel();
                _debounce = Timer(
                  const Duration(milliseconds: 300),
                  () => unawaited(_load()),
                );
              },
            ),
          ),
          const SizedBox(height: 8),
          Expanded(
            child: _loading && _models.isEmpty
                ? const LoadingState()
                : _error != null && _models.isEmpty
                ? ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  )
                : ListView.separated(
                    itemCount: _models.length,
                    separatorBuilder: (_, _) => const Divider(indent: 20),
                    itemBuilder: (context, index) {
                      final model = _models[index];
                      final id = asJsonString(model['id']) ?? '';
                      final contextLength = asJsonInt(model['contextLength']);
                      return ListTile(
                        title: Text(asJsonString(model['name']) ?? id),
                        subtitle: Text(
                          '$id · ${contextLength == 0 ? '?' : '${(contextLength / 1000).round()}k'} ctx · '
                          '${_price(model['promptPricePerMillion'])} in / '
                          '${_price(model['completionPricePerMillion'])} out per 1M',
                        ),
                        trailing: asJsonBool(model['supportsTools'])
                            ? const Tooltip(
                                message: 'Supports tools',
                                child: Icon(
                                  PhosphorIconsRegular.puzzlePiece,
                                  size: 18,
                                  color: JarvisColors.success,
                                ),
                              )
                            : null,
                        onTap: () => Navigator.pop(context, id),
                      );
                    },
                  ),
          ),
        ],
      ),
    ),
  );
}
