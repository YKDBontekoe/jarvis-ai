part of 'integrations_screen.dart';

mixin _IntegrationsPacks on _IntegrationsController {
  /// Apps Jarvis knows how to set up, each with one plain line and one tap.
  Widget _suggestions() {
    final rows = <Widget>[
      for (final status in _packs) _packRow(status),
      _featuredRow(
        provider: 'home-assistant',
        steps:
            'Whoever runs your Jarvis server turns on Home Assistant first. '
            'Then create a long-lived access token in your Home Assistant profile and paste it here.',
      ),
      _featuredRow(
        provider: 'github',
        steps:
            'Whoever runs your Jarvis server turns on GitHub first. '
            'Then create a personal access token on GitHub with only the access Jarvis needs, and paste it here.',
      ),
      if (widget.onAskInChat != null)
        _SuggestionRow(
          icon: PhosphorIconsRegular.chatCircle,
          title: 'Something else',
          subtitle:
              'Tell Jarvis which app, and it sets it up with you in chat.',
          trailing: Icon(
            PhosphorIconsRegular.caretRight,
            size: 16,
            color: JarvisColors.of(context).muted,
          ),
          onTap: () => _askInChat(mcpSetupPrompt),
        ),
    ];
    return GroupedSection(dividerIndent: 68, children: rows);
  }

  Widget _packRow(Map<String, dynamic> status) {
    final pack = jsonObject(status['pack']) ?? status;
    final id = asJsonString(pack['id']) ?? '';
    final info = _appInfo(asJsonString(pack['name']) ?? id);
    final installed = asJsonBool(status['installed']);
    return _SuggestionRow(
      key: Key('suggest-$id'),
      icon: info.icon,
      title: info.name,
      subtitle: info.what ?? asJsonString(pack['description']) ?? '',
      trailing: installed
          ? const _AddedLabel()
          : _SetUpButton(
              onPressed: id.isEmpty ? null : () => _installPack(status),
            ),
      onTap: id.isEmpty ? null : () => _installPack(status),
    );
  }

  Future<void> _installPack(Map<String, dynamic> status) async {
    final pack = jsonObject(status['pack']) ?? status;
    final id = asJsonString(pack['id']);
    if (id == null) return;
    final supportsIcs = asJsonBool(pack['supportsIcs']);
    final saved = await showDialog<_PackSetupValues>(
      context: context,
      builder: (_) => _PackSetupDialog(
        name: asJsonString(pack['name']) ?? 'pack',
        supportsIcs: supportsIcs,
        suggestedEndpoint: asJsonString(pack['suggestedEndpoint']) ?? '',
      ),
    );
    if (saved == null || !mounted) return;
    if (supportsIcs &&
        saved.icsUrl.isNotEmpty &&
        parsePublicHttpsUrl(saved.icsUrl) == null) {
      setState(
        () => _error =
            'That calendar link doesn’t work. Use the secret address that starts with https:// and ends in .ics.',
      );
      return;
    }
    try {
      await widget.http.post<void>(
        '/api/v1/integrations/packs/$id',
        data: {
          if (saved.icsUrl.isNotEmpty) 'icsUrl': saved.icsUrl,
          if (saved.icsToken.isNotEmpty) 'icsToken': saved.icsToken,
          if (saved.mcpEndpoint.isNotEmpty) 'endpoint': saved.mcpEndpoint,
        },
      );
      if (mounted) await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not set that up. Try again, or ask Jarvis in chat.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => _error =
              'Could not set that up. Try again, or ask Jarvis in chat.',
        );
      }
    }
  }

  @override
  Future<void> _connectOAuth({String? server, String? endpoint}) async {
    final target = (server ?? '').trim();
    final url = (endpoint ?? '').trim();
    if (target.isEmpty && url.isEmpty) {
      setState(
        () => _error =
            'Jarvis doesn’t know where to sign in for this app yet. Ask Jarvis in chat to set it up.',
      );
      return;
    }
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/integrations/oauth/sessions',
        data: {
          if (target.isNotEmpty) 'server': target,
          if (target.isEmpty && url.isNotEmpty) 'endpoint': url,
        },
      );
      final body = jsonObject(response.data) ?? const {};
      final status = asJsonString(body['status']);
      final authorize = parseHttpUrl(asJsonString(body['authorizationUrl']));
      if (status == 'needs_token') {
        await _editSecret(
          provider: target.isNotEmpty ? target : 'jarvis-mcp-discovery',
          secretName: 'token',
        );
        return;
      }
      if (authorize == null) {
        setState(
          () => _error =
              'This app has no sign-in page. Use an access key instead.',
        );
        return;
      }
      final opened = await launchHttpUrl(authorize);
      if (!opened && mounted) {
        setState(() => _error = 'Could not open the sign-in page.');
        return;
      }
      final id = asJsonString(body['id']);
      if (id == null ||
          id.isEmpty ||
          id == '00000000-0000-0000-0000-000000000000') {
        if (mounted) await _load();
        return;
      }
      for (var i = 0; i < 45; i++) {
        await Future<void>.delayed(const Duration(seconds: 2));
        if (!mounted) return;
        try {
          final poll = await widget.http.get<dynamic>(
            '/api/v1/integrations/oauth/sessions/$id',
          );
          final session = jsonObject(poll.data);
          final state = asJsonString(session?['status']);
          if (state == 'completed') {
            if (mounted) {
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(
                  content: Text('Signed in. Jarvis can use it now.'),
                ),
              );
              await _load();
            }
            return;
          }
          if (state == 'failed') {
            setState(
              () => _error =
                  asJsonString(session?['error']) ??
                  'Sign-in didn’t finish. You can use an access key instead.',
            );
            return;
          }
        } catch (_) {
          break;
        }
      }
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not start sign-in.',
        );
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not start sign-in.');
    }
  }
}

class _PackSetupValues {
  const _PackSetupValues({
    required this.icsUrl,
    required this.icsToken,
    required this.mcpEndpoint,
  });

  final String icsUrl;
  final String icsToken;
  final String mcpEndpoint;
}

class _PackSetupDialog extends StatefulWidget {
  const _PackSetupDialog({
    required this.name,
    required this.supportsIcs,
    required this.suggestedEndpoint,
  });

  final String name;
  final bool supportsIcs;
  final String suggestedEndpoint;

  @override
  State<_PackSetupDialog> createState() => _PackSetupDialogState();
}

class _PackSetupDialogState extends State<_PackSetupDialog> {
  final _ics = TextEditingController();
  final _token = TextEditingController();
  late final _endpoint = TextEditingController(text: widget.suggestedEndpoint);

  @override
  void dispose() {
    _ics.dispose();
    _token.dispose();
    _endpoint.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text('Set up ${widget.name}'),
    content: SizedBox(
      width: 480,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (widget.supportsIcs) ...[
              TextFormField(
                controller: _ics,
                keyboardType: TextInputType.url,
                decoration: const InputDecoration(
                  labelText: 'Calendar link',
                  hintText: 'https://…/basic.ics',
                ),
              ),
              const SizedBox(height: 8),
              TextFormField(
                controller: _token,
                obscureText: true,
                decoration: const InputDecoration(
                  labelText: 'Link password (if it has one)',
                ),
              ),
              const SizedBox(height: 8),
            ],
            TextFormField(
              controller: _endpoint,
              keyboardType: TextInputType.url,
              decoration: InputDecoration(
                labelText: widget.supportsIcs
                    ? 'Server address, to add events (optional)'
                    : 'Server address (optional)',
              ),
            ),
            const SizedBox(height: 12),
            Text(
              widget.supportsIcs
                  ? 'Find the secret calendar address (it ends in .ics) in your calendar’s settings. That is enough to show today’s events on Home. Only add a server address if Jarvis should also create events.'
                  : 'Leave the address empty and Jarvis installs the standard connector for you. You sign in next. Never paste passwords into chat.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(
          context,
          _PackSetupValues(
            icsUrl: _ics.text.trim(),
            icsToken: _token.text.trim(),
            mcpEndpoint: _endpoint.text.trim(),
          ),
        ),
        child: const Text('Save'),
      ),
    ],
  );
}

class _SuggestionRow extends StatelessWidget {
  const _SuggestionRow({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.trailing,
    required this.onTap,
    super.key,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final Widget trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => InkWell(
    onTap: onTap,
    child: Padding(
      padding: const EdgeInsets.fromLTRB(16, 14, 12, 14),
      child: Row(
        children: [
          IconBadge(icon: icon, size: 38),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 2),
                Text(
                  subtitle,
                  style: TextStyle(
                    fontSize: 13,
                    height: 1.35,
                    color: JarvisColors.of(context).inkSoft,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          trailing,
        ],
      ),
    ),
  );
}

class _SetUpButton extends StatelessWidget {
  const _SetUpButton({required this.onPressed});

  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) => FilledButton.tonal(
    onPressed: onPressed,
    style: FilledButton.styleFrom(
      minimumSize: const Size(0, 34),
      padding: const EdgeInsets.symmetric(horizontal: 14),
      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
      backgroundColor: JarvisColors.of(context).surfaceRaised,
      foregroundColor: JarvisColors.of(context).ink,
    ),
    child: const Text('Set up'),
  );
}

class _AddedLabel extends StatelessWidget {
  const _AddedLabel();

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Icon(
        PhosphorIconsRegular.checkCircle,
        size: 16,
        color: JarvisColors.of(context).success,
      ),
      const SizedBox(width: 4),
      Text(
        'Added',
        style: TextStyle(
          fontSize: 13,
          fontWeight: FontWeight.w500,
          color: JarvisColors.of(context).inkSoft,
        ),
      ),
    ],
  );
}
