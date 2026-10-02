import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

typedef SettingsDestination = ({
  String title,
  String subtitle,
  IconData icon,
  String destination,
});

/// Settings groups in display order. Each destination is opened by the app shell's utility router.
const List<(String, List<SettingsDestination>)> settingsGroups = [
  (
    'General',
    [
      (
        title: 'Appearance',
        subtitle: 'Light, dark, or match this device',
        icon: PhosphorIconsRegular.circleHalf,
        destination: 'appearance',
      ),
      (
        title: 'Face ID lock',
        subtitle: 'Lock Jarvis when you leave the app',
        icon: PhosphorIconsRegular.lockSimple,
        destination: 'app-lock',
      ),
      (
        title: 'Voice',
        subtitle: 'The voice Jarvis speaks with, and hands-free listening',
        icon: PhosphorIconsRegular.microphone,
        destination: 'voice-settings',
      ),
      (
        title: 'Morning briefing',
        subtitle: 'When your daily summary arrives',
        icon: PhosphorIconsRegular.sunHorizon,
        destination: 'briefing',
      ),
    ],
  ),
  (
    'Connections',
    [
      (
        title: 'Connected apps',
        subtitle: 'Calendar, mail, smart home, and more',
        icon: PhosphorIconsRegular.plugsConnected,
        destination: 'integrations',
      ),
      (
        title: 'WhatsApp & Signal',
        subtitle: 'Chat with Jarvis from your messaging apps',
        icon: PhosphorIconsRegular.whatsappLogo,
        destination: 'channels',
      ),
      (
        title: 'This device',
        subtitle: 'Location, clipboard, links, and notifications',
        icon: PhosphorIconsRegular.deviceMobile,
        destination: 'devices',
      ),
    ],
  ),
  (
    'Routines',
    [
      (
        title: 'Reminders and notifications',
        subtitle: 'What Jarvis reminds you of, and what it sent you',
        icon: PhosphorIconsRegular.bell,
        destination: 'reminders',
      ),
      (
        title: 'Condition watches',
        subtitle: 'Get a heads-up when something changes',
        icon: PhosphorIconsRegular.pulse,
        destination: 'watches',
      ),
      (
        title: 'Automation rules',
        subtitle: 'When something happens, Jarvis does something',
        icon: PhosphorIconsRegular.flowArrow,
        destination: 'automations',
      ),
    ],
  ),
  (
    'How Jarvis works with you',
    [
      (
        title: 'Persona',
        subtitle: 'What Jarvis has learned about working with you',
        icon: PhosphorIconsRegular.userCircle,
        destination: 'persona',
      ),
      (
        title: 'Skills',
        subtitle: 'Step-by-step routines Jarvis knows',
        icon: PhosphorIconsRegular.magicWand,
        destination: 'skills',
      ),
      (
        title: 'Profiles',
        subtitle: 'Separate setups, such as work and home',
        icon: PhosphorIconsRegular.identificationCard,
        destination: 'profiles',
      ),
      (
        title: 'Learning',
        subtitle: 'How Jarvis learns, reflects, and checks in',
        icon: PhosphorIconsRegular.pulse,
        destination: 'learning',
      ),
    ],
  ),
  (
    'Privacy & data',
    [
      (
        title: 'Approvals',
        subtitle: 'Actions waiting for your OK',
        icon: PhosphorIconsRegular.shieldCheck,
        destination: 'approvals',
      ),
      (
        title: 'Activity log',
        subtitle: 'Everything Jarvis did, and when',
        icon: PhosphorIconsRegular.listChecks,
        destination: 'audit',
      ),
      (
        title: 'Files',
        subtitle: 'Documents you shared with Jarvis',
        icon: PhosphorIconsRegular.folderOpen,
        destination: 'files',
      ),
      (
        title: 'Knowledge graph',
        subtitle: 'People, places, and projects Jarvis knows about',
        icon: PhosphorIconsRegular.graph,
        destination: 'graph',
      ),
      (
        title: 'Usage',
        subtitle: 'How much you use Jarvis, and what it costs',
        icon: PhosphorIconsRegular.chartBar,
        destination: 'usage',
      ),
    ],
  ),
  (
    'Advanced',
    [
      (
        title: 'Models',
        subtitle: 'Which AI models Jarvis runs on',
        icon: PhosphorIconsRegular.cpu,
        destination: 'models',
      ),
      (
        title: 'Coding runs',
        subtitle: 'Code changes Jarvis made for you to review',
        icon: PhosphorIconsRegular.code,
        destination: 'coding',
      ),
      (
        title: 'Other agents',
        subtitle: 'Let other AI assistants work with Jarvis',
        icon: PhosphorIconsRegular.robot,
        destination: 'agents',
      ),
    ],
  ),
];

class SettingsView extends StatelessWidget {
  const SettingsView({
    required this.connected,
    required this.onOpen,
    this.onSignOut,
    super.key,
  });

  final bool connected;
  final ValueChanged<String> onOpen;
  final VoidCallback? onSignOut;

  @override
  Widget build(BuildContext context) => SafeArea(
    child: ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
      children: [
        ContentWidth(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SurfaceCard(
                child: Row(
                  children: [
                    const JarvisOrb(size: 44, glow: false),
                    const SizedBox(width: 16),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Your assistant',
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            connected
                                ? 'Live updates connected'
                                : 'Offline — live updates paused',
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
              for (final (title, items) in settingsGroups)
                _group(context, title, [
                  for (final item in items) _tile(context, item),
                ]),
              if (onSignOut != null)
                _group(context, 'Account', [
                  ListTile(
                    leading: IconBadge(
                      icon: PhosphorIconsRegular.signOut,
                      color: JarvisColors.of(context).danger,
                      size: 34,
                    ),
                    title: Text(
                      'Sign out',
                      style: TextStyle(color: JarvisColors.of(context).danger),
                    ),
                    onTap: onSignOut,
                  ),
                ]),
            ],
          ),
        ),
      ],
    ),
  );

  Widget _group(BuildContext context, String title, List<Widget> tiles) =>
      Padding(
        padding: const EdgeInsets.only(top: 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(6, 0, 6, 8),
              child: Text(
                title.toUpperCase(),
                style: Theme.of(context).textTheme.labelSmall?.copyWith(
                  color: JarvisColors.of(context).muted,
                  letterSpacing: .8,
                ),
              ),
            ),
            SurfaceCard(
              padding: const EdgeInsets.symmetric(vertical: 6),
              child: Column(
                children: [
                  for (final (index, tile) in tiles.indexed) ...[
                    if (index > 0) const Divider(indent: 64),
                    tile,
                  ],
                ],
              ),
            ),
          ],
        ),
      );

  Widget _tile(BuildContext context, SettingsDestination item) => ListTile(
    key: Key('settings-${item.destination}'),
    leading: IconBadge(icon: item.icon, size: 34),
    title: Text(item.title),
    subtitle: Text(item.subtitle),
    trailing: Icon(
      PhosphorIconsRegular.caretRight,
      size: 16,
      color: JarvisColors.of(context).muted,
    ),
    onTap: () => onOpen(item.destination),
  );
}
