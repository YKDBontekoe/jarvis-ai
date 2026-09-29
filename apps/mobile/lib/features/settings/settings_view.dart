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
    'App',
    [
      (
        title: 'Appearance',
        subtitle: 'Light, dark, or match this device',
        icon: PhosphorIconsRegular.circleHalf,
        destination: 'appearance',
      ),
    ],
  ),
  (
    'Assistant',
    [
      (
        title: 'Models',
        subtitle: 'Codex or OpenRouter chat, reasoning, and embeddings',
        icon: PhosphorIconsRegular.cpu,
        destination: 'models',
      ),
      (
        title: 'Learning & heartbeat',
        subtitle: 'Continuous learning, dreaming, and check-ins',
        icon: PhosphorIconsRegular.pulse,
        destination: 'learning',
      ),
      (
        title: 'Persona',
        subtitle: 'How Jarvis has learned to work with you',
        icon: PhosphorIconsRegular.userCircle,
        destination: 'persona',
      ),
      (
        title: 'Profiles',
        subtitle: 'Personas, skills, files, models, and learning per context',
        icon: PhosphorIconsRegular.identificationCard,
        destination: 'profiles',
      ),
      (
        title: 'Skills',
        subtitle: 'Procedures Jarvis learned or you taught it',
        icon: PhosphorIconsRegular.magicWand,
        destination: 'skills',
      ),
      (
        title: 'WhatsApp & Signal',
        subtitle: 'Chat with Jarvis from your phone',
        icon: PhosphorIconsRegular.whatsappLogo,
        destination: 'channels',
      ),
      (
        title: 'Agents',
        subtitle: 'Agent2Agent peers and inbound tokens',
        icon: PhosphorIconsRegular.robot,
        destination: 'agents',
      ),
      (
        title: 'This device',
        subtitle: 'Location, clipboard, links, and notifications',
        icon: PhosphorIconsRegular.deviceMobile,
        destination: 'devices',
      ),
      (
        title: 'Voice',
        subtitle: 'ChatGPT voice, hands-free listening, and captions',
        icon: PhosphorIconsRegular.microphone,
        destination: 'voice-settings',
      ),
      (
        title: 'Integrations',
        subtitle: 'Packs, MCP servers, OAuth, and credentials',
        icon: PhosphorIconsRegular.plugsConnected,
        destination: 'integrations',
      ),
      (
        title: 'Coding runs',
        subtitle: 'Review isolated worktrees and diffs',
        icon: PhosphorIconsRegular.code,
        destination: 'coding',
      ),
      (
        title: 'Approvals',
        subtitle: 'Review actions Jarvis needs permission to run',
        icon: PhosphorIconsRegular.shieldCheck,
        destination: 'approvals',
      ),
      (
        title: 'Morning briefing',
        subtitle: 'Choose your daily briefing schedule and time zone',
        icon: PhosphorIconsRegular.sunHorizon,
        destination: 'briefing',
      ),
    ],
  ),
  (
    'Automations',
    [
      (
        title: 'Reminders and notifications',
        subtitle: 'View scheduled reminders and alerts',
        icon: PhosphorIconsRegular.bell,
        destination: 'reminders',
      ),
      (
        title: 'Condition watches',
        subtitle: 'Manage threshold alerts',
        icon: PhosphorIconsRegular.pulse,
        destination: 'watches',
      ),
      (
        title: 'Automation rules',
        subtitle: 'Triggers, conditions, and chained actions',
        icon: PhosphorIconsRegular.flowArrow,
        destination: 'automations',
      ),
    ],
  ),
  (
    'Data',
    [
      (
        title: 'Usage',
        subtitle: 'Tokens, Codex, OpenRouter costs, memories, and dreams',
        icon: PhosphorIconsRegular.chartBar,
        destination: 'usage',
      ),
      (
        title: 'Knowledge graph',
        subtitle: 'Pan, search, and inspect people, places, and projects',
        icon: PhosphorIconsRegular.graph,
        destination: 'graph',
      ),
      (
        title: 'Files',
        subtitle: 'Browse uploaded documents',
        icon: PhosphorIconsRegular.folderOpen,
        destination: 'files',
      ),
      (
        title: 'Audit log',
        subtitle: 'Review Jarvis activity',
        icon: PhosphorIconsRegular.listChecks,
        destination: 'audit',
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
