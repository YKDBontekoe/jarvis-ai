part of 'integrations_screen.dart';

// ignore_for_file: annotate_overrides

mixin _IntegrationsCredentials on _IntegrationsController {
  Future<void> _editSecret({String? provider, String? secretName}) async {
    final replacing =
        secretName != null &&
        (_providers.any(
              (item) =>
                  item['provider'] == provider &&
                  jsonStrings(item['secretNames']).contains(secretName),
            ) ||
            (secretName == 'token' &&
                _managedServers.any(
                  (server) =>
                      server['id'] == provider && server['hasToken'] == true,
                )));
    final providerController = TextEditingController(text: provider ?? '');
    final nameController = TextEditingController(text: secretName ?? '');
    final valueController = TextEditingController();
    var obscure = true;
    var saving = false;
    String? dialogError;
    final saved = await showJarvisDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(replacing ? 'Replace access key' : 'Paste an access key'),
          content: SizedBox(
            width: 420,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (provider == null)
                  TextField(
                    controller: providerController,
                    autocorrect: false,
                    textCapitalization: TextCapitalization.none,
                    decoration: const InputDecoration(
                      labelText: 'App ID',
                      hintText: 'home-assistant',
                      helperText:
                          'The name your Jarvis server uses for this app',
                    ),
                  ),
                if (secretName == null)
                  TextField(
                    controller: nameController,
                    autocorrect: false,
                    textCapitalization: TextCapitalization.none,
                    decoration: const InputDecoration(
                      labelText: 'Key name',
                      hintText: 'token',
                    ),
                  ),
                TextField(
                  controller: valueController,
                  obscureText: obscure,
                  autocorrect: false,
                  enableSuggestions: false,
                  decoration: InputDecoration(
                    labelText: 'Access key',
                    helperText:
                        'Stored encrypted. Jarvis never shows it again.',
                    suffixIcon: IconButton(
                      tooltip: obscure ? 'Show value' : 'Hide value',
                      onPressed: () => setDialogState(() => obscure = !obscure),
                      icon: Icon(
                        obscure
                            ? PhosphorIconsRegular.eye
                            : PhosphorIconsRegular.eyeSlash,
                      ),
                    ),
                  ),
                ),
                if (dialogError != null) ...[
                  const SizedBox(height: 10),
                  Text(
                    dialogError!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ],
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: saving
                  ? null
                  : () => Navigator.pop(dialogContext, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: saving
                  ? null
                  : () async {
                      final providerValue =
                          (provider ?? providerController.text).trim();
                      final nameValue = (secretName ?? nameController.text)
                          .trim();
                      final value = valueController.text;
                      if (providerValue.isEmpty ||
                          nameValue.isEmpty ||
                          value.trim().isEmpty) {
                        setDialogState(
                          () => dialogError = 'Fill in every field.',
                        );
                        return;
                      }
                      setDialogState(() => saving = true);
                      try {
                        await widget.http.put<void>(
                          '/api/v1/integrations/${Uri.encodeComponent(providerValue)}/credentials/${Uri.encodeComponent(nameValue)}',
                          data: {'value': value},
                        );
                        if (dialogContext.mounted) {
                          Navigator.pop(dialogContext, true);
                        }
                      } on DioException catch (error) {
                        final message =
                            firstProblemMessage(error.response?.data) ??
                            'Could not save the key.';
                        if (dialogContext.mounted) {
                          setDialogState(() {
                            dialogError = message;
                            saving = false;
                          });
                        }
                      } catch (_) {
                        if (dialogContext.mounted) {
                          setDialogState(() {
                            dialogError = 'Could not save the key.';
                            saving = false;
                          });
                        }
                      } finally {
                        if (dialogContext.mounted) {
                          setDialogState(() => saving = false);
                        }
                      }
                    },
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
    providerController.dispose();
    nameController.dispose();
    valueController.dispose();
    if (saved == true && mounted) await _load();
  }

  Future<void> _deleteSecret(String provider, String secretName) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this key?',
      message: 'Jarvis can’t use ${_appInfo(provider).name} with it anymore.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.key,
    );
    if (!confirmed) return;
    if (!mounted) return;
    try {
      await widget.http.delete<void>(
        '/api/v1/integrations/${Uri.encodeComponent(provider)}/credentials/${Uri.encodeComponent(secretName)}',
      );
      if (mounted) await _load();
    } on DioException {
      if (mounted) setState(() => _error = 'Could not delete the key.');
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not delete the key.');
    }
  }

  Future<void> _deleteProvider(String provider) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete all keys for ${_appInfo(provider).name}?',
      message: 'Jarvis can’t sign in to it anymore until you add a key again.',
      confirmLabel: 'Delete all',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed) return;
    if (!mounted) return;
    try {
      await widget.http.delete<void>(
        '/api/v1/integrations/${Uri.encodeComponent(provider)}/credentials',
      );
      if (mounted) await _load();
    } on DioException {
      if (mounted) {
        setState(() => _error = 'Could not delete those keys.');
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Could not delete those keys.');
      }
    }
  }

  bool _hasKey(String provider) =>
      !_credentialsFailed &&
      _providers.any(
        (item) =>
            item['provider'] == provider &&
            jsonStrings(item['secretNames']).contains('token'),
      );

  /// An app the Jarvis host can run once the owner stores an access key.
  @override
  Widget _featuredRow({required String provider, required String steps}) {
    final info = _appInfo(provider);
    final added = _hasKey(provider);
    void open() => _showFeaturedSheet(info, provider, steps, added);
    return _SuggestionRow(
      key: Key('suggest-$provider'),
      icon: info.icon,
      title: info.name,
      subtitle: info.what ?? '',
      trailing: added ? const _AddedLabel() : _SetUpButton(onPressed: open),
      onTap: open,
    );
  }

  Future<void> _showFeaturedSheet(
    _AppInfo info,
    String provider,
    String steps,
    bool added,
  ) async {
    final ask = widget.onAskInChat;
    final choice = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (sheetContext) {
        final theme = Theme.of(sheetContext);
        final colors = JarvisColors.of(sheetContext);
        return SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(24, 0, 24, 20),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    IconBadge(icon: info.icon, size: 44),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Text(info.name, style: theme.textTheme.titleLarge),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                Text(
                  info.what ?? '',
                  style: TextStyle(
                    fontSize: 15,
                    height: 1.45,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  steps,
                  style: TextStyle(
                    fontSize: 14,
                    height: 1.5,
                    color: colors.inkSoft,
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  'Jarvis asks for your OK before it acts in ${info.name}, unless you allowed that action in advance.',
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.45,
                    color: colors.muted,
                  ),
                ),
                const SizedBox(height: 24),
                FilledButton(
                  onPressed: () => Navigator.pop(sheetContext, 'key'),
                  child: Text(
                    added ? 'Replace access key' : 'Paste access key',
                  ),
                ),
                if (ask != null) ...[
                  const SizedBox(height: 8),
                  TextButton(
                    onPressed: () => Navigator.pop(sheetContext, 'chat'),
                    child: const Text('Ask Jarvis to help'),
                  ),
                ],
              ],
            ),
          ),
        );
      },
    );
    if (!mounted) return;
    switch (choice) {
      case 'key':
        await _editSecret(provider: provider, secretName: 'token');
      case 'chat':
        _askInChat(
          'Help me connect ${info.name} in this chat. Show the setup card.',
        );
    }
  }

  /// Saved keys are rarely needed, so they sit folded at the bottom.
  Widget _savedKeys(List<Map<String, dynamic>> providers) {
    final colors = JarvisColors.of(context);
    final count = providers.fold<int>(
      0,
      (sum, item) => sum + jsonStrings(item['secretNames']).length,
    );
    return Theme(
      data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
      child: ExpansionTile(
        key: const Key('saved-keys'),
        tilePadding: const EdgeInsets.symmetric(horizontal: 4),
        shape: const Border(),
        collapsedShape: const Border(),
        title: Text(
          'Saved keys',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        subtitle: Text(
          _credentialsFailed
              ? 'Could not load your saved keys.'
              : count == 0
              ? 'None yet. Keys are stored encrypted and never shown again.'
              : '${count == 1 ? '1 key' : '$count keys'}, stored encrypted and never shown again.',
          style: TextStyle(fontSize: 13, color: colors.muted),
        ),
        children: [for (final item in providers) _providerCard(item)],
      ),
    );
  }

  Widget _providerCard(Map<String, dynamic> provider) {
    final slug = asJsonString(provider['provider']) ?? '';
    final info = _appInfo(slug);
    final names = jsonStrings(provider['secretNames']);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 12, 8, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(icon: info.icon),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  info.name,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              IconButton(
                tooltip: 'Remove all keys for ${info.name}',
                onPressed: () => _deleteProvider(slug),
                icon: const Icon(PhosphorIconsRegular.trash, size: 20),
              ),
            ],
          ),
          const SizedBox(height: 4),
          for (final name in names)
            ListTile(
              dense: true,
              contentPadding: const EdgeInsets.only(left: 4),
              leading: const Icon(PhosphorIconsRegular.key, size: 19),
              title: Text(_secretLabel(name)),
              subtitle: const Text('Saved · hidden'),
              trailing: Wrap(
                children: [
                  IconButton(
                    tooltip: 'Replace',
                    onPressed: () =>
                        _editSecret(provider: slug, secretName: name),
                    icon: const Icon(
                      PhosphorIconsRegular.pencilSimple,
                      size: 19,
                    ),
                  ),
                  IconButton(
                    tooltip: 'Delete',
                    onPressed: () => _deleteSecret(slug, name),
                    icon: const Icon(
                      PhosphorIconsRegular.minusCircle,
                      size: 19,
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
