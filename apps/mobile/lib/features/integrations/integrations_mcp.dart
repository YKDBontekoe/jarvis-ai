part of 'integrations_screen.dart';

// ignore_for_file: annotate_overrides

mixin _IntegrationsMcp on _IntegrationsController {
  /// Every connection in one list: servers the owner added, and servers the
  /// host runs (such as GitHub) that are not already shown.
  List<Widget> _connectionCards() => [
    for (final server in _managedServers) _managedServerCard(server),
    for (final connection in _connections)
      if (!_isManaged(connection)) _hostConnectionCard(connection),
  ];

  bool _isManaged(Map<String, dynamic> connection) => _managedServers.any(
    (server) =>
        asJsonString(server['id']) != null &&
        asJsonString(server['id']) == asJsonString(connection['id']),
  );

  Map<String, dynamic>? _connectionFor(String id) =>
      _connections.where((item) => asJsonString(item['id']) == id).firstOrNull;

  int _needsAttentionCount() {
    var count = 0;
    for (final server in _managedServers) {
      if (server['enabled'] == false) continue;
      final connection = _connectionFor(asJsonString(server['id']) ?? '');
      if (connection != null && _health(connection).fix != _Fix.none) count++;
    }
    for (final connection in _connections) {
      if (!_isManaged(connection) && _health(connection).fix != _Fix.none) {
        count++;
      }
    }
    return count;
  }

  /// Plain-language status for a live connection, and the one fix that helps.
  _Health _health(Map<String, dynamic>? connection, {bool enabled = true}) {
    final colors = JarvisColors.of(context);
    if (!enabled) {
      return (
        label: 'Paused',
        detail: 'Jarvis won’t use it until you switch it back on.',
        color: colors.muted,
        fix: _Fix.none,
      );
    }
    if (connection == null) {
      return (
        label: 'Checking',
        detail: 'Jarvis checks it the next time it needs it.',
        color: colors.muted,
        fix: _Fix.none,
      );
    }
    final state = asJsonString(connection['state']) ?? 'unavailable';
    final toolCount = asJsonInt(connection['toolCount']);
    final issue = switch (asJsonString(connection['issue'])) {
      'too_many_tools' =>
        'It offers too many actions at once. Ask Jarvis to keep only the ones you need.',
      'no_matching_tools' =>
        'The actions you picked are no longer offered. Ask Jarvis to pick new ones.',
      'invalid_configuration' => 'Its setup needs fixing. Ask Jarvis to help.',
      'connection_timed_out' =>
        'It took too long to answer. It may be busy or offline.',
      _ => null,
    };
    return switch (state) {
      'connected' => (
        label: 'Working',
        detail: 'Jarvis can use ${toolCount.actions()} here.',
        color: colors.success,
        fix: _Fix.none,
      ),
      'needs_credentials' => (
        label: 'Needs sign-in',
        detail: issue ?? 'Sign in so Jarvis can use it.',
        color: colors.warning,
        fix: _Fix.signIn,
      ),
      'paused' => (
        label: 'Paused',
        detail: 'Jarvis won’t use it until you switch it back on.',
        color: colors.muted,
        fix: _Fix.none,
      ),
      'disabled' => (
        label: 'Off',
        detail: issue ?? 'No actions are switched on for Jarvis.',
        color: colors.muted,
        fix: _Fix.none,
      ),
      _ => (
        label: 'Can’t connect',
        detail: issue ?? 'Jarvis couldn’t reach it. It may be offline.',
        color: colors.danger,
        fix: _Fix.retry,
      ),
    };
  }

  Widget _managedServerCard(Map<String, dynamic> server) {
    final id = asJsonString(server['id']) ?? '';
    final info = _appInfo(asJsonString(server['name']) ?? '');
    final endpoint = asJsonString(server['endpoint']) ?? '';
    final tools = jsonStrings(server['allowedTools']);
    final allTools = tools.length == 1 && tools.first == '*';
    final enabled = server['enabled'] != false;
    final hasToken = server['hasToken'] == true;
    final secrets = jsonMaps(server['secrets']);
    final missingKeys = secrets
        .where((s) => s['required'] == true && s['isSet'] != true)
        .toList();
    final health = _health(_connectionFor(id), enabled: enabled);
    return _AppCard(
      key: Key('app-$id'),
      info: info,
      health: health,
      enabled: enabled,
      onEnabled: id.isEmpty ? null : (value) => _setManagedEnabled(id, value),
      fixes: [
        // A missing key is the real problem; signing in would not help.
        if (enabled && missingKeys.isNotEmpty)
          for (final key in missingKeys)
            FilledButton(
              onPressed: () => _editSecret(
                provider: id,
                secretName: asJsonString(key['name']),
              ),
              child: Text(
                'Add ${asJsonString(key['label']) ?? asJsonString(key['name'])}',
              ),
            )
        else if (health.fix == _Fix.signIn) ...[
          FilledButton(
            onPressed: () => _connectOAuth(server: id, endpoint: endpoint),
            child: const Text('Sign in'),
          ),
          TextButton(
            onPressed: () => _editSecret(provider: id, secretName: 'token'),
            child: const Text('Use a key instead'),
          ),
        ],
        if (health.fix == _Fix.retry)
          FilledButton.tonal(onPressed: _load, child: const Text('Try again')),
      ],
      details: [
        _DetailRow(
          label: 'What Jarvis may use',
          child: allTools
              ? const Text('Everything this app offers')
              : Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: [for (final tool in tools) _ToolChip(tool)],
                ),
        ),
        if (secrets.isNotEmpty)
          _DetailRow(
            label: 'Keys',
            child: Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final key in secrets)
                  ActionChip(
                    avatar: Icon(
                      key['isSet'] == true
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.key,
                      size: 16,
                    ),
                    label: Text(
                      asJsonString(key['label']) ??
                          asJsonString(key['name']) ??
                          'Key',
                    ),
                    onPressed: () => _editSecret(
                      provider: id,
                      secretName: asJsonString(key['name']),
                    ),
                  ),
              ],
            ),
          ),
        if (endpoint.isNotEmpty)
          _DetailRow(
            label: 'Address',
            child: SelectableText(
              endpoint,
              style: TextStyle(
                fontFamily: 'monospace',
                fontSize: 12.5,
                color: JarvisColors.of(context).inkSoft,
              ),
            ),
          ),
        Wrap(
          spacing: 4,
          runSpacing: 4,
          children: [
            TextButton.icon(
              onPressed: () => _connectOAuth(server: id, endpoint: endpoint),
              icon: const Icon(PhosphorIconsRegular.signIn, size: 18),
              label: const Text('Sign in again'),
            ),
            TextButton.icon(
              onPressed: () => _editSecret(provider: id, secretName: 'token'),
              icon: const Icon(PhosphorIconsRegular.key, size: 18),
              label: Text(hasToken ? 'Replace key' : 'Add a key'),
            ),
            TextButton.icon(
              onPressed: () => _removeManagedServer(id, info.name),
              style: TextButton.styleFrom(
                foregroundColor: JarvisColors.of(context).danger,
              ),
              icon: const Icon(PhosphorIconsRegular.trash, size: 18),
              label: const Text('Remove'),
            ),
          ],
        ),
      ],
    );
  }

  /// A server the Jarvis host runs. The owner can pause it for their account;
  /// setup itself happens on the server.
  Widget _hostConnectionCard(Map<String, dynamic> connection) {
    final name = asJsonString(connection['name']) ?? '';
    final info = _appInfo(name);
    final state = asJsonString(connection['state']) ?? 'unavailable';
    final health = _health(connection);
    final canPause = name.isNotEmpty && name != '(unnamed)';
    final provider = name.toLowerCase();
    return _AppCard(
      key: Key('app-$name'),
      info: info,
      health: health,
      enabled: state != 'paused',
      onEnabled: canPause ? (value) => _setHostEnabled(name, value) : null,
      fixes: [
        if (health.fix == _Fix.signIn)
          FilledButton(
            onPressed: () =>
                _editSecret(provider: provider, secretName: 'token'),
            child: const Text('Add access key'),
          ),
        if (health.fix == _Fix.retry)
          FilledButton.tonal(onPressed: _load, child: const Text('Try again')),
      ],
      details: const [
        _DetailRow(
          label: 'Set up on your Jarvis server',
          child: Text(
            'Whoever runs your Jarvis server added this app. You can pause it for your account here.',
          ),
        ),
      ],
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
      if (mounted) setState(() => _error = 'Could not update that app.');
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not update that app.');
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
      if (mounted) setState(() => _error = 'Could not update that app.');
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not update that app.');
    }
  }

  Future<void> _removeManagedServer(String id, String name) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove $name?',
      message:
          'Jarvis stops using $name and forgets its saved keys. You can connect it again later.',
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
      if (mounted) setState(() => _error = 'Could not remove that app.');
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not remove that app.');
    }
  }
}

/// One connected app: what it is, how it is doing, the one fix it needs,
/// and the rest tucked away under Details.
class _AppCard extends StatelessWidget {
  const _AppCard({
    required this.info,
    required this.health,
    required this.enabled,
    required this.onEnabled,
    required this.fixes,
    required this.details,
    super.key,
  });

  final _AppInfo info;
  final _Health health;
  final bool enabled;
  final ValueChanged<bool>? onEnabled;
  final List<Widget> fixes;
  final List<Widget> details;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.fromLTRB(16, 16, 12, 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              IconBadge(icon: info.icon, size: 40),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(info.name, style: theme.textTheme.titleMedium),
                    const SizedBox(height: 4),
                    Row(
                      children: [
                        Container(
                          width: 7,
                          height: 7,
                          decoration: BoxDecoration(
                            color: health.color,
                            shape: BoxShape.circle,
                          ),
                        ),
                        const SizedBox(width: 6),
                        Flexible(
                          child: Text(
                            health.label,
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w500,
                              color: colors.inkSoft,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              Semantics(
                label: 'Let Jarvis use ${info.name}',
                child: Switch(value: enabled, onChanged: onEnabled),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            [?info.what, health.detail].join(' '),
            style: TextStyle(
              fontSize: 13.5,
              height: 1.45,
              color: colors.inkSoft,
            ),
          ),
          if (fixes.isNotEmpty) ...[
            const SizedBox(height: 12),
            Wrap(spacing: 8, runSpacing: 8, children: fixes),
          ],
          Theme(
            data: theme.copyWith(dividerColor: Colors.transparent),
            child: ExpansionTile(
              tilePadding: EdgeInsets.zero,
              childrenPadding: const EdgeInsets.only(bottom: 8),
              expandedCrossAxisAlignment: CrossAxisAlignment.start,
              shape: const Border(),
              collapsedShape: const Border(),
              visualDensity: VisualDensity.compact,
              title: Text(
                'Details',
                style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w500,
                  color: colors.muted,
                ),
              ),
              children: details,
            ),
          ),
        ],
      ),
    );
  }
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.label, required this.child});

  final String label;
  final Widget child;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 14),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: Theme.of(context).textTheme.labelSmall?.copyWith(
            color: JarvisColors.of(context).muted,
            letterSpacing: .3,
          ),
        ),
        const SizedBox(height: 6),
        DefaultTextStyle.merge(
          style: TextStyle(
            fontSize: 13.5,
            height: 1.4,
            color: JarvisColors.of(context).inkSoft,
          ),
          child: child,
        ),
      ],
    ),
  );
}

class _ToolChip extends StatelessWidget {
  const _ToolChip(this.name);

  final String name;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
    decoration: BoxDecoration(
      color: JarvisColors.of(context).surfaceMuted,
      borderRadius: BorderRadius.circular(8),
    ),
    child: Text(
      name.replaceAll('_', ' '),
      style: TextStyle(fontSize: 12, color: JarvisColors.of(context).inkSoft),
    ),
  );
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
      border: Border.all(color: JarvisColors.of(context).outlineStrong),
    ),
    child: Row(
      children: [
        Icon(icon, size: 20, color: JarvisColors.of(context).muted),
        const SizedBox(width: 12),
        Expanded(
          child: Text(
            text,
            style: TextStyle(color: JarvisColors.of(context).inkSoft),
          ),
        ),
      ],
    ),
  );
}
