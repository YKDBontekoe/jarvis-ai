import 'package:flutter/material.dart';

import '../../conversation_groups.dart';
import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../projects/project_style.dart';

/// Primary navigation: a drawer on phones and a permanent sidebar on wide
/// screens. Destinations sit on top, then the most recently used projects,
/// recent conversations below, pinned first and then grouped by day, and
/// settings with connection status at the bottom.
class JarvisSidebar extends StatefulWidget {
  const JarvisSidebar({
    required this.conversations,
    required this.selectedConversationId,
    required this.homeSelected,
    required this.connected,
    required this.onHome,
    required this.onNewChat,
    required this.onVoice,
    required this.onConversation,
    required this.onSeeAll,
    required this.onUtility,
    required this.onSettings,
    required this.onJarvisSearch,
    this.projects = const [],
    this.selectedProjectId,
    this.onProject,
    this.onAllProjects,
    this.onNewProject,
    super.key,
  });

  /// How many projects the sidebar lists before "See all".
  static const visibleProjects = 5;

  final List<Map<String, dynamic>> conversations;
  final String? selectedConversationId;
  final bool homeSelected;
  final bool connected;
  final VoidCallback onHome;
  final VoidCallback onNewChat;
  final VoidCallback onVoice;
  final ValueChanged<String> onConversation;
  final VoidCallback onSeeAll;
  final ValueChanged<String> onUtility;
  final VoidCallback onSettings;
  final VoidCallback onJarvisSearch;
  final List<Map<String, dynamic>> projects;
  final String? selectedProjectId;
  final ValueChanged<String>? onProject;
  final VoidCallback? onAllProjects;
  final VoidCallback? onNewProject;

  @override
  State<JarvisSidebar> createState() => _JarvisSidebarState();
}

class _JarvisSidebarState extends State<JarvisSidebar> {
  @override
  Widget build(BuildContext context) {
    final groups = groupConversations(widget.conversations);
    return ColoredBox(
      color: JarvisColors.of(context).canvas,
      child: SafeArea(
        right: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 10),
              child: Row(
                children: [
                  Expanded(child: _SearchPill(onTap: widget.onJarvisSearch)),
                  const SizedBox(width: 10),
                  CircleIconButton(
                    icon: PhosphorIconsRegular.notePencil,
                    tooltip: 'New chat',
                    size: 42,
                    onPressed: widget.onNewChat,
                  ),
                ],
              ),
            ),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(10, 4, 10, 12),
                children: [
                  _NavRow(
                    leading: const JarvisOrb(size: 22, glow: false),
                    label: 'Jarvis',
                    selected: widget.homeSelected,
                    onTap: widget.onHome,
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.sunHorizon,
                    label: 'Today',
                    onTap: () => widget.onUtility('today'),
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.whatsappLogo,
                    label: 'WhatsApp',
                    onTap: () => widget.onUtility('whatsapp'),
                  ),
                  if (widget.onProject != null) ..._projectRows(context),
                  const SizedBox(height: 14),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(12, 0, 0, 2),
                    child: Row(
                      children: [
                        Expanded(
                          child: Text(
                            'Recents',
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w600,
                              color: JarvisColors.of(context).inkSoft,
                            ),
                          ),
                        ),
                        TextButton(
                          onPressed: widget.onSeeAll,
                          style: TextButton.styleFrom(
                            foregroundColor: JarvisColors.of(context).muted,
                            visualDensity: VisualDensity.compact,
                            textStyle: Theme.of(
                              context,
                            ).textTheme.labelLarge?.copyWith(fontSize: 13),
                          ),
                          child: const Text('See all'),
                        ),
                      ],
                    ),
                  ),
                  if (groups.isEmpty)
                    Padding(
                      padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
                      child: Text(
                        'Your conversations will appear here.',
                        style: TextStyle(
                          fontSize: 13.5,
                          color: JarvisColors.of(context).muted,
                        ),
                      ),
                    ),
                  for (final (label, items) in groups) ...[
                    Padding(
                      padding: const EdgeInsets.fromLTRB(12, 12, 12, 4),
                      child: Text(
                        label,
                        style: TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w500,
                          color: JarvisColors.of(context).muted,
                        ),
                      ),
                    ),
                    for (final conversation in items)
                      _ConversationRow(
                        title:
                            asJsonString(conversation['title']) ??
                            'New conversation',
                        selected:
                            conversation['id'] == widget.selectedConversationId,
                        onTap: () {
                          final id = jsonString(conversation, 'id');
                          if (id != null) {
                            widget.onConversation(id);
                          }
                        },
                      ),
                  ],
                ],
              ),
            ),
            const Divider(),
            _SettingsRow(connected: widget.connected, onTap: widget.onSettings),
          ],
        ),
      ),
    );
  }
}

extension on _JarvisSidebarState {
  List<Widget> _projectRows(BuildContext context) {
    final colors = JarvisColors.of(context);
    final projects = widget.projects
        .where((item) => jsonString(item, 'id') != null)
        .take(JarvisSidebar.visibleProjects)
        .toList();
    return [
      const SizedBox(height: 14),
      Padding(
        padding: const EdgeInsets.fromLTRB(12, 0, 0, 2),
        child: Row(
          children: [
            Expanded(
              child: Text(
                'Projects',
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                  color: colors.inkSoft,
                ),
              ),
            ),
            if (widget.projects.isNotEmpty)
              TextButton(
                onPressed: widget.onAllProjects,
                style: TextButton.styleFrom(
                  foregroundColor: colors.muted,
                  visualDensity: VisualDensity.compact,
                  textStyle: Theme.of(
                    context,
                  ).textTheme.labelLarge?.copyWith(fontSize: 13),
                ),
                child: const Text('See all'),
              ),
            if (widget.onNewProject != null)
              IconButton(
                tooltip: 'New project',
                visualDensity: VisualDensity.compact,
                onPressed: widget.onNewProject,
                icon: Icon(
                  PhosphorIconsRegular.plus,
                  size: 16,
                  color: colors.muted,
                ),
              ),
          ],
        ),
      ),
      if (projects.isEmpty)
        _NavRow(
          icon: PhosphorIconsRegular.folderSimple,
          label: 'New project',
          muted: true,
          onTap: widget.onNewProject ?? widget.onAllProjects ?? () {},
        ),
      for (final project in projects)
        _NavRow(
          leading: ProjectBadge(
            color: asJsonString(project['color']),
            size: 22,
          ),
          label: asJsonString(project['name']) ?? 'Untitled project',
          selected: jsonString(project, 'id') == widget.selectedProjectId,
          onTap: () => widget.onProject!(jsonString(project, 'id')!),
        ),
    ];
  }
}

/// One way in to search: chats, memories, files, and everything else.
class _SearchPill extends StatelessWidget {
  const _SearchPill({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      label: 'Ask or find with Jarvis',
      excludeSemantics: true,
      child: Material(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(12),
        child: InkWell(
          borderRadius: BorderRadius.circular(12),
          onTap: () {
            FocusManager.instance.primaryFocus?.unfocus();
            onTap();
          },
          child: SizedBox(
            height: 42,
            child: Row(
              children: [
                const SizedBox(width: 12),
                Icon(
                  PhosphorIconsRegular.magnifyingGlass,
                  size: 18,
                  color: colors.muted,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    'Ask or find',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontSize: 14.5, color: colors.muted),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _NavRow extends StatelessWidget {
  const _NavRow({
    required this.label,
    required this.onTap,
    this.icon,
    this.leading,
    this.selected = false,
    this.muted = false,
  });

  final String label;
  final VoidCallback onTap;
  final IconData? icon;
  final Widget? leading;
  final bool selected;

  /// A quieter row, used for suggestions such as "New project".
  final bool muted;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 1),
    child: Material(
      color: selected
          ? JarvisColors.of(context).surfaceRaised
          : Colors.transparent,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
          child: Row(
            children: [
              SizedBox.square(
                dimension: 22,
                child: Center(
                  child:
                      leading ??
                      Icon(
                        icon,
                        size: 20,
                        color: muted
                            ? JarvisColors.of(context).muted
                            : JarvisColors.of(context).ink,
                      ),
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w500,
                    letterSpacing: -.2,
                    color: muted
                        ? JarvisColors.of(context).muted
                        : JarvisColors.of(context).ink,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class _ConversationRow extends StatelessWidget {
  const _ConversationRow({
    required this.title,
    required this.selected,
    required this.onTap,
  });

  final String title;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
    color: selected
        ? JarvisColors.of(context).surfaceRaised
        : Colors.transparent,
    borderRadius: BorderRadius.circular(10),
    child: InkWell(
      borderRadius: BorderRadius.circular(10),
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
        child: Text(
          title,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            fontSize: 14.5,
            letterSpacing: -.1,
            color: selected
                ? JarvisColors.of(context).ink
                : JarvisColors.of(context).inkSoft,
            fontWeight: selected ? FontWeight.w500 : FontWeight.w400,
          ),
        ),
      ),
    ),
  );
}

class _SettingsRow extends StatelessWidget {
  const _SettingsRow({required this.connected, required this.onTap});

  final bool connected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
    onTap: onTap,
    child: Padding(
      padding: const EdgeInsets.fromLTRB(20, 12, 16, 12),
      child: Row(
        children: [
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(
              color: JarvisColors.of(context).ink,
              shape: BoxShape.circle,
            ),
            child: Icon(
              PhosphorIconsRegular.user,
              size: 17,
              color: JarvisColors.of(context).onInk,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Settings',
                  style: TextStyle(
                    fontSize: 14.5,
                    fontWeight: FontWeight.w500,
                    color: JarvisColors.of(context).ink,
                  ),
                ),
                const SizedBox(height: 1),
                Row(
                  children: [
                    Container(
                      width: 6,
                      height: 6,
                      decoration: BoxDecoration(
                        color: connected
                            ? JarvisColors.of(context).success
                            : JarvisColors.of(context).muted,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      connected ? 'Connected' : 'Offline',
                      style: TextStyle(
                        fontSize: 12.5,
                        color: JarvisColors.of(context).muted,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
          Icon(
            PhosphorIconsRegular.gearSix,
            size: 19,
            color: JarvisColors.of(context).inkSoft,
          ),
        ],
      ),
    ),
  );
}
