import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What happened after an app was added, so the caller can start sign-in.
typedef AppInstallOutcome = ({
  String serverId,
  String name,
  String endpoint,
  String nextStep,
});

/// Find an app by name and add it in one tap, or paste its address.
class AppCatalogScreen extends StatefulWidget {
  const AppCatalogScreen({
    required this.http,
    this.startWithAddress = false,
    super.key,
  });

  final Dio http;

  /// Opens the "paste an address" dialog as soon as the page shows.
  final bool startWithAddress;

  @override
  State<AppCatalogScreen> createState() => _AppCatalogScreenState();
}

const _suggestedSearches = [
  'Notion',
  'Linear',
  'GitHub',
  'Web search',
  'Slack',
  'Google Drive',
];

class _AppCatalogScreenState extends State<AppCatalogScreen> {
  final _search = TextEditingController();
  Timer? _debounce;
  List<Map<String, dynamic>> _results = [];
  bool _searching = false;
  bool _busy = false;
  String? _error;
  String _lastQuery = '';
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    if (widget.startWithAddress) afterRouteSettles(this, _pasteAddress);
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  void _onChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(
      const Duration(milliseconds: 350),
      () => _runSearch(value),
    );
  }

  Future<void> _runSearch(String value) async {
    final query = value.trim();
    if (query == _lastQuery && _error == null) return;
    _lastQuery = query;
    final revision = ++_revision;
    if (query.isEmpty) {
      setState(() {
        _results = [];
        _searching = false;
        _error = null;
      });
      return;
    }
    setState(() {
      _searching = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/mcp-catalog',
        queryParameters: {'search': query, 'limit': 20},
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _results = jsonMaps(jsonObject(response.data)?['servers']);
        _searching = false;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _searching = false;
        _error =
            asJsonString(jsonObject(error.response?.data)?['message']) ??
            'Jarvis could not search for apps right now.';
      });
    }
  }

  void _searchFor(String text) {
    _search.text = text;
    _search.selection = TextSelection.collapsed(offset: text.length);
    _debounce?.cancel();
    _runSearch(text);
  }

  Future<void> _openEntry(Map<String, dynamic> entry) async {
    final options = jsonMaps(entry['options']);
    if (options.isEmpty) return;
    final optionId = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (_) => _AppDetailsSheet(entry: entry, options: options),
    );
    if (optionId == null || !mounted) return;
    await _install(
      () => widget.http.post<dynamic>(
        '/api/v1/mcp-catalog/install',
        data: {'name': asJsonString(entry['name']), 'option': optionId},
      ),
    );
  }

  Future<void> _pasteAddress() async {
    final endpoint = await showDialog<String>(
      context: context,
      builder: (_) => const _AddressDialog(),
    );
    if (endpoint == null || !mounted) return;
    await _install(
      () => widget.http.post<dynamic>(
        '/api/v1/mcp-servers/connect',
        data: {'endpoint': endpoint},
      ),
    );
  }

  Future<void> _install(Future<Response<dynamic>> Function() request) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    Map<String, dynamic> result;
    try {
      result = jsonObject((await request()).data) ?? const {};
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            asJsonString(jsonObject(error.response?.data)?['message']) ??
            'Jarvis could not add that app. Try again.';
      });
      return;
    }
    if (!mounted) return;
    setState(() => _busy = false);
    final server = jsonObject(result['server']) ?? const {};
    final serverId = asJsonString(server['id']) ?? '';
    var nextStep = asJsonString(result['nextStep']) ?? 'ready';
    final secrets = jsonMaps(server['secrets']);
    if (nextStep == 'secrets' && serverId.isNotEmpty && secrets.isNotEmpty) {
      final saved = await showDialog<bool>(
        context: context,
        barrierDismissible: false,
        builder: (_) => AppKeysDialog(
          http: widget.http,
          serverId: serverId,
          appName: asJsonString(server['name']) ?? 'this app',
          secrets: secrets,
        ),
      );
      if (saved == true) nextStep = 'ready';
    }
    if (!mounted) return;
    Navigator.of(context).pop<AppInstallOutcome>((
      serverId: serverId,
      name: asJsonString(server['name']) ?? 'The app',
      endpoint: asJsonString(server['endpoint']) ?? '',
      nextStep: nextStep,
    ));
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final query = _search.text.trim();
    return Scaffold(
      appBar: AppBar(
        title: const Text('Browse apps'),
        actions: [
          HeaderAction(
            label: 'Paste address',
            icon: PhosphorIconsRegular.linkSimple,
            onPressed: _busy ? null : _pasteAddress,
          ),
        ],
      ),
      body: AbsorbPointer(
        absorbing: _busy,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 40),
          children: [
            ContentWidth(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  TextField(
                    key: const Key('app-catalog-search'),
                    controller: _search,
                    autofocus: !widget.startWithAddress,
                    textInputAction: TextInputAction.search,
                    onChanged: _onChanged,
                    onSubmitted: (value) {
                      _debounce?.cancel();
                      _runSearch(value);
                    },
                    decoration: const InputDecoration(
                      prefixIcon: Icon(PhosphorIconsRegular.magnifyingGlass),
                      hintText: 'Search apps, like Notion or web search',
                    ),
                  ),
                  if (_busy) ...[
                    const SizedBox(height: 16),
                    const LinearProgressIndicator(),
                    const SizedBox(height: 8),
                    Text(
                      'Adding the app…',
                      style: TextStyle(color: colors.inkSoft),
                    ),
                  ],
                  if (_error != null)
                    InlineNotice(
                      message: _error!,
                      tone: NoticeTone.danger,
                      margin: const EdgeInsets.only(top: 16),
                    ),
                  const SizedBox(height: 16),
                  if (query.isEmpty) ...[
                    Text(
                      'Popular',
                      style: TextStyle(
                        fontWeight: FontWeight.w600,
                        color: colors.inkSoft,
                      ),
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        for (final suggestion in _suggestedSearches)
                          ActionChip(
                            label: Text(suggestion),
                            onPressed: () => _searchFor(suggestion),
                          ),
                      ],
                    ),
                    const SizedBox(height: 20),
                    Text(
                      'Apps come from the public app directory. Each app asks for '
                      'your OK before Jarvis acts in it.',
                      style: TextStyle(
                        fontSize: 13.5,
                        height: 1.4,
                        color: colors.muted,
                      ),
                    ),
                  ] else if (_searching)
                    const Padding(
                      padding: EdgeInsets.only(top: 24),
                      child: Center(child: CircularProgressIndicator()),
                    )
                  else if (_results.isEmpty && _error == null)
                    Padding(
                      padding: const EdgeInsets.only(top: 16),
                      child: Text(
                        'No apps found for “$query”. Try another name, or paste '
                        'the app’s address.',
                        style: TextStyle(color: colors.inkSoft),
                      ),
                    )
                  else
                    for (final entry in _results)
                      _AppResultTile(
                        entry: entry,
                        onTap: () => _openEntry(entry),
                      ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Where an install option runs, in words an owner understands.
String _whereItRuns(String kind) => switch (kind) {
  'remote' => 'Online',
  'npm' || 'pypi' => 'Runs on your Jarvis server',
  _ => kind,
};

class _AppResultTile extends StatelessWidget {
  const _AppResultTile({required this.entry, required this.onTap});

  final Map<String, dynamic> entry;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final options = jsonMaps(entry['options']);
    final kind = options.isEmpty
        ? ''
        : asJsonString(options.first['kind']) ?? '';
    final needsKey =
        options.isNotEmpty &&
        jsonMaps(options.first['secrets']).any((s) => s['required'] == true);
    final description = asJsonString(entry['description']);
    return ListTile(
      key: Key('catalog-${asJsonString(entry['name'])}'),
      contentPadding: const EdgeInsets.symmetric(horizontal: 4, vertical: 4),
      leading: const IconBadge(icon: PhosphorIconsRegular.puzzlePiece),
      title: Text(asJsonString(entry['title']) ?? 'App'),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (description != null)
            Text(description, maxLines: 2, overflow: TextOverflow.ellipsis),
          const SizedBox(height: 4),
          Text(
            [
              _whereItRuns(kind),
              if (needsKey) 'needs a key',
              asJsonString(entry['name']) ?? '',
            ].join(' · '),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 12, color: colors.muted),
          ),
        ],
      ),
      onTap: onTap,
    );
  }
}

class _AppDetailsSheet extends StatefulWidget {
  const _AppDetailsSheet({required this.entry, required this.options});

  final Map<String, dynamic> entry;
  final List<Map<String, dynamic>> options;

  @override
  State<_AppDetailsSheet> createState() => _AppDetailsSheetState();
}

class _AppDetailsSheetState extends State<_AppDetailsSheet> {
  late String _option = asJsonString(widget.options.first['id']) ?? '';

  Map<String, dynamic> get _selected => widget.options.firstWhere(
    (option) => asJsonString(option['id']) == _option,
    orElse: () => widget.options.first,
  );

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final selected = _selected;
    final kind = asJsonString(selected['kind']) ?? '';
    final summary = asJsonString(selected['summary']) ?? '';
    final secrets = jsonMaps(selected['secrets']);
    final description = asJsonString(widget.entry['description']);
    final host = Uri.tryParse(summary)?.host ?? summary;
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(24, 0, 24, 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              asJsonString(widget.entry['title']) ?? 'App',
              style: theme.textTheme.titleLarge,
            ),
            if (description != null) ...[
              const SizedBox(height: 8),
              Text(description, style: TextStyle(color: colors.inkSoft)),
            ],
            if (widget.options.length > 1) ...[
              const SizedBox(height: 16),
              Text(
                'How to run it',
                style: TextStyle(
                  fontWeight: FontWeight.w600,
                  color: colors.ink,
                ),
              ),
              const SizedBox(height: 4),
              RadioGroup<String>(
                groupValue: _option,
                onChanged: (value) =>
                    setState(() => _option = value ?? _option),
                child: Column(
                  children: [
                    for (final option in widget.options)
                      RadioListTile<String>(
                        contentPadding: EdgeInsets.zero,
                        value: asJsonString(option['id']) ?? '',
                        title: Text(
                          _whereItRuns(asJsonString(option['kind']) ?? ''),
                        ),
                        subtitle: Text(
                          asJsonString(option['summary']) ?? '',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                ),
              ),
            ],
            const SizedBox(height: 16),
            _Fact(
              icon: kind == 'remote'
                  ? PhosphorIconsRegular.globe
                  : PhosphorIconsRegular.terminalWindow,
              text: kind == 'remote'
                  ? 'Jarvis connects to $host. You may be asked to sign in.'
                  : 'Jarvis downloads $summary and runs it on your Jarvis server.',
            ),
            _Fact(
              icon: PhosphorIconsRegular.shieldCheck,
              text: 'Jarvis asks for your OK before each action in this app.',
            ),
            if (secrets.isNotEmpty)
              _Fact(
                icon: PhosphorIconsRegular.key,
                text:
                    'You’ll paste ${secrets.length == 1 ? 'a key' : '${secrets.length} keys'} next: '
                    '${secrets.map((s) => asJsonString(s['label']) ?? asJsonString(s['name'])).join(', ')}.',
              ),
            const SizedBox(height: 20),
            FilledButton(
              key: const Key('app-catalog-connect'),
              onPressed: () => Navigator.of(context).pop(_option),
              child: const Text('Connect'),
            ),
          ],
        ),
      ),
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 10),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 18, color: JarvisColors.of(context).inkSoft),
        const SizedBox(width: 10),
        Expanded(child: Text(text)),
      ],
    ),
  );
}

class _AddressDialog extends StatefulWidget {
  const _AddressDialog();

  @override
  State<_AddressDialog> createState() => _AddressDialogState();
}

class _AddressDialogState extends State<_AddressDialog> {
  final _controller = TextEditingController();
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _submit() {
    final value = _controller.text.trim();
    final uri = Uri.tryParse(value);
    if (uri == null || uri.scheme != 'https' || uri.host.isEmpty) {
      setState(() => _error = 'Paste an address that starts with https://');
      return;
    }
    Navigator.of(context).pop(value);
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Paste an app’s address'),
    content: SizedBox(
      width: 420,
      child: TextField(
        key: const Key('app-address'),
        controller: _controller,
        autofocus: true,
        autocorrect: false,
        keyboardType: TextInputType.url,
        onSubmitted: (_) => _submit(),
        decoration: InputDecoration(
          hintText: 'https://mcp.example.com/mcp',
          helperText: 'The app’s setup page lists this address.',
          errorText: _error,
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.of(context).pop(),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _submit, child: const Text('Connect')),
    ],
  );
}

/// Asks for every key an app needs, in one dialog, and saves them encrypted.
class AppKeysDialog extends StatefulWidget {
  const AppKeysDialog({
    required this.http,
    required this.serverId,
    required this.appName,
    required this.secrets,
    super.key,
  });

  final Dio http;
  final String serverId;
  final String appName;
  final List<Map<String, dynamic>> secrets;

  @override
  State<AppKeysDialog> createState() => _AppKeysDialogState();
}

class _AppKeysDialogState extends State<AppKeysDialog> {
  late final Map<String, TextEditingController> _fields = {
    for (final secret in widget.secrets)
      asJsonString(secret['name']) ?? '': TextEditingController(),
  };
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    for (final field in _fields.values) {
      field.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    final missing = widget.secrets.where(
      (secret) =>
          secret['required'] == true &&
          secret['isSet'] != true &&
          _fields[asJsonString(secret['name'])]!.text.trim().isEmpty,
    );
    if (missing.isNotEmpty) {
      setState(
        () => _error =
            'Fill in ${asJsonString(missing.first['label']) ?? 'every required key'}.',
      );
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      for (final MapEntry(key: name, value: field) in _fields.entries) {
        final value = field.text.trim();
        if (value.isEmpty) continue;
        await widget.http.put<dynamic>(
          '/api/v1/integrations/${Uri.encodeComponent(widget.serverId)}/credentials/${Uri.encodeComponent(name)}',
          data: {'value': value},
        );
      }
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not save those keys. Try again.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return AlertDialog(
      title: Text('Keys for ${widget.appName}'),
      content: SizedBox(
        width: 440,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                'Jarvis stores these encrypted and only gives them to this app.',
                style: TextStyle(color: colors.inkSoft),
              ),
              for (final secret in widget.secrets) ...[
                const SizedBox(height: 14),
                TextField(
                  key: Key('app-key-${asJsonString(secret['name'])}'),
                  controller: _fields[asJsonString(secret['name'])],
                  obscureText: true,
                  autocorrect: false,
                  enableSuggestions: false,
                  decoration: InputDecoration(
                    labelText:
                        '${asJsonString(secret['label']) ?? asJsonString(secret['name'])}'
                        '${secret['required'] == true ? '' : ' (optional)'}',
                    helperText: asJsonString(secret['description']),
                    helperMaxLines: 3,
                    hintText: secret['isSet'] == true
                        ? 'Saved — leave empty to keep'
                        : null,
                  ),
                ),
              ],
              if (_error != null) ...[
                const SizedBox(height: 12),
                Text(_error!, style: TextStyle(color: colors.danger)),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _saving ? null : () => Navigator.of(context).pop(false),
          child: const Text('Later'),
        ),
        FilledButton(
          key: const Key('app-keys-save'),
          onPressed: _saving ? null : _save,
          child: const Text('Save'),
        ),
      ],
    );
  }
}
