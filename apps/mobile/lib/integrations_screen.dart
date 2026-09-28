import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'theme.dart';
import 'json_maps.dart';
import 'ui/jarvis_ui.dart';

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
  bool _credentialsFailed = false;
  bool _connectionsFailed = false;
  bool _serversFailed = false;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    try {
      var credentialsFailed = false;
      var connectionsFailed = false;
      var serversFailed = false;
      String? error;
      try {
        final response = await widget.http.get<dynamic>(
          '/api/v1/integrations/credentials',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _providers = jsonMaps(response.data));
      } on DioException {
        credentialsFailed = true;
        error = 'Could not load integration credentials.';
      }
      try {
        final connectionResponse = await widget.http.get<dynamic>(
          '/api/v1/integrations/connections',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _connections = jsonMaps(connectionResponse.data));
      } on DioException {
        connectionsFailed = true;
        error ??= 'Could not load integration connections.';
      }
      try {
        final serversResponse = await widget.http.get<dynamic>(
          '/api/v1/mcp-servers',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _managedServers = jsonMaps(serversResponse.data));
      } on DioException {
        serversFailed = true;
        error ??= 'Could not load MCP servers.';
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _credentialsFailed = credentialsFailed;
        _connectionsFailed = connectionsFailed;
        _serversFailed = serversFailed;
        _loading = false;
        _error = error;
      });
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() {
          _loading = false;
          _error ??= 'Could not load integration credentials.';
        });
      }
    }
  }

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
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Integrations'),
      actions: [
        HeaderAction(
          label: 'Add',
          icon: PhosphorIconsRegular.plus,
          onPressed: () => _editSecret(),
        ),
      ],
    ),
    body: _loading
        ? const LoadingState()
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: _content(),
                  ),
                ),
              ],
            ),
          ),
  );

  List<Widget> _content() {
    final providers = _providers
        .where(
          (item) =>
              !(asJsonString(item['provider']) ?? '').startsWith('jarvis-mcp-'),
        )
        .toList();
    return [
      const InlineNotice(
        tone: NoticeTone.info,
        message:
            'Secrets are encrypted before storage. Values stay hidden and are injected only into configured MCP connections.',
      ),
      if (_error != null)
        InlineNotice(
          message: _error!,
          tone: NoticeTone.danger,
          margin: const EdgeInsets.only(top: 12),
        ),
      const SizedBox(height: 24),
      const SectionHeader('Featured'),
      _featuredCard(
        title: 'Home Assistant',
        icon: PhosphorIconsRegular.house,
        description:
            'Enable Home Assistant’s MCP Server integration, expose the entities Jarvis may use, and configure HOME_ASSISTANT_MCP_URL on the Jarvis host. Store a long-lived access token here; Jarvis adds the bearer scheme when connecting. Jarvis asks for approval before every Home Assistant action.',
        provider: 'home-assistant',
      ),
      const SizedBox(height: 12),
      _featuredCard(
        title: 'GitHub',
        icon: PhosphorIconsRegular.code,
        description:
            'Enable the GitHub MCP Compose overlay, then store a least-privilege personal access token here. Jarvis exposes repository, issue, and pull request tools. In chat it can pause GitHub or narrow those tools. Each operation asks for approval.',
        provider: 'github',
      ),
      const SizedBox(height: 28),
      const SectionHeader('MCP connections'),
      for (final server in _managedServers) _managedServerCard(server),
      if ((_connectionsFailed || _serversFailed) &&
          _connections.isEmpty &&
          _managedServers.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.cloudSlash,
          text: 'Could not load MCP servers.',
        )
      else if (_connections.isEmpty && _managedServers.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.cloudSlash,
          text: 'No MCP servers are configured on this Jarvis host.',
        ),
      for (final connection in _connections) _connectionCard(connection),
      const SizedBox(height: 28),
      const SectionHeader('Stored credentials'),
      if (_credentialsFailed)
        const _MutedLine(
          icon: PhosphorIconsRegular.key,
          text: 'Could not load integration credentials.',
        )
      else if (providers.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.key,
          text: 'No integration credentials yet.',
        ),
      for (final item in providers) _providerCard(item),
    ];
  }

  Widget _featuredCard({
    required String title,
    required IconData icon,
    required String description,
    required String provider,
  }) {
    final configured = !_credentialsFailed &&
        _providers.any(
          (item) =>
              item['provider'] == provider &&
              jsonStrings(item['secretNames']).contains(
                'token',
              ),
        );
    final statusLabel = _credentialsFailed
        ? 'Couldn’t load'
        : configured
        ? 'Token stored'
        : 'Not configured';
    final statusColor = _credentialsFailed
        ? JarvisColors.danger
        : configured
        ? JarvisColors.success
        : JarvisColors.muted;
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
              StatusPill(
                label: statusLabel,
                color: statusColor,
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            description,
            style: const TextStyle(
              height: 1.5,
              fontSize: 13.5,
              color: JarvisColors.inkSoft,
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

  Widget _managedServerCard(Map<String, dynamic> server) {
    final id = asJsonString(server['id']) ?? '';
    final name = asJsonString(server['name']) ?? 'MCP server';
    final endpoint = asJsonString(server['endpoint']) ?? '';
    final tools = jsonStrings(server['allowedTools']);
    final allTools = tools.length == 1 && tools.first == '*';
    final enabled = server['enabled'] != false;
    final hasToken = server['hasToken'] == true;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 8, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.plugsConnected),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  name,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              if (!enabled)
                const Padding(
                  padding: EdgeInsets.only(right: 4),
                  child: StatusPill(label: 'Paused', color: JarvisColors.muted),
                ),
              Switch(
                value: enabled,
                onChanged: id.isEmpty
                    ? null
                    : (value) => _setManagedEnabled(id, value),
              ),
              IconButton(
                tooltip: 'Remove MCP server',
                onPressed: () => _removeManagedServer(id, name),
                icon: const Icon(PhosphorIconsRegular.trash, size: 20),
              ),
            ],
          ),
          const SizedBox(height: 10),
          SelectableText(
            endpoint,
            style: const TextStyle(
              fontFamily: 'monospace',
              fontSize: 12.5,
              color: JarvisColors.inkSoft,
            ),
          ),
          const SizedBox(height: 10),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              if (allTools)
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 4,
                  ),
                  decoration: BoxDecoration(
                    color: JarvisColors.surfaceMuted,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: const Text(
                    'Every exposed tool',
                    style: TextStyle(
                      fontSize: 12,
                      color: JarvisColors.inkSoft,
                    ),
                  ),
                ),
              if (!allTools)
                for (final tool in tools)
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 4,
                  ),
                  decoration: BoxDecoration(
                    color: JarvisColors.surfaceMuted,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    tool,
                    style: const TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 12,
                      color: JarvisColors.inkSoft,
                    ),
                  ),
                ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            'Credential provider: $id',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton.icon(
              onPressed: () => _editSecret(provider: id, secretName: 'token'),
              icon: const Icon(PhosphorIconsRegular.key, size: 18),
              label: Text(
                hasToken ? 'Rotate bearer token' : 'Add bearer token',
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _setManagedEnabled(String id, bool enabled) async {
    try {
      await widget.http.put<void>(
        '/api/v1/mcp-servers/${Uri.encodeComponent(id)}/state',
        data: {'enabled': enabled},
      );
      if (mounted) await _load();
    } on DioException {
      if (mounted) setState(() => _error = 'Could not update the MCP server.');
    }
  }

  Future<void> _setHostEnabled(String name, bool enabled) async {
    try {
      await widget.http.put<void>(
        '/api/v1/mcp-controls/${Uri.encodeComponent(name)}',
        data: {'enabled': enabled},
      );
      if (mounted) await _load();
    } on DioException {
      if (mounted) setState(() => _error = 'Could not update the MCP server.');
    }
  }

  Future<void> _removeManagedServer(String id, String name) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove MCP server?',
      message: 'Remove $name and its stored credentials?',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.plugsConnected,
    );
    if (!confirmed) return;
    if (!mounted) return;
    try {
      await widget.http.delete<void>(
        '/api/v1/mcp-servers/${Uri.encodeComponent(id)}',
      );
      if (mounted) await _load();
    } on DioException {
      if (mounted) setState(() => _error = 'Could not remove MCP server.');
    }
  }

  Widget _providerCard(Map<String, dynamic> provider) {
    final slug = asJsonString(provider['provider']) ?? '';
    final names =
        jsonStrings(provider['secretNames']);
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

  Widget _connectionCard(Map<String, dynamic> connection) {
    final name = asJsonString(connection['name']) ?? 'MCP server';
    final id = asJsonString(connection['id']);
    final state = asJsonString(connection['state']) ?? 'unavailable';
    final toolCount = asJsonInt(connection['toolCount']);
    final issue = asJsonString(connection['issue']);
    final detail = switch (issue) {
      'too_many_tools' => 'More than 80 tools. Narrow the allowlist.',
      'no_matching_tools' => 'None of the selected tools are available',
      'invalid_configuration' => 'Configuration needs attention',
      _ => null,
    };
    final (icon, fallback, label, color) = switch (state) {
      'connected' => (
        PhosphorIconsRegular.checkCircle,
        '$toolCount allowlisted tools available',
        'Connected',
        JarvisColors.success,
      ),
      'needs_credentials' => (
        PhosphorIconsRegular.key,
        'Owner credentials are required',
        'Needs token',
        JarvisColors.warning,
      ),
      'paused' => (
        PhosphorIconsRegular.pauseCircle,
        'Paused for this account',
        'Paused',
        JarvisColors.muted,
      ),
      'disabled' => (
        PhosphorIconsRegular.prohibit,
        'No tools are allowlisted',
        'Disabled',
        JarvisColors.muted,
      ),
      _ => (
        PhosphorIconsRegular.warningCircle,
        'Server could not be reached',
        'Unavailable',
        JarvisColors.danger,
      ),
    };
    final hostControlled = id == null || id.isEmpty;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      child: Row(
        children: [
          IconBadge(icon: icon),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(name, style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 2),
                Text(
                  detail ?? fallback,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                if (hostControlled && name != '(unnamed)')
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton(
                      onPressed: () =>
                          _setHostEnabled(name, state == 'paused'),
                      child: Text(state == 'paused' ? 'Resume' : 'Pause'),
                    ),
                  ),
              ],
            ),
          ),
          StatusPill(label: label, color: color),
        ],
      ),
    );
  }
}

class _MutedLine extends StatelessWidget {
  const _MutedLine({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(bottom: 10),
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
    decoration: BoxDecoration(
      borderRadius: BorderRadius.circular(JarvisRadii.lg),
      border: Border.all(color: JarvisColors.outlineStrong),
    ),
    child: Row(
      children: [
        Icon(icon, size: 20, color: JarvisColors.muted),
        const SizedBox(width: 12),
        Expanded(
          child: Text(
            text,
            style: const TextStyle(color: JarvisColors.inkSoft),
          ),
        ),
      ],
    ),
  );
}
