part of 'model_settings_screen.dart';

mixin _ModelSettingsCards on _ModelSettingsController {
  Widget _providerCard() => SurfaceCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Provider', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 6),
        const Text(
          'ChatGPT uses the server’s signed-in Codex session for conversations. '
          'OpenRouter uses your own key for chat—or only for the optional embedding model while chat stays on Codex.',
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

  Widget _codexCard() {
    final installed = _installedVersion ?? 'unknown';
    final latest = _latestVersion;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.cpu, size: 34),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  'Codex CLI',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              if (!_codexLoading)
                StatusPill(
                  label: _updateAvailable
                      ? 'Update available'
                      : _canUpdate && latest != null
                      ? 'Up to date'
                      : 'Installed',
                  color: _updateAvailable
                      ? JarvisColors.warning
                      : _canUpdate && latest != null
                      ? JarvisColors.success
                      : JarvisColors.muted,
                ),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            latest == null
                ? 'Installed $installed.'
                : 'Installed $installed. Latest release is $latest.',
            style: const TextStyle(color: JarvisColors.inkSoft, height: 1.4),
          ),
          if (_codexLoading)
            const Padding(
              padding: EdgeInsets.only(top: 12),
              child: LinearProgressIndicator(),
            ),
          if (_updateAvailable)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: Align(
                alignment: Alignment.centerLeft,
                child: FilledButton.tonalIcon(
                  key: const Key('update-codex'),
                  onPressed: _updating || _saving
                      ? null
                      : () => unawaited(_updateCodex()),
                  icon: _updating
                      ? const SizedBox.square(
                          dimension: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(
                          PhosphorIconsRegular.arrowsClockwise,
                          size: 18,
                        ),
                  label: Text('Update to $latest'),
                ),
              ),
            ),
          if (_updateBlocked != null && !_canUpdate)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                _updateBlocked!,
                style: const TextStyle(
                  color: JarvisColors.inkSoft,
                  height: 1.4,
                ),
              ),
            ),
          if (_codexError != null)
            InlineNotice(
              message: _codexError!,
              tone: NoticeTone.danger,
              margin: const EdgeInsets.only(top: 12),
            ),
        ],
      ),
    );
  }

  Widget _keyCard({required bool embeddingOnly}) => SurfaceCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const IconBadge(icon: PhosphorIconsRegular.key, size: 34),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                embeddingOnly
                    ? 'OpenRouter API key (embeddings)'
                    : 'OpenRouter API key',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            StatusPill(
              label: _keyConfigured ? 'Saved' : 'Not set',
              color: _keyConfigured ? JarvisColors.success : JarvisColors.muted,
            ),
          ],
        ),
        if (embeddingOnly) ...[
          const SizedBox(height: 6),
          const Text(
            'Required only when you pick an embedding model below. Chat stays on Codex.',
            style: TextStyle(color: JarvisColors.inkSoft, height: 1.4),
          ),
        ],
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
    final codexPicker = !openRouter && _visibleCodexModels.isNotEmpty;
    Widget field(
      TextEditingController controller,
      String label,
      String helper, {
      Key? key,
      bool codexModels = false,
      String? browseCodexKey,
    }) => Padding(
      padding: const EdgeInsets.only(top: 12),
      child: TextField(
        key: key,
        controller: controller,
        readOnly: codexModels,
        autocorrect: false,
        decoration: InputDecoration(
          labelText: label,
          hintText: codexModels && controller.text.trim().isEmpty
              ? 'Account default'
              : null,
          helperText: helper,
          helperMaxLines: 3,
          suffixIcon: codexModels
              ? IconButton(
                  key: Key(browseCodexKey ?? 'browse-codex-models'),
                  tooltip: 'Browse Codex models',
                  icon: const Icon(PhosphorIconsRegular.magnifyingGlass),
                  onPressed: () => unawaited(_pickCodex(controller)),
                )
              : openRouter || controller == _embedding
              ? IconButton(
                  tooltip: 'Browse OpenRouter models',
                  icon: const Icon(PhosphorIconsRegular.magnifyingGlass),
                  onPressed: () => unawaited(_pick(controller, label)),
                )
              : null,
        ),
      ),
    );
    String codexFieldHelper(String selected) {
      final known = _codexModels.any(
        (model) =>
            (asJsonString(model['model']) ?? asJsonString(model['id'])) ==
            selected,
      );
      if (_codexModels.isEmpty) {
        return 'Leave empty to use the ChatGPT account default.';
      }
      return 'Models supported by Codex ${_installedVersion ?? 'the installed CLI'}. '
          'Leave empty for the account default.'
          '${selected.isNotEmpty && !known ? ' This model is not in the installed catalog.' : ''}';
    }

    final chatSelected = _chat.text.trim();
    final reasoningSelected = _reasoning.text.trim();
    final codexChatHelper = codexFieldHelper(chatSelected);
    final codexReasoningHelper =
        'Used for reflection, dreaming, and durable background tasks. '
        '${codexFieldHelper(reasoningSelected)}';
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
                : codexChatHelper,
            key: const Key('chat-model'),
            codexModels: codexPicker,
          ),
          if (openRouter)
            field(
              _fast,
              'Fast model (optional)',
              'Used for memory extraction and reranking. Defaults to the chat model.',
            ),
          field(
            _reasoning,
            'Reasoning model (optional)',
            openRouter
                ? 'Used for reflection, dreaming, and durable background tasks. Defaults to the chat model.'
                : codexReasoningHelper,
            key: const Key('reasoning-model'),
            codexModels: codexPicker,
            browseCodexKey: 'browse-codex-reasoning-models',
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
    final result = _test;
    if (result == null) return const SizedBox.shrink();
    final ok = asJsonBool(result['ok']);
    final model = asJsonString(result['model']);
    final latency = asJsonInt(result['latencyMs']);
    return InlineNotice(
      margin: const EdgeInsets.only(top: 16),
      tone: ok ? NoticeTone.success : NoticeTone.danger,
      message: ok
          ? 'Connected${model == null ? '' : ' to $model'} in $latency ms.'
          : asJsonString(result['error']) ?? 'The model did not answer.',
    );
  }
}
