part of 'channels_screen.dart';

/// Notification categories a channel can forward, in display order. Ids match
/// `ChannelNotificationCategories` on the server.
const channelNotificationCategories = <({String id, String label})>[
  (id: 'reminders', label: 'Reminders'),
  (id: 'tasks', label: 'Task results'),
  (id: 'briefings', label: 'Daily briefing'),
  (id: 'watches', label: 'Watches'),
  (id: 'automations', label: 'Automation results'),
  (id: 'learning', label: 'Learning updates'),
  (id: 'check_ins', label: 'Check-ins'),
  (id: 'approvals', label: 'Approvals'),
];

/// What the server forwards when a channel has no explicit choice: everything except approvals.
Set<String> defaultChannelNotificationCategories() => {
  for (final category in channelNotificationCategories)
    if (category.id != 'approvals') category.id,
};

/// Chips for choosing which notifications go to a channel, with the honest
/// limits of answering approvals from a chat app.
class ChannelNotificationCategoryPicker extends StatelessWidget {
  const ChannelNotificationCategoryPicker({
    required this.kind,
    required this.selected,
    required this.onChanged,
    super.key,
  });

  final String kind;
  final Set<String> selected;
  final ValueChanged<Set<String>> onChanged;

  @override
  Widget build(BuildContext context) {
    final label = channelKind(kind).label;
    final muted = JarvisColors.of(context).inkSoft;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 4,
          children: [
            for (final category in channelNotificationCategories)
              FilterChip(
                key: Key('channel-category-${category.id}'),
                label: Text(category.label),
                selected: selected.contains(category.id),
                onSelected: (value) => onChanged({
                  ...selected.where((id) => id != category.id),
                  if (value) category.id,
                }),
              ),
          ],
        ),
        if (selected.contains('approvals'))
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Text(
              'Approvals started in this $label chat can be answered with YES or NO. '
              'Approvals from app chats, tasks, and automations can only be decided in the Jarvis app; '
              'Jarvis says so in the $label message.',
              key: const Key('channel-approvals-note'),
              style: Theme.of(
                context,
              ).textTheme.bodySmall?.copyWith(color: muted),
            ),
          ),
      ],
    );
  }
}

/// Turn notification forwarding on or off for an existing channel and pick what is forwarded.
class ChannelNotificationsSheet extends StatefulWidget {
  const ChannelNotificationsSheet({
    required this.http,
    required this.channel,
    super.key,
  });

  final Dio http;
  final Map<String, dynamic> channel;

  @override
  State<ChannelNotificationsSheet> createState() =>
      _ChannelNotificationsSheetState();
}

class _ChannelNotificationsSheetState extends State<ChannelNotificationsSheet> {
  late var _forward = asJsonBool(widget.channel['forwardNotifications']);
  late Set<String> _selected = widget.channel['notificationCategories'] == null
      ? defaultChannelNotificationCategories()
      : jsonStrings(widget.channel['notificationCategories']).toSet();
  var _saving = false;
  String? _error;

  Future<void> _save() async {
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/channels/${asJsonString(widget.channel['id'])}',
        data: {
          'kind': asJsonString(widget.channel['kind']),
          'displayName': asJsonString(widget.channel['displayName']),
          'account': asJsonString(widget.channel['account']),
          'enabled': asJsonBool(widget.channel['enabled'], true),
          'allowedSenders': jsonStrings(widget.channel['allowedSenders']),
          'forwardNotifications': _forward,
          'notifyRecipient': asJsonString(widget.channel['notifyRecipient']),
          'notificationCategories': _selected.toList(),
        },
      );
      if (!mounted) return;
      Navigator.of(context).pop(
        jsonObject(response.data) ??
            {
              ...widget.channel,
              'forwardNotifications': _forward,
              'notificationCategories': _selected.toList(),
            },
      );
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not update notifications.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not update notifications.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final kind = asJsonString(widget.channel['kind']) ?? '';
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        16,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Notifications on ${channelKind(kind).label}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            SwitchListTile(
              key: const Key('channel-forward-switch'),
              contentPadding: EdgeInsets.zero,
              title: const Text('Forward Jarvis notifications'),
              value: _forward,
              onChanged:
                  _saving ||
                      jsonStrings(widget.channel['allowedSenders']).isEmpty
                  ? null
                  : (value) => setState(() => _forward = value),
            ),
            if (jsonStrings(widget.channel['allowedSenders']).isEmpty)
              const Text(
                'Add an allowed phone number before forwarding notifications.',
              ),
            if (_forward)
              ChannelNotificationCategoryPicker(
                kind: kind,
                selected: _selected,
                onChanged: (value) => setState(() => _selected = value),
              ),
            if (_error != null)
              InlineNotice(
                message: _error!,
                tone: NoticeTone.danger,
                margin: const EdgeInsets.only(top: 12),
              ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('channel-notifications-save'),
              onPressed: _saving ? null : () => unawaited(_save()),
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
  }
}
