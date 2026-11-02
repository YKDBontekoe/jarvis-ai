import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../everything/feature_search.dart' show foldForSearch;

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
        title: 'Waiting to sync',
        subtitle: 'Changes made without a connection',
        icon: PhosphorIconsRegular.clock,
        destination: 'queued-changes',
      ),
      (
        title: 'Time zone',
        subtitle: 'Where your day starts and ends',
        icon: PhosphorIconsRegular.globeSimple,
        destination: 'home-zone',
      ),
      (
        title: 'Never learn about',
        subtitle: 'Topics Jarvis must not remember from chats',
        icon: PhosphorIconsRegular.eye,
        destination: 'do-not-learn',
      ),
      (
        title: 'Language and currency',
        subtitle: 'Reply language and the default for budgets',
        icon: PhosphorIconsRegular.globeSimple,
        destination: 'locale',
      ),
      (
        title: 'Morning briefing',
        subtitle: 'When your daily summary arrives',
        icon: PhosphorIconsRegular.sunHorizon,
        destination: 'briefing',
      ),
      (
        title: 'Weekly review',
        subtitle: 'A Sunday look back at your week, with trends',
        icon: PhosphorIconsRegular.chartLine,
        destination: 'weekly-review',
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
      (
        title: 'Modes',
        subtitle: 'Focus, meeting, sleep: when Jarvis stays quiet',
        icon: PhosphorIconsRegular.moon,
        destination: 'modes',
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
        subtitle: 'Actions waiting for your OK, and ones you always allow',
        icon: PhosphorIconsRegular.shieldCheck,
        destination: 'approvals',
      ),
      (
        title: 'Audit log',
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

/// Words people use for a setting besides its title, in English and Dutch, by
/// destination: "dark mode" finds Appearance, "tijdzone" finds Time zone.
const settingAliases = <String, List<String>>{
  'appearance': [
    'dark mode',
    'donker',
    'theme',
    'thema',
    'light',
    'licht',
    'display',
    'text size',
    'tekstgrootte',
    'motion',
  ],
  'app-lock': [
    'face id',
    'touch id',
    'passcode',
    'pincode',
    'biometrics',
    'security',
    'beveiliging',
    'vergrendeling',
  ],
  'voice-settings': [
    'speech',
    'spraak',
    'microphone',
    'microfoon',
    'hands-free',
    'captions',
    'ondertiteling',
  ],
  'queued-changes': ['offline', 'sync', 'synchroniseren', 'pending', 'queue'],
  'home-zone': ['timezone', 'tijdzone', 'region', 'clock', 'tijd'],
  'do-not-learn': ['forget', 'vergeten', 'privacy', 'block', 'topics'],
  'locale': [
    'language',
    'taal',
    'currency',
    'valuta',
    'euro',
    'dollar',
    'nederlands',
    'english',
    'dutch',
  ],
  'briefing': ['morning', 'ochtend', 'summary', 'daily', 'dagelijks'],
  'weekly-review': ['sunday', 'zondag', 'trends', 'recap'],
  'integrations': [
    'calendar',
    'agenda',
    'mail',
    'gmail',
    'smart home',
    'mcp',
    'koppelingen',
  ],
  'channels': ['whatsapp', 'signal', 'messaging', 'berichten'],
  'devices': [
    'location',
    'locatie',
    'clipboard',
    'permissions',
    'toestemmingen',
    'phone',
    'telefoon',
  ],
  'reminders': ['notifications', 'meldingen', 'alerts', 'herinneringen'],
  'watches': ['monitor', 'price', 'prijs', 'alert'],
  'automations': ['rules', 'regels', 'workflows', 'triggers'],
  'modes': ['focus', 'sleep', 'slaap', 'do not disturb', 'niet storen'],
  'persona': ['personality', 'persoonlijkheid', 'tone', 'style'],
  'skills': ['routines', 'abilities', 'vaardigheden'],
  'profiles': ['workspace', 'werk', 'thuis', 'work', 'home'],
  'learning': ['lessons', 'lessen', 'reflect', 'leren'],
  'approvals': ['permissions', 'toestemming', 'allow', 'toestaan', 'confirm'],
  'audit': ['history', 'log', 'geschiedenis', 'activity'],
  'files': ['documents', 'documenten', 'uploads', 'bestanden'],
  'graph': ['people', 'places', 'mensen', 'kennis'],
  'usage': ['cost', 'kosten', 'tokens', 'spend', 'gebruik'],
  'models': ['ai', 'model', 'openrouter', 'codex', 'gpt'],
  'coding': ['code', 'github', 'programming'],
  'agents': ['a2a', 'assistants', 'assistenten'],
};

/// A setting that matched a search, with the group it lives in.
typedef SettingsMatch = ({String group, SettingsDestination item, int score});

/// Settings that answer [query] by title, description or alias, best first.
/// An exact title beats a title that starts with the words, which beats an
/// alias, which beats a word in the description or the group name.
List<SettingsMatch> searchSettings(String query) {
  final needle = foldForSearch(query);
  if (needle.isEmpty) return const [];
  final matches = <SettingsMatch>[];
  for (final (group, items) in settingsGroups) {
    for (final item in items) {
      final title = foldForSearch(item.title);
      var score = 0;
      void consider(int value) {
        if (value > score) score = value;
      }

      if (title == needle) consider(100);
      if (title.startsWith(needle)) consider(90);
      if (title.split(RegExp(r'[\s&-]+')).any((w) => w.startsWith(needle))) {
        consider(80);
      }
      if (title.contains(needle)) consider(70);
      for (final alias in settingAliases[item.destination] ?? const []) {
        final folded = foldForSearch(alias);
        if (folded == needle) consider(60);
        if (folded.startsWith(needle)) consider(50);
        if (folded.contains(needle)) consider(40);
      }
      if (foldForSearch(item.subtitle).contains(needle)) consider(30);
      if (foldForSearch(group).contains(needle)) consider(20);
      if (score > 0) matches.add((group: group, item: item, score: score));
    }
  }
  // Stable: equal scores keep the order the settings are listed in.
  final order = {for (final (i, m) in matches.indexed) m.item.destination: i};
  matches.sort((a, b) {
    final byScore = b.score.compareTo(a.score);
    return byScore != 0
        ? byScore
        : order[a.item.destination]!.compareTo(order[b.item.destination]!);
  });
  return matches;
}

class SettingsView extends StatefulWidget {
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
  State<SettingsView> createState() => _SettingsViewState();

  /// Each group has its own colour, so a long list is easy to scan.
  static Color _groupColor(JarvisColors colors, String group) =>
      switch (group) {
        'General' => colors.accent,
        'Connections' => colors.info,
        'Routines' => colors.success,
        'How Jarvis works with you' => colors.violet,
        'Privacy & data' => colors.warning,
        _ => colors.inkSoft,
      };
}

class _SettingsViewState extends State<SettingsView> {
  final _query = TextEditingController();

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SafeArea(
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _hero(context, colors),
                _search(context, colors),
                if (_query.text.trim().isNotEmpty)
                  _results(context, colors)
                else
                  for (final (title, items) in settingsGroups)
                    _group(context, title, [
                      for (final item in items)
                        _tile(
                          context,
                          item,
                          SettingsView._groupColor(colors, title),
                        ),
                    ]),
                if (widget.onSignOut != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 28),
                    child: SurfaceCard(
                      padding: EdgeInsets.zero,
                      onTap: widget.onSignOut,
                      child: SizedBox(
                        height: 52,
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              PhosphorIconsRegular.signOut,
                              size: 18,
                              color: colors.danger,
                            ),
                            const SizedBox(width: 8),
                            Text(
                              'Sign out',
                              style: TextStyle(
                                color: colors.danger,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _search(BuildContext context, JarvisColors colors) => Padding(
    padding: const EdgeInsets.only(top: 16),
    // The page is shown with and without a Scaffold around it.
    child: Material(
      type: MaterialType.transparency,
      child: TextField(
        key: const Key('settings-search'),
        controller: _query,
        onChanged: (_) => setState(() {}),
        textInputAction: TextInputAction.search,
        decoration: InputDecoration(
          hintText: 'Search settings',
          prefixIcon: Icon(
            PhosphorIconsRegular.magnifyingGlass,
            size: 18,
            color: colors.muted,
          ),
          suffixIcon: _query.text.isEmpty
              ? null
              : IconButton(
                  key: const Key('settings-clear-search'),
                  tooltip: 'Clear settings search',
                  onPressed: () => setState(_query.clear),
                  icon: Icon(
                    PhosphorIconsRegular.x,
                    size: 16,
                    color: colors.inkSoft,
                  ),
                ),
        ),
      ),
    ),
  );

  /// Matching settings, each with the group it belongs to.
  Widget _results(BuildContext context, JarvisColors colors) {
    final matches = searchSettings(_query.text);
    if (matches.isEmpty) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 40),
        child: Center(
          child: Text(
            'No setting matches “${_query.text.trim()}”',
            key: const Key('settings-no-match'),
            style: TextStyle(color: colors.muted),
          ),
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.only(top: 16),
      child: SurfaceCard(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Column(
          children: [
            for (final (index, match) in matches.indexed) ...[
              if (index > 0) const Divider(indent: 62, height: 1),
              _tile(
                context,
                match.item,
                SettingsView._groupColor(colors, match.group),
                group: match.group,
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _hero(BuildContext context, JarvisColors colors) => SurfaceCard(
    radius: JarvisRadii.xl,
    padding: const EdgeInsets.fromLTRB(18, 18, 18, 18),
    gradient: colors.litSurface(colors.violet, strength: .12),
    borderColor: colors.outline.withValues(alpha: colors.isDark ? .6 : .35),
    child: Row(
      children: [
        const JarvisOrb(size: 56),
        const SizedBox(width: 16),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Your assistant',
                style: JarvisType.displayOf(context)
                    .copyWith(fontSize: 20, letterSpacing: -.4),
              ),
              const SizedBox(height: 8),
              StatusChip(
                label: widget.connected ? 'Live' : 'Offline, updates paused',
                color: widget.connected ? colors.success : colors.muted,
              ),
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
                title,
                style: TextStyle(
                  fontFamily: 'Geist',
                  fontSize: 13.5,
                  fontWeight: FontWeight.w600,
                  color: JarvisColors.of(context).inkSoft,
                ),
              ),
            ),
            SurfaceCard(
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: Column(
                children: [
                  for (final (index, tile) in tiles.indexed) ...[
                    if (index > 0) const Divider(indent: 62, height: 1),
                    tile,
                  ],
                ],
              ),
            ),
          ],
        ),
      );

  Widget _tile(
    BuildContext context,
    SettingsDestination item,
    Color tint, {
    String? group,
  }) {
    final colors = JarvisColors.of(context);
    return ListTile(
      key: Key('settings-${item.destination}'),
      contentPadding: const EdgeInsets.fromLTRB(14, 2, 12, 2),
      leading: Container(
        width: 34,
        height: 34,
        decoration: BoxDecoration(
          color: tint.withValues(alpha: colors.isDark ? .22 : .12),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Icon(item.icon, size: 18, color: tint),
      ),
      title: Text(item.title),
      subtitle: Text(
        group == null ? item.subtitle : '$group · ${item.subtitle}',
      ),
      trailing: Icon(
        PhosphorIconsRegular.caretRight,
        size: 16,
        color: colors.muted,
      ),
      onTap: () => widget.onOpen(item.destination),
    );
  }
}
