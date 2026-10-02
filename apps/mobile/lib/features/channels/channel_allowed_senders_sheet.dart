part of 'channels_screen.dart';

class ChannelAllowedSendersSheet extends StatefulWidget {
  const ChannelAllowedSendersSheet({
    required this.http,
    required this.channel,
    super.key,
  });

  final Dio http;
  final Map<String, dynamic> channel;

  @override
  State<ChannelAllowedSendersSheet> createState() =>
      _ChannelAllowedSendersSheetState();
}

class _ChannelAllowedSendersSheetState
    extends State<ChannelAllowedSendersSheet> {
  late final TextEditingController _senders;
  var _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _senders = TextEditingController(
      text: jsonStrings(widget.channel['allowedSenders']).join('\n'),
    );
  }

  @override
  void dispose() {
    _senders.dispose();
    super.dispose();
  }

  String _normalizePhone(String value) {
    var trimmed = value.trim();
    if (trimmed.startsWith('00')) trimmed = '+${trimmed.substring(2)}';
    final digits = trimmed.replaceAll(RegExp(r'\D'), '');
    return digits.isEmpty ? '' : '+$digits';
  }

  Future<void> _save() async {
    final seen = <String>{};
    final senders = <String>[];
    for (final raw in _senders.text.split(RegExp(r'[\n,]'))) {
      final value = raw.trim();
      if (value.isEmpty) continue;
      final normalized = _normalizePhone(value);
      final identity = normalized.isEmpty ? value : normalized;
      if (seen.add(identity)) senders.add(value);
    }

    if (senders.isEmpty &&
        asJsonString(widget.channel['kind']) != 'whatsapp_linked') {
      setState(() => _error = 'Keep at least one allowed phone number.');
      return;
    }
    if (senders.length > 20) {
      setState(() => _error = 'You can allow up to 20 phone numbers.');
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });
    final currentRecipient = asJsonString(widget.channel['notifyRecipient']);
    final notifyRecipient =
        currentRecipient != null &&
            senders.any(
              (sender) =>
                  _normalizePhone(sender) == _normalizePhone(currentRecipient),
            )
        ? currentRecipient
        : null;
    final forward =
        senders.isNotEmpty &&
        asJsonBool(widget.channel['forwardNotifications']);

    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/channels/${asJsonString(widget.channel['id'])}',
        data: {
          'kind': asJsonString(widget.channel['kind']),
          'displayName': asJsonString(widget.channel['displayName']),
          'account': asJsonString(widget.channel['account']),
          'enabled': asJsonBool(widget.channel['enabled'], true),
          'allowedSenders': senders,
          'forwardNotifications': forward,
          'notifyRecipient': notifyRecipient,
          if (widget.channel['notificationCategories'] != null)
            'notificationCategories': jsonStrings(
              widget.channel['notificationCategories'],
            ),
        },
      );
      if (!mounted) return;
      final updated = jsonObject(response.data);
      Navigator.of(context).pop(
        updated ??
            {
              ...widget.channel,
              'allowedSenders': senders,
              'notifyRecipient': notifyRecipient,
              'forwardNotifications': forward,
            },
      );
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not update allowed senders.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not update allowed senders.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final inset = MediaQuery.viewInsetsOf(context).bottom;
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 16, 20, 20 + inset),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Allowed senders',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 6),
            Text(
              'Add one international phone number per line. Only these numbers can message Jarvis. You can allow up to 20.${asJsonString(widget.channel['kind']) == 'whatsapp_linked' ? ' Leave this empty to use only read along, without automatic replies.' : ''}',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: JarvisColors.of(context).inkSoft,
              ),
            ),
            const SizedBox(height: 16),
            TextField(
              key: const Key('channel-allowed-senders'),
              controller: _senders,
              autofocus: true,
              keyboardType: TextInputType.multiline,
              minLines: 3,
              maxLines: 8,
              decoration: const InputDecoration(
                labelText: 'Phone numbers',
                hintText: '+31612345678',
                alignLabelWithHint: true,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              'If you remove the current notification number, notifications will go to the first number in this list.',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: JarvisColors.of(context).muted,
              ),
            ),
            if (_error != null)
              InlineNotice(
                message: _error!,
                tone: NoticeTone.danger,
                margin: const EdgeInsets.only(top: 12),
              ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('channel-allowed-senders-save'),
              onPressed: _saving ? null : () => unawaited(_save()),
              child: _saving
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('Save allowed senders'),
            ),
          ],
        ),
      ),
    );
  }
}
