part of 'channels_screen.dart';

class ChannelEditorSheet extends StatefulWidget {
  const ChannelEditorSheet({required this.http, required this.kind, super.key});

  final Dio http;
  final String kind;

  @override
  State<ChannelEditorSheet> createState() => _ChannelEditorSheetState();
}

class _ChannelEditorSheetState extends State<ChannelEditorSheet> {
  final _name = TextEditingController();
  final _account = TextEditingController();
  final _senders = TextEditingController();
  final _notify = TextEditingController();
  final _accessToken = TextEditingController();
  final _appSecret = TextEditingController();
  final _verifyToken = TextEditingController();
  var _enabled = true;
  var _forward = true;
  var _categories = defaultChannelNotificationCategories();
  var _saving = false;
  String? _error;

  bool get _whatsapp => widget.kind == 'whatsapp';

  @override
  void initState() {
    super.initState();
    _name.text = _whatsapp ? 'WhatsApp' : 'Signal';
  }

  @override
  void dispose() {
    _name.dispose();
    _account.dispose();
    _senders.dispose();
    _notify.dispose();
    _accessToken.dispose();
    _appSecret.dispose();
    _verifyToken.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final senders = _senders.text
        .split(RegExp(r'[\n,]'))
        .map((value) => value.trim())
        .where((value) => value.isNotEmpty)
        .toList();
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await widget.http.post<dynamic>(
        '/api/v1/channels',
        data: {
          'kind': widget.kind,
          'displayName': _name.text.trim(),
          'account': _account.text.trim(),
          'enabled': _enabled,
          'allowedSenders': senders,
          'forwardNotifications': _forward,
          'notificationCategories': _categories.toList(),
          'notifyRecipient': _notify.text.trim().isEmpty
              ? null
              : _notify.text.trim(),
          if (_whatsapp)
            'secrets': {
              'access_token': _accessToken.text.trim(),
              'app_secret': _appSecret.text.trim(),
              'verify_token': _verifyToken.text.trim(),
            },
        },
      );
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not connect this channel.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not connect this channel.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final kind = channelKind(widget.kind);
    final inset = MediaQuery.viewInsetsOf(context).bottom;
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 16, 20, 20 + inset),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Connect ${kind.label}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 6),
            Text(
              _whatsapp
                  ? 'Use the phone number ID, access token, app secret, and verify token from Meta Developer → WhatsApp → API Setup.'
                  : 'Enter the Signal account number that signal-cli is registered as, then allow the phones that may talk to Jarvis.',
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: JarvisColors.of(context).inkSoft),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _name,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Display name'),
            ),
            const SizedBox(height: 10),
            TextField(
              key: const Key('channel-account'),
              controller: _account,
              keyboardType: TextInputType.phone,
              decoration: InputDecoration(
                labelText: _whatsapp
                    ? 'WhatsApp phone number ID'
                    : 'Signal account number',
                hintText: _whatsapp ? '106540352242922' : '+31612345678',
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              key: const Key('channel-senders'),
              controller: _senders,
              minLines: 2,
              maxLines: 4,
              decoration: const InputDecoration(
                labelText: 'Allowed phone numbers',
                hintText: '+31612345678',
                helperText: 'One international number per line. Only these can talk to Jarvis.',
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _notify,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(
                labelText: 'Notification number (optional)',
                helperText: 'Must be one of the allowed numbers.',
              ),
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Enabled'),
              value: _enabled,
              onChanged: (value) => setState(() => _enabled = value),
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Forward Jarvis notifications'),
              value: _forward,
              onChanged: (value) => setState(() => _forward = value),
            ),
            if (_forward)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: ChannelNotificationCategoryPicker(
                  kind: widget.kind,
                  selected: _categories,
                  onChanged: (value) => setState(() => _categories = value),
                ),
              ),
            if (_whatsapp) ...[
              TextField(
                key: const Key('channel-access-token'),
                controller: _accessToken,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Access token'),
              ),
              const SizedBox(height: 10),
              TextField(
                key: const Key('channel-app-secret'),
                controller: _appSecret,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'App secret'),
              ),
              const SizedBox(height: 10),
              TextField(
                key: const Key('channel-verify-token'),
                controller: _verifyToken,
                decoration: const InputDecoration(
                  labelText: 'Webhook verify token',
                ),
              ),
            ],
            if (_error != null)
              InlineNotice(
                message: _error!,
                tone: NoticeTone.danger,
                margin: const EdgeInsets.only(top: 12),
              ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('channel-save'),
              onPressed: _saving ? null : () => unawaited(_save()),
              child: _saving
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Text('Connect ${kind.label}'),
            ),
          ],
        ),
      ),
    );
  }
}
