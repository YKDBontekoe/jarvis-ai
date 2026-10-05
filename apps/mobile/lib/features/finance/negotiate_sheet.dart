import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What the owner picked in the negotiation sheet. [cancelUrl] is the trimmed text of the field, so an empty
/// string clears a saved page.
class NegotiationChoice {
  const NegotiationChoice({
    required this.goal,
    required this.mode,
    required this.cancelUrl,
  });

  final String goal;
  final String mode;
  final String cancelUrl;
}

/// Cancel a subscription or ask for a lower price, either as a drafted message (a background task writes it, the
/// owner sends it) or live in the browser with Jarvis in a chat. The browser route asks for approval at every
/// step, so the sheet says so; it needs a chat to open, so it is off without one.
Future<NegotiationChoice?> showNegotiateSheet(
  BuildContext context, {
  required String merchant,
  required String? savedCancelUrl,
  required bool browserAvailable,
}) => showModalBottomSheet<NegotiationChoice>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => _NegotiateSheet(
    merchant: merchant,
    savedCancelUrl: savedCancelUrl,
    browserAvailable: browserAvailable,
  ),
);

class _NegotiateSheet extends StatefulWidget {
  const _NegotiateSheet({
    required this.merchant,
    required this.savedCancelUrl,
    required this.browserAvailable,
  });

  final String merchant;
  final String? savedCancelUrl;
  final bool browserAvailable;

  @override
  State<_NegotiateSheet> createState() => _NegotiateSheetState();
}

class _NegotiateSheetState extends State<_NegotiateSheet> {
  String _goal = 'cancel';
  String _mode = 'draft';
  late final _url = TextEditingController(text: widget.savedCancelUrl ?? '');

  @override
  void dispose() {
    _url.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    return SafeArea(
      child: SingleChildScrollView(
        padding: EdgeInsets.fromLTRB(
          20,
          0,
          20,
          16 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(widget.merchant, style: text.titleMedium),
            const SizedBox(height: 4),
            Text(
              'What do you want to do?',
              style: TextStyle(color: colors.inkSoft),
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              children: [
                ChoiceChip(
                  key: const Key('negotiate-goal-cancel'),
                  label: const Text('Cancel it'),
                  selected: _goal == 'cancel',
                  onSelected: (_) => setState(() => _goal = 'cancel'),
                ),
                ChoiceChip(
                  key: const Key('negotiate-goal-lower'),
                  label: const Text('Get a lower price'),
                  selected: _goal == 'lower_price',
                  onSelected: (_) => setState(() => _goal = 'lower_price'),
                ),
              ],
            ),
            const SizedBox(height: 16),
            _ModeCard(
              key: const Key('negotiate-mode-draft'),
              selected: _mode == 'draft',
              icon: PhosphorIconsRegular.pencilSimple,
              title: 'Draft a message',
              body:
                  'A background task writes it and you find it in Tasks. You '
                  'send it yourself; nothing is contacted.',
              onTap: () => setState(() => _mode = 'draft'),
            ),
            const SizedBox(height: 8),
            _ModeCard(
              key: const Key('negotiate-mode-browser'),
              selected: _mode == 'browser',
              enabled: widget.browserAvailable,
              icon: PhosphorIconsRegular.browser,
              title: 'Do it with me in the browser',
              body: widget.browserAvailable
                  ? 'Opens a chat. Jarvis works in an isolated browser and '
                        'asks your approval before it navigates, clicks or '
                        'types. It never enters passwords or payment details.'
                  : 'Needs the chat, which is not available from here.',
              onTap: () => setState(() => _mode = 'browser'),
            ),
            const SizedBox(height: 16),
            TextField(
              key: const Key('negotiate-url'),
              controller: _url,
              keyboardType: TextInputType.url,
              autocorrect: false,
              decoration: const InputDecoration(
                labelText: 'Cancel page (optional)',
                hintText: 'https://…',
                helperText:
                    'Saved for next time. Leave empty to let Jarvis find it.',
              ),
            ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('negotiate-start'),
              onPressed: () => Navigator.of(context).pop(
                NegotiationChoice(
                  goal: _goal,
                  mode: _mode,
                  cancelUrl: _url.text.trim(),
                ),
              ),
              child: Text(
                _mode == 'draft' ? 'Draft the message' : 'Open the chat',
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ModeCard extends StatelessWidget {
  const _ModeCard({
    required this.selected,
    required this.icon,
    required this.title,
    required this.body,
    required this.onTap,
    this.enabled = true,
    super.key,
  });

  final bool selected;
  final bool enabled;
  final IconData icon;
  final String title;
  final String body;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Opacity(
      opacity: enabled ? 1 : 0.5,
      child: SurfaceCard(
        onTap: enabled ? onTap : null,
        borderColor: selected && enabled ? colors.accent : colors.outline,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            IconBadge(icon: icon),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: Theme.of(context).textTheme.titleSmall),
                  const SizedBox(height: 2),
                  Text(
                    body,
                    style: TextStyle(fontSize: 13, color: colors.inkSoft),
                  ),
                ],
              ),
            ),
            if (selected && enabled)
              Icon(PhosphorIconsRegular.checkCircle, color: colors.accent),
          ],
        ),
      ),
    );
  }
}
