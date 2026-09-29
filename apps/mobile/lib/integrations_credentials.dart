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
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(
            replacing ? 'Replace credential' : 'Add integration credential',
          ),
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
                      labelText: 'Provider slug',
                      hintText: 'home-assistant',
                    ),
                  ),
                if (secretName == null)
                  TextField(
                    controller: nameController,
                    autocorrect: false,
                    textCapitalization: TextCapitalization.none,
                    decoration: const InputDecoration(
                      labelText: 'Credential name',
                      hintText: 'token',
                    ),
                  ),
                TextField(
                  controller: valueController,
                  obscureText: obscure,
                  autocorrect: false,
                  enableSuggestions: false,
                  decoration: InputDecoration(
                    labelText: 'Secret value',
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
                          () => dialogError =
                              'Enter a provider, credential name, and value.',
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
                            'Could not save credential.';
                        if (dialogContext.mounted) {
                          setDialogState(() {
                            dialogError = message;
                            saving = false;
                          });
                        }
                      } catch (_) {
                        if (dialogContext.mounted) {
                          setDialogState(() {
                            dialogError = 'Could not save credential.';
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
      title: 'Delete credential?',
      message: 'Remove $secretName from $provider?',
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
      if (mounted) setState(() => _error = 'Could not delete credential.');
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not delete credential.');
    }
  }

  Future<void> _deleteProvider(String provider) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove integration credentials?',
      message: 'Delete all stored credentials for $provider?',
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
        setState(() => _error = 'Could not remove integration credentials.');
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Could not remove integration credentials.');
      }
    }
  }

  Widget _featuredCard({
    required String title,
    required IconData icon,
    required String description,
    required String provider,
  }) {
    final configured =
        !_credentialsFailed &&
        _providers.any(
          (item) =>
              item['provider'] == provider &&
              jsonStrings(item['secretNames']).contains('token'),
        );
    final statusLabel = _credentialsFailed
        ? 'Couldn’t load'
        : configured
        ? 'Token stored'
        : 'Not configured';
    final statusColor = _credentialsFailed
        ? JarvisColors.of(context).danger
        : configured
        ? JarvisColors.of(context).success
        : JarvisColors.of(context).muted;
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(icon: icon, size: 44),
              const SizedBox(width: 14),
              Expanded(
                child: Text(
                  title,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              StatusPill(label: statusLabel, color: statusColor),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            description,
            style: TextStyle(
              height: 1.5,
              fontSize: 13.5,
              color: JarvisColors.of(context).inkSoft,
            ),
          ),
          const SizedBox(height: 16),
          OutlinedButton.icon(
            onPressed: () =>
                _editSecret(provider: provider, secretName: 'token'),
            icon: const Icon(PhosphorIconsRegular.key, size: 18),
            label: const Text('Set or rotate token'),
          ),
        ],
      ),
    );
  }

  Widget _providerCard(Map<String, dynamic> provider) {
    final slug = asJsonString(provider['provider']) ?? '';
    final names = jsonStrings(provider['secretNames']);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 12, 8, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.lockSimple),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  slug,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              IconButton(
                tooltip: 'Remove all credentials',
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
              title: Text(name),
              subtitle: const Text('Stored securely · value hidden'),
              trailing: Wrap(
                children: [
                  IconButton(
                    tooltip: 'Replace value',
                    onPressed: () =>
                        _editSecret(provider: slug, secretName: name),
                    icon: const Icon(
                      PhosphorIconsRegular.pencilSimple,
                      size: 19,
                    ),
                  ),
                  IconButton(
                    tooltip: 'Delete value',
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
