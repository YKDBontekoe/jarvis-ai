part of 'integrations_screen.dart';

// ignore_for_file: annotate_overrides

mixin _IntegrationsMcp on _IntegrationsController {
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
                    style: TextStyle(fontSize: 12, color: JarvisColors.inkSoft),
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
    } catch (_) {
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
    } catch (_) {
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
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not remove MCP server.');
    }
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
                      onPressed: () => _setHostEnabled(name, state == 'paused'),
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
