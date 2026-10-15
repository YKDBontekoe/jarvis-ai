import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/app_tiles.dart';
import '../tiles/tile_controller.dart';
import '../tiles/tile_models.dart';
import 'app_catalog_screen.dart' show AppKeysDialog;
import 'app_presentation.dart';

/// Everything about one connected app on one page: whether it works, its
/// keys and sign-in, which actions Jarvis may use, and for an app Jarvis
/// wrote, its code, tests and versions.
class McpServerDetailScreen extends StatefulWidget {
  const McpServerDetailScreen({
    required this.http,
    this.serverId,
    this.hostName,
    this.onAskInChat,
    this.tiles,
    super.key,
  }) : assert(serverId != null || hostName != null);

  final Dio http;

  /// An app the owner added (`jarvis-mcp-…`).
  final String? serverId;

  /// An app the Jarvis server runs, such as GitHub.
  final String? hostName;

  final ValueChanged<String>? onAskInChat;

  /// Home layout to pin this app to; defaults to the one Home is showing.
  final TileLayoutController? tiles;

  @override
  State<McpServerDetailScreen> createState() => _McpServerDetailScreenState();
}

class _McpServerDetailScreenState extends State<McpServerDetailScreen> {
  Map<String, dynamic>? _server;
  Map<String, dynamic>? _connection;
  Map<String, dynamic>? _custom;
  bool _loading = true;
  bool _busy = false;
  bool _testing = false;
  String? _error;
  AppTestResult? _test;
  int _revision = 0;

  bool get _isHost => widget.serverId == null;
  String get _key => widget.serverId ?? widget.hostName!;
  TileLayoutController? get _tiles =>
      widget.tiles ?? TileLayoutController.active;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final revision = ++_revision;
    String? error;
    Map<String, dynamic>? server;
    Map<String, dynamic>? connection;
    Map<String, dynamic>? custom;
    try {
      final connections = jsonMaps(
        (await widget.http.get<dynamic>(
          '/api/v1/integrations/connections',
        )).data,
      );
      connection = connections
          .where(
            (item) => _isHost
                ? item['hostManaged'] == true &&
                      asJsonString(item['name']) == widget.hostName
                : asJsonString(item['id']) == widget.serverId,
          )
          .firstOrNull;
    } catch (_) {
      error = 'Could not check how the app is doing.';
    }
    if (!_isHost) {
      try {
        server = jsonMaps(
          (await widget.http.get<dynamic>('/api/v1/mcp-servers')).data,
        ).where((item) => item['id'] == widget.serverId).firstOrNull;
      } catch (_) {
        error ??= 'Could not load the app.';
      }
      try {
        final summary =
            jsonMaps(
                  (await widget.http.get<dynamic>(
                    '/api/v1/custom-mcp-servers',
                  )).data,
                )
                .where((item) => item['registryServerId'] == widget.serverId)
                .firstOrNull;
        final id = asJsonString(summary?['id']);
        if (id != null) {
          custom = jsonObject(
            (await widget.http.get<dynamic>(
              '/api/v1/custom-mcp-servers/$id',
            )).data,
          );
        }
      } catch (_) {
        // Older servers have no self-built apps.
      }
    }
    if (!mounted || revision != _revision) return;
    setState(() {
      _server = server;
      _connection = connection;
      _custom = custom;
      _error = error;
      _loading = false;
    });
  }

  String get _name =>
      asJsonString(_server?['name']) ??
      asJsonString(_connection?['name']) ??
      widget.hostName ??
      'App';

  bool get _selfBuilt => _custom != null || _connection?['selfBuilt'] == true;

  bool get _enabled => _isHost
      ? asJsonString(_connection?['state']) != 'paused'
      : _server?['enabled'] != false;

  Future<void> _check() async {
    setState(() {
      _testing = true;
      _test = null;
    });
    final result = await testAppConnection(widget.http, _key);
    if (!mounted) return;
    setState(() {
      _testing = false;
      _test = result;
    });
    await _load();
  }

  Future<void> _run(Future<void> Function() action, String failure) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              asJsonString(jsonObject(error.response?.data)?['message']) ??
              failure,
        );
      }
    } catch (_) {
      if (mounted) setState(() => _error = failure);
    }
    if (!mounted) return;
    setState(() => _busy = false);
    await _load();
  }

  Future<void> _setEnabled(bool enabled) => _run(
    () => _isHost
        ? widget.http.put<dynamic>(
            '/api/v1/mcp-controls/${Uri.encodeComponent(_key)}',
            data: {'enabled': enabled},
          )
        : widget.http.put<dynamic>(
            '/api/v1/mcp-servers/${Uri.encodeComponent(_key)}/state',
            data: {'enabled': enabled},
          ),
    'Could not update the app.',
  );

  Future<void> _setTools(String mode, List<String> tools) => _run(
    () => widget.http.put<dynamic>(
      _isHost
          ? '/api/v1/mcp-controls/${Uri.encodeComponent(_key)}'
          : '/api/v1/mcp-servers/${Uri.encodeComponent(_key)}/state',
      data: {'toolMode': mode, 'allowedTools': tools},
    ),
    'Could not change what Jarvis may use.',
  );

  Future<void> _editKeys(List<Map<String, dynamic>> secrets) async {
    final provider = _isHost
        ? asJsonString(_connection?['credentialProvider']) ?? _key.toLowerCase()
        : _key;
    final saved = await showJarvisDialog<bool>(
      context: context,
      builder: (_) => AppKeysDialog(
        http: widget.http,
        serverId: provider,
        appName: _name,
        secrets: secrets,
        verifyServer: _key,
      ),
    );
    if (saved == true && mounted) await _load();
  }

  Future<void> _removeKey(String name, String label) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove $label?',
      message: 'Jarvis can’t use $_name with it until you add it again.',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.key,
    );
    if (!confirmed || !mounted) return;
    await _run(
      () => widget.http.delete<dynamic>(
        '/api/v1/integrations/${Uri.encodeComponent(_key)}/credentials/${Uri.encodeComponent(name)}',
      ),
      'Could not remove the key.',
    );
  }

  Future<void> _signIn() async {
    final outcome = await signInToApp(
      widget.http,
      server: _key,
      endpoint: asJsonString(_server?['endpoint']),
      alive: () => mounted,
    );
    if (!mounted) return;
    switch (outcome.status) {
      case 'needs_token':
        await _editKeys([
          {'name': 'token', 'label': 'Access key', 'required': true},
        ]);
      case 'signed_in':
        await _check();
      case 'pending':
        await _load();
      default:
        setState(() => _error = outcome.message);
    }
  }

  Future<void> _remove() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove $_name?',
      message: _selfBuilt
          ? 'Jarvis stops using $_name and deletes its code and saved keys.'
          : 'Jarvis stops using $_name and forgets its saved keys. You can connect it again later.',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.plugsConnected,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<dynamic>(
        '/api/v1/mcp-servers/${Uri.encodeComponent(_key)}',
      );
      _tiles?.unpin(_tileId);
      if (mounted) Navigator.of(context).pop();
    } on DioException {
      if (mounted) setState(() => _error = 'Could not remove the app.');
    }
  }

  // ------------------------------------------------------------ self-built

  String? get _customId => asJsonString(jsonObject(_custom?['server'])?['id']);

  Future<void> _testDraft() async {
    final id = _customId;
    if (id == null) return;
    setState(() => _testing = true);
    try {
      await widget.http.post<dynamic>(
        '/api/v1/custom-mcp-servers/$id/test',
        data: const <String, dynamic>{},
      );
    } on DioException {
      if (mounted) setState(() => _error = 'Could not test the app.');
    }
    if (!mounted) return;
    setState(() => _testing = false);
    await _load();
  }

  Future<void> _approve() async {
    final server = jsonObject(_custom?['server']) ?? const {};
    final id = _customId;
    if (id == null) return;
    final test = jsonObject(server['lastTest']);
    final tested =
        test?['ok'] == true && test?['sha256'] == server['draftSha256'];
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Switch on ${asJsonString(server['name']) ?? 'this app'}?',
      message: [
        'Jarvis runs this code in a locked-down helper, away from your data. '
            'Each action still asks for your OK unless you allowed it.',
        if (!tested) 'This version has not passed a test yet.',
      ].join(' '),
      confirmLabel: 'Switch on',
      icon: PhosphorIconsRegular.shieldCheck,
    );
    if (!confirmed || !mounted) return;
    await _run(
      () => widget.http.post<dynamic>(
        '/api/v1/custom-mcp-servers/$id/activate',
        data: {'sha256': asJsonString(server['draftSha256'])},
      ),
      'Could not switch the app on.',
    );
  }

  Future<void> _revert(int revision) async {
    final id = _customId;
    if (id == null) return;
    await _run(
      () => widget.http.post<dynamic>(
        '/api/v1/custom-mcp-servers/$id/revert',
        data: {'revision': revision},
      ),
      'Could not go back to that version.',
    );
  }

  void _ask(String prompt) {
    final ask = widget.onAskInChat;
    if (ask == null) return;
    Navigator.of(context).pop();
    ask(prompt);
  }

  // ------------------------------------------------------------------- tiles

  String get _tileId => appTileId(
    _isHost ? '$appTileHostPrefix${widget.hostName}' : widget.serverId!,
    _name,
  );

  void _togglePin() {
    final tiles = _tiles;
    if (tiles == null) return;
    final pinned = tiles.contains(_tileId);
    if (pinned) {
      tiles.unpin(_tileId);
    } else {
      tiles.pin(_tileId, TileSize.square);
    }
    setState(() {});
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(pinned ? 'Removed from Home.' : 'Added to Home.')),
    );
  }

  // -------------------------------------------------------------------- build

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: PageTitle(_loading ? 'App' : _name),
      actions: [
        if (!_loading)
          HeaderAction(
            key: const Key('app-detail-check'),
            label: 'Check',
            icon: PhosphorIconsRegular.pulse,
            busy: _testing,
            onPressed: _testing ? null : _check,
          ),
      ],
    ),
    body: _loading
        ? const SkeletonList()
        : AbsorbPointer(
            absorbing: _busy,
            child: OrbRefresh(
              onRefresh: _load,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 40),
                children: [
                  ContentWidth(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (_busy) const LinearProgressIndicator(),
                        if (_error != null)
                          InlineNotice(
                            message: _error!,
                            tone: NoticeTone.danger,
                            margin: const EdgeInsets.only(bottom: 12),
                          ),
                        _header(context),
                        if (_custom != null) ..._selfBuiltSection(context),
                        ..._keysSection(context),
                        ..._toolsSection(context),
                        const SizedBox(height: 24),
                        _footer(context),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
  );

  Widget _header(BuildContext context) {
    final colors = JarvisColors.of(context);
    final info = appInfo(_name, selfBuilt: _selfBuilt);
    final health = appHealth(context, _connection, enabled: _enabled);
    final calls = asJsonInt(_connection?['callsLast7Days']);
    final lastUsed = jsonDate(_connection?['lastUsedAt'], local: true);
    final tiles = _tiles;
    final pinned = tiles?.contains(_tileId) ?? false;
    final test = _test;
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBadge(icon: info.icon, size: 48),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(_name, style: Theme.of(context).textTheme.titleLarge),
                    const SizedBox(height: 6),
                    Wrap(
                      spacing: 8,
                      runSpacing: 6,
                      children: [
                        StatusPill(label: health.label, color: health.color),
                        if (_selfBuilt)
                          StatusPill(
                            label: 'Built by Jarvis',
                            color: colors.accent,
                          ),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            [?info.what, health.detail].join(' '),
            style: TextStyle(height: 1.45, color: colors.inkSoft),
          ),
          if (calls > 0 || lastUsed != null) ...[
            const SizedBox(height: 6),
            Text(
              [
                if (calls > 0)
                  'Used ${calls == 1 ? 'once' : '$calls times'} this week',
                if (lastUsed != null) 'last ${_ago(lastUsed)}',
              ].join(' · '),
              style: TextStyle(fontSize: 13, color: colors.muted),
            ),
          ],
          if (_testing) ...[
            const SizedBox(height: 12),
            const LinearProgressIndicator(),
            const SizedBox(height: 6),
            Text(
              'Checking the app. A first start can take a minute.',
              style: TextStyle(fontSize: 13, color: colors.inkSoft),
            ),
          ] else if (test != null)
            InlineNotice(
              key: const Key('app-detail-test-result'),
              message: test.message,
              tone: test.ok ? NoticeTone.success : NoticeTone.danger,
              margin: const EdgeInsets.only(top: 12),
            ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              if (health.fix == AppFix.signIn && !_isHost)
                FilledButton(onPressed: _signIn, child: const Text('Sign in')),
              if (health.fix == AppFix.retry)
                FilledButton.tonal(
                  onPressed: _testing ? null : _check,
                  child: const Text('Try again'),
                ),
              if (widget.onAskInChat != null)
                OutlinedButton.icon(
                  onPressed: () => _ask('Use $_name to '),
                  icon: const Icon(PhosphorIconsRegular.chatCircle, size: 18),
                  label: Text('Ask Jarvis'),
                ),
              if (tiles != null)
                OutlinedButton.icon(
                  key: const Key('app-detail-pin'),
                  onPressed: _togglePin,
                  icon: Icon(PhosphorIconsRegular.pushPin, size: 18),
                  label: Text(pinned ? 'On Home' : 'Pin to Home'),
                ),
            ],
          ),
        ],
      ),
    );
  }

  List<Widget> _keysSection(BuildContext context) {
    final colors = JarvisColors.of(context);
    final declared = jsonMaps(_server?['secrets']);
    final remote = asJsonString(_server?['transport']) == 'streamableHttp';
    final hasToken = _server?['hasToken'] == true;
    if (_isHost) {
      final hasKey = _connection?['hasCredentials'] == true;
      return [
        const SizedBox(height: 24),
        const SectionHeader('Access'),
        SurfaceCard(
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
          child: _KeyRow(
            label: 'Access key',
            detail: hasKey ? 'Saved · hidden' : 'Not added yet',
            isSet: hasKey,
            onEdit: () => _editKeys([
              {'name': 'token', 'label': 'Access key', 'required': true},
            ]),
          ),
        ),
      ];
    }
    if (declared.isEmpty && !remote) return const [];
    return [
      const SizedBox(height: 24),
      const SectionHeader('Keys and sign-in'),
      SurfaceCard(
        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
        child: Column(
          children: [
            for (final secret in declared)
              _KeyRow(
                key: Key('app-detail-key-${asJsonString(secret['name'])}'),
                label:
                    asJsonString(secret['label']) ??
                    secretLabel(asJsonString(secret['name']) ?? ''),
                detail: [
                  secret['isSet'] == true
                      ? 'Saved · hidden'
                      : secret['required'] == true
                      ? 'Missing'
                      : 'Optional',
                  ?asJsonString(secret['description']),
                ].join(' · '),
                isSet: secret['isSet'] == true,
                onEdit: () => _editKeys([secret]),
                onRemove: secret['isSet'] == true
                    ? () => _removeKey(
                        asJsonString(secret['name']) ?? '',
                        asJsonString(secret['label']) ?? 'this key',
                      )
                    : null,
              ),
            if (remote && declared.isEmpty)
              _KeyRow(
                label: 'Sign-in',
                detail: hasToken
                    ? 'Signed in. Jarvis renews it when it can.'
                    : 'Not signed in',
                isSet: hasToken,
                editLabel: hasToken ? 'Sign in again' : 'Sign in',
                onEdit: _signIn,
                onRemove: hasToken
                    ? () => _removeKey('token', 'the sign-in')
                    : null,
              ),
          ],
        ),
      ),
      Padding(
        padding: const EdgeInsets.fromLTRB(4, 8, 4, 0),
        child: Text(
          'Keys are stored encrypted, only given to this app, and never shown again.',
          style: TextStyle(fontSize: 12.5, color: colors.muted),
        ),
      ),
    ];
  }

  List<Widget> _toolsSection(BuildContext context) {
    final colors = JarvisColors.of(context);
    final available = jsonStrings(_connection?['tools']);
    final configured = _isHost
        ? jsonStrings(_connection?['configuredTools'])
        : jsonStrings(_server?['allowedTools']);
    final readOnly = jsonStrings(_connection?['readOnlyTools']).toSet();
    final allTools = configured.length == 1 && configured.first == '*';
    final tools = {
      ...available,
      ...configured.where((tool) => tool != '*'),
    }.toList()..sort();
    return [
      const SizedBox(height: 24),
      SectionHeader(
        'What Jarvis may use',
        trailing: allTools || tools.isEmpty
            ? null
            : TextButton(
                onPressed: () => _setTools('replace', const ['*']),
                child: const Text('Allow all'),
              ),
      ),
      if (tools.isEmpty)
        Text(
          allTools
              ? 'Everything this app offers. Jarvis lists the actions once it connects.'
              : 'Jarvis lists this app’s actions once it connects.',
          style: TextStyle(color: colors.inkSoft),
        )
      else
        SurfaceCard(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Column(
            children: [
              for (final tool in tools)
                SwitchListTile(
                  key: Key('app-tool-$tool'),
                  dense: true,
                  value: allTools || configured.contains(tool),
                  title: Text(humanizeAppTool(tool, appName: _name)),
                  subtitle: Text(
                    readOnly.contains(tool)
                        ? 'Only reads · can run without asking'
                        : 'Asks for your OK each time',
                    style: TextStyle(fontSize: 12, color: colors.muted),
                  ),
                  onChanged: (on) {
                    if (on) {
                      _setTools('add', [tool]);
                    } else if (allTools) {
                      _setTools(
                        'replace',
                        tools.where((other) => other != tool).toList(),
                      );
                    } else {
                      _setTools('remove', [tool]);
                    }
                  },
                ),
            ],
          ),
        ),
    ];
  }

  List<Widget> _selfBuiltSection(BuildContext context) {
    final colors = JarvisColors.of(context);
    final server = jsonObject(_custom?['server']) ?? const {};
    final draft = asJsonString(_custom?['draftSource']) ?? '';
    final revision = asJsonInt(server['revision']);
    final activeRevision = server['activeRevision'] is int
        ? server['activeRevision'] as int
        : null;
    final waiting = server['draftSha256'] != server['activeSha256'];
    final test = jsonObject(server['lastTest']);
    final testedDraft = test != null && test['sha256'] == server['draftSha256'];
    final revisions = jsonMaps(_custom?['revisions']);
    final description = asJsonString(server['description']);
    return [
      const SizedBox(height: 24),
      const SectionHeader('Built by Jarvis'),
      SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (description != null) ...[
              Text(description, style: TextStyle(color: colors.inkSoft)),
              const SizedBox(height: 10),
            ],
            Text(
              waiting
                  ? activeRevision == null
                        ? 'Version $revision is waiting for your OK.'
                        : 'Version $revision is waiting for your OK. Version $activeRevision stays live until then.'
                  : 'Version $revision is live.',
              style: const TextStyle(fontWeight: FontWeight.w500),
            ),
            if (testedDraft) ...[
              const SizedBox(height: 8),
              InlineNotice(
                tone: test['ok'] == true
                    ? NoticeTone.success
                    : NoticeTone.danger,
                message: test['ok'] == true
                    ? 'Test passed: ${jsonStrings(test['tools']).map((tool) => humanizeAppTool(tool, appName: _name)).join(', ')}.'
                    : 'Test failed: ${asJsonString(test['error']) ?? 'it did not start.'}',
              ),
              if (asJsonString(test['diagnostics']) case final diagnostics?)
                _CodeBlock(
                  title: 'What the app printed',
                  code: diagnostics,
                  initiallyExpanded: test['ok'] != true,
                ),
            ],
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                if (waiting)
                  FilledButton(
                    key: const Key('app-detail-approve'),
                    onPressed: _approve,
                    child: Text(
                      activeRevision == null ? 'Switch on' : 'Use this version',
                    ),
                  ),
                OutlinedButton.icon(
                  key: const Key('app-detail-test-draft'),
                  onPressed: _testing ? null : _testDraft,
                  icon: const Icon(PhosphorIconsRegular.play, size: 18),
                  label: Text(testedDraft ? 'Test again' : 'Test it'),
                ),
                if (widget.onAskInChat != null)
                  OutlinedButton.icon(
                    onPressed: () =>
                        _ask('Change the $_name app you built for me so that '),
                    icon: const Icon(PhosphorIconsRegular.magicWand, size: 18),
                    label: const Text('Ask for a change'),
                  ),
              ],
            ),
            _CodeBlock(title: 'Code · version $revision', code: draft),
            if (revisions.length > 1)
              Theme(
                data: Theme.of(
                  context,
                ).copyWith(dividerColor: Colors.transparent),
                child: ExpansionTile(
                  tilePadding: EdgeInsets.zero,
                  shape: const Border(),
                  collapsedShape: const Border(),
                  title: Text(
                    'Earlier versions',
                    style: TextStyle(fontSize: 14, color: colors.inkSoft),
                  ),
                  children: [
                    for (final item in revisions)
                      ListTile(
                        dense: true,
                        contentPadding: EdgeInsets.zero,
                        title: Text(
                          'Version ${asJsonInt(item['revision'])}'
                          '${item['active'] == true ? ' · live' : ''}',
                        ),
                        subtitle: Text(
                          _ago(
                            jsonDate(item['createdAt'], local: true) ??
                                DateTime.now(),
                          ),
                        ),
                        trailing: asJsonInt(item['revision']) == revision
                            ? null
                            : TextButton(
                                onPressed: () =>
                                    _revert(asJsonInt(item['revision'])),
                                child: const Text('Go back to this'),
                              ),
                      ),
                  ],
                ),
              ),
          ],
        ),
      ),
    ];
  }

  Widget _footer(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SurfaceCard(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
      child: Column(
        children: [
          SwitchListTile(
            key: const Key('app-detail-enabled'),
            value: _enabled,
            title: Text('Let Jarvis use $_name'),
            subtitle: const Text('Pausing keeps its keys and settings.'),
            onChanged: _connection == null && _server == null
                ? null
                : _setEnabled,
          ),
          if (!_isHost)
            ListTile(
              key: const Key('app-detail-remove'),
              leading: Icon(PhosphorIconsRegular.trash, color: colors.danger),
              title: Text(
                'Remove $_name',
                style: TextStyle(color: colors.danger),
              ),
              onTap: _remove,
            ),
        ],
      ),
    );
  }
}

String _ago(DateTime time) {
  final gap = DateTime.now().difference(time);
  if (gap.inMinutes < 1) return 'just now';
  if (gap.inHours < 1) return '${gap.inMinutes} min ago';
  if (gap.inDays < 1) return '${gap.inHours} h ago';
  if (gap.inDays == 1) return 'yesterday';
  return '${gap.inDays} days ago';
}

class _KeyRow extends StatelessWidget {
  const _KeyRow({
    required this.label,
    required this.detail,
    required this.isSet,
    required this.onEdit,
    this.onRemove,
    this.editLabel,
    super.key,
  });

  final String label;
  final String detail;
  final bool isSet;
  final VoidCallback onEdit;
  final VoidCallback? onRemove;
  final String? editLabel;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return ListTile(
      leading: Icon(
        isSet ? PhosphorIconsRegular.checkCircle : PhosphorIconsRegular.key,
        color: isSet ? colors.success : colors.warning,
      ),
      title: Text(label),
      subtitle: Text(detail, maxLines: 3, overflow: TextOverflow.ellipsis),
      trailing: Wrap(
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          TextButton(
            onPressed: onEdit,
            child: Text(editLabel ?? (isSet ? 'Replace' : 'Add')),
          ),
          if (onRemove != null)
            IconButton(
              tooltip: 'Remove',
              onPressed: onRemove,
              icon: const Icon(PhosphorIconsRegular.minusCircle, size: 19),
            ),
        ],
      ),
    );
  }
}

/// Monospace text that can be read and copied, folded by default.
class _CodeBlock extends StatelessWidget {
  const _CodeBlock({
    required this.title,
    required this.code,
    this.initiallyExpanded = false,
  });

  final String title;
  final String code;
  final bool initiallyExpanded;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Theme(
      data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
      child: ExpansionTile(
        tilePadding: EdgeInsets.zero,
        shape: const Border(),
        collapsedShape: const Border(),
        initiallyExpanded: initiallyExpanded,
        title: Text(
          title,
          style: TextStyle(fontSize: 14, color: colors.inkSoft),
        ),
        children: [
          Container(
            width: double.infinity,
            constraints: const BoxConstraints(maxHeight: 420),
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: colors.surfaceMuted,
              borderRadius: BorderRadius.circular(10),
            ),
            child: SingleChildScrollView(
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: SelectableText(
                  code,
                  style: TextStyle(
                    fontFamily: 'monospace',
                    fontSize: 12,
                    height: 1.4,
                    color: colors.ink,
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
