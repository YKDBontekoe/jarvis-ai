import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

class IntegrationsScreen extends StatefulWidget {
  const IntegrationsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<IntegrationsScreen> createState() => _IntegrationsScreenState();
}

class _IntegrationsScreenState extends State<IntegrationsScreen> {
  List<Map<String, dynamic>> _providers = [];
  List<Map<String, dynamic>> _connections = [];
  List<Map<String, dynamic>> _managedServers = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<List<dynamic>>(
        '/api/v1/integrations/credentials',
      );
      final connectionResponse = await widget.http.get<List<dynamic>>(
        '/api/v1/integrations/connections',
      );
      final serversResponse = await widget.http.get<List<dynamic>>(
        '/api/v1/mcp-servers',
      );
      if (!mounted) return;
      setState(() {
        _providers = (response.data ?? const <dynamic>[])
            .cast<Map<String, dynamic>>()
            .toList();
        _connections = (connectionResponse.data ?? const <dynamic>[])
            .cast<Map<String, dynamic>>()
            .toList();
        _managedServers = (serversResponse.data ?? const <dynamic>[])
            .cast<Map<String, dynamic>>()
            .toList();
        _loading = false;
        _error = null;
      });
    } on DioException {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = 'Could not load integration credentials.';
        });
      }
    }
  }

  Future<void> _editSecret({String? provider, String? secretName}) async {
    final replacing =
        secretName != null &&
        _providers.any(
          (item) =>
              item['provider'] == provider &&
              (item['secretNames'] as List<dynamic>? ?? const <dynamic>[])
                  .contains(secretName),
        );
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
                            ? Icons.visibility_outlined
                            : Icons.visibility_off_outlined,
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
                        final data = error.response?.data;
                        final message = data is Map<String, dynamic>
                            ? _problemMessage(data)
                            : 'Could not save credential.';
                        if (dialogContext.mounted) {
                          setDialogState(() {
                            dialogError = message;
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
    if (saved == true) await _load();
  }

  String _problemMessage(Map<String, dynamic> data) {
    final detail = data['detail'] as String?;
    if (detail != null) return detail;
    final errors = data['errors'];
    if (errors is Map<String, dynamic>) {
      for (final value in errors.values) {
        if (value is List && value.isNotEmpty) return value.first.toString();
      }
    }
    return 'Could not save credential.';
  }

  Future<void> _deleteSecret(String provider, String secretName) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete credential?'),
        content: Text('Remove $secretName from $provider?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      await widget.http.delete<void>(
        '/api/v1/integrations/${Uri.encodeComponent(provider)}/credentials/${Uri.encodeComponent(secretName)}',
      );
      await _load();
    } on DioException {
      if (mounted) setState(() => _error = 'Could not delete credential.');
    }
  }

  Future<void> _deleteProvider(String provider) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Remove integration credentials?'),
        content: Text('Delete all stored credentials for $provider?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete all'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      await widget.http.delete<void>(
        '/api/v1/integrations/${Uri.encodeComponent(provider)}/credentials',
      );
      await _load();
    } on DioException {
      if (mounted) {
        setState(() => _error = 'Could not remove integration credentials.');
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Integrations')),
    floatingActionButton: FloatingActionButton.extended(
      onPressed: () => _editSecret(),
      icon: const Icon(Icons.add),
      label: const Text('Add credential'),
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 100),
              children: [
                const Text(
                  'Secrets are encrypted before storage. Values stay hidden and are injected only into configured MCP connections.',
                  style: TextStyle(height: 1.45),
                ),
                Card(
                  margin: const EdgeInsets.only(top: 16),
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Home Assistant',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 6),
                        const Text(
                          'Enable Home Assistant’s MCP Server integration, expose the entities Jarvis may use, and configure HOME_ASSISTANT_MCP_URL on the Jarvis host. Store a long-lived access token here; Jarvis adds the bearer scheme when connecting. Jarvis asks for approval before every Home Assistant action.',
                          style: TextStyle(height: 1.4),
                        ),
                        const SizedBox(height: 12),
                        FilledButton.icon(
                          onPressed: () => _editSecret(
                            provider: 'home-assistant',
                            secretName: 'token',
                          ),
                          icon: const Icon(Icons.home_outlined),
                          label: const Text('Set or rotate token'),
                        ),
                      ],
                    ),
                  ),
                ),
                Card(
                  margin: const EdgeInsets.only(top: 12),
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'GitHub',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 6),
                        const Text(
                          'Enable the GitHub MCP Compose overlay, then store a least-privilege personal access token here. Jarvis exposes repository, issue, and pull request tools; each operation asks for approval.',
                          style: TextStyle(height: 1.4),
                        ),
                        const SizedBox(height: 12),
                        FilledButton.icon(
                          onPressed: () => _editSecret(
                            provider: 'github',
                            secretName: 'token',
                          ),
                          icon: const Icon(Icons.code_outlined),
                          label: const Text('Set or rotate token'),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 20),
                Text(
                  'MCP connections',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                for (final server in _managedServers)
                  _managedServerCard(server),
                if (_connections.isEmpty)
                  const Padding(
                    padding: EdgeInsets.only(top: 8),
                    child: Text(
                      'No MCP servers are configured on this Jarvis host.',
                    ),
                  ),
                for (final connection in _connections)
                  _connectionCard(connection),
                if (_error != null) ...[
                  const SizedBox(height: 12),
                  Text(
                    _error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ],
                if (_providers.isEmpty) ...[
                  const SizedBox(height: 48),
                  const Icon(Icons.hub_outlined, size: 40),
                  const SizedBox(height: 12),
                  const Center(child: Text('No integration credentials yet.')),
                ],
                for (final item in _providers.where(
                  (item) => !(item['provider'] as String? ?? '').startsWith(
                    'jarvis-mcp-',
                  ),
                ))
                  _providerCard(item),
              ],
            ),
          ),
  );

  Widget _managedServerCard(Map<String, dynamic> server) {
    final id = server['id'] as String? ?? '';
    final name = server['name'] as String? ?? 'MCP server';
    final endpoint = server['endpoint'] as String? ?? '';
    final tools =
        (server['allowedTools'] as List<dynamic>? ?? const <dynamic>[])
            .cast<String>();
    final hasToken = _providers.any(
      (provider) =>
          provider['provider'] == id &&
          (provider['secretNames'] as List<dynamic>? ?? const <dynamic>[])
              .contains('token'),
    );
    return Card(
      margin: const EdgeInsets.only(top: 8),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Icons.hub_outlined),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    name,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                IconButton(
                  tooltip: 'Remove MCP server',
                  onPressed: () => _removeManagedServer(id, name),
                  icon: const Icon(Icons.delete_outline),
                ),
              ],
            ),
            SelectableText(
              endpoint,
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 4),
            Text(
              'Allowed tools: ${tools.join(', ')}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 4),
            Text(
              'Credential provider: $id',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            TextButton.icon(
              onPressed: () => _editSecret(provider: id, secretName: 'token'),
              icon: const Icon(Icons.key_outlined),
              label: Text(
                hasToken ? 'Rotate bearer token' : 'Add bearer token',
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _removeManagedServer(String id, String name) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Remove MCP server?'),
        content: Text('Remove $name and its stored credentials?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Remove'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      await widget.http.delete<void>(
        '/api/v1/mcp-servers/${Uri.encodeComponent(id)}',
      );
      await _load();
    } on DioException {
      if (mounted) setState(() => _error = 'Could not remove MCP server.');
    }
  }

  Widget _providerCard(Map<String, dynamic> provider) {
    final slug = provider['provider'] as String? ?? '';
    final names =
        (provider['secretNames'] as List<dynamic>? ?? const <dynamic>[])
            .cast<String>();
    return Card(
      margin: const EdgeInsets.only(top: 12),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Icons.hub_outlined),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    slug,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                IconButton(
                  tooltip: 'Remove all credentials',
                  onPressed: () => _deleteProvider(slug),
                  icon: const Icon(Icons.delete_outline),
                ),
              ],
            ),
            for (final name in names)
              ListTile(
                dense: true,
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.key_outlined, size: 19),
                title: Text(name),
                subtitle: const Text('Stored securely · value hidden'),
                trailing: Wrap(
                  children: [
                    IconButton(
                      tooltip: 'Replace value',
                      onPressed: () =>
                          _editSecret(provider: slug, secretName: name),
                      icon: const Icon(Icons.edit_outlined),
                    ),
                    IconButton(
                      tooltip: 'Delete value',
                      onPressed: () => _deleteSecret(slug, name),
                      icon: const Icon(Icons.remove_circle_outline),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _connectionCard(Map<String, dynamic> connection) {
    final name = connection['name'] as String? ?? 'MCP server';
    final state = connection['state'] as String? ?? 'unavailable';
    final toolCount = connection['toolCount'] as int? ?? 0;
    final issue = connection['issue'] as String?;
    final (icon, detail) = switch (state) {
      'connected' => (
        Icons.check_circle_outline,
        '$toolCount allowlisted tools available',
      ),
      'needs_credentials' => (
        Icons.key_outlined,
        'Owner credentials are required',
      ),
      'disabled' => (Icons.block_outlined, 'No tools are allowlisted'),
      _ => (
        Icons.error_outline,
        issue == 'invalid_configuration'
            ? 'Configuration needs attention'
            : 'Server could not be reached',
      ),
    };
    return Card(
      margin: const EdgeInsets.only(top: 8),
      child: ListTile(
        leading: Icon(icon),
        title: Text(name),
        subtitle: Text(detail),
      ),
    );
  }
}
