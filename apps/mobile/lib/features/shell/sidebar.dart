import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Primary navigation: a drawer on phones and a permanent sidebar on wide
/// screens. Destinations sit on top, recent conversations below, grouped by
/// day, and settings with connection status at the bottom.
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
    super.key,
  });

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

  @override
  State<JarvisSidebar> createState() => _JarvisSidebarState();
}

class _JarvisSidebarState extends State<JarvisSidebar> {
  final _search = TextEditingController();
  final _searchFocus = FocusNode();

  void _leaveSearch(VoidCallback action) {
    _searchFocus.unfocus();
    FocusManager.instance.primaryFocus?.unfocus();
    action();
  }

  @override
  void dispose() {
    _search.dispose();
    _searchFocus.dispose();
    super.dispose();
  }

  List<(String, List<Map<String, dynamic>>)> _groups() {
    final query = _search.text.trim().toLowerCase();
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final buckets = <String, List<Map<String, dynamic>>>{};
    for (final conversation in widget.conversations) {
      if (jsonString(conversation, 'id') == null) continue;
      final title = (asJsonString(conversation['title']) ?? '').toLowerCase();
      if (query.isNotEmpty && !title.contains(query)) continue;
      final updated = DateTime.tryParse(
        asJsonString(conversation['updatedAt']) ?? '',
      )?.toLocal();
      final day = updated == null
          ? null
          : DateTime(updated.year, updated.month, updated.day);
      final age = day == null ? 999 : today.difference(day).inDays;
      final label = switch (age) {
        <= 0 => 'Today',
        1 => 'Yesterday',
        < 7 => 'Previous 7 days',
        _ => 'Older',
      };
      buckets.putIfAbsent(label, () => []).add(conversation);
    }
    return [
      for (final label in ['Today', 'Yesterday', 'Previous 7 days', 'Older'])
        if (buckets[label] case final items?) (label, items),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final groups = _groups();
    return ColoredBox(
      color: JarvisColors.canvas,
      child: SafeArea(
        right: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 10),
              child: Row(
                children: [
                  Expanded(
                    child: SizedBox(
                      height: 42,
                      child: TextField(
                        controller: _search,
                        focusNode: _searchFocus,
                        onChanged: (_) => setState(() {}),
                        textInputAction: TextInputAction.search,
                        style: const TextStyle(fontSize: 14.5),
                        decoration: const InputDecoration(
                          hintText: 'Search',
                          prefixIcon: Icon(
                            PhosphorIconsRegular.magnifyingGlass,
                            size: 18,
                          ),
                          fillColor: JarvisColors.surfaceMuted,
                          contentPadding: EdgeInsets.zero,
                          enabledBorder: OutlineInputBorder(
                            borderRadius: BorderRadius.all(Radius.circular(12)),
                            borderSide: BorderSide.none,
                          ),
                          focusedBorder: OutlineInputBorder(
                            borderRadius: BorderRadius.all(Radius.circular(12)),
                            borderSide: BorderSide(color: JarvisColors.outline),
                          ),
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  CircleIconButton(
                    icon: PhosphorIconsRegular.notePencil,
                    tooltip: 'New chat',
                    size: 42,
                    onPressed: () => _leaveSearch(widget.onNewChat),
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
                    onTap: () => _leaveSearch(widget.onHome),
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.waveform,
                    label: 'Voice',
                    onTap: () => _leaveSearch(widget.onVoice),
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.listChecks,
                    label: 'Tasks',
                    onTap: () => _leaveSearch(() => widget.onUtility('tasks')),
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.notebook,
                    label: 'Memory',
                    onTap: () => _leaveSearch(() => widget.onUtility('memory')),
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.bell,
                    label: 'Reminders',
                    onTap: () =>
                        _leaveSearch(() => widget.onUtility('reminders')),
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.folderSimple,
                    label: 'Files',
                    onTap: () => _leaveSearch(() => widget.onUtility('files')),
                  ),
                  _NavRow(
                    icon: PhosphorIconsRegular.chartBar,
                    label: 'Usage',
                    onTap: () => _leaveSearch(() => widget.onUtility('usage')),
                  ),
                  const SizedBox(height: 18),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(12, 0, 0, 2),
                    child: Row(
                      children: [
                        const Expanded(
                          child: Text(
                            'Recents',
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w600,
                              color: JarvisColors.inkSoft,
                            ),
                          ),
                        ),
                        TextButton(
                          onPressed: () => _leaveSearch(widget.onSeeAll),
                          style: TextButton.styleFrom(
                            foregroundColor: JarvisColors.muted,
                            visualDensity: VisualDensity.compact,
                            textStyle: const TextStyle(fontSize: 13),
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
                        _search.text.isEmpty
                            ? 'Your conversations will appear here.'
                            : 'No conversations match “${_search.text.trim()}”.',
                        style: const TextStyle(
                          fontSize: 13.5,
                          color: JarvisColors.muted,
                        ),
                      ),
                    ),
                  for (final (label, items) in groups) ...[
                    Padding(
                      padding: const EdgeInsets.fromLTRB(12, 12, 12, 4),
                      child: Text(
                        label,
                        style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w500,
                          color: JarvisColors.muted,
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
                            _leaveSearch(() => widget.onConversation(id));
                          }
                        },
                      ),
                  ],
                ],
              ),
            ),
            const Divider(),
            _SettingsRow(
              connected: widget.connected,
              onTap: () => _leaveSearch(widget.onSettings),
            ),
          ],
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
  });

  final String label;
  final VoidCallback onTap;
  final IconData? icon;
  final Widget? leading;
  final bool selected;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 1),
    child: Material(
      color: selected ? JarvisColors.surfaceRaised : Colors.transparent,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
          child: Row(
            children: [
              SizedBox.square(
                dimension: 22,
                child: Center(
                  child:
                      leading ?? Icon(icon, size: 20, color: JarvisColors.ink),
                ),
              ),
              const SizedBox(width: 14),
              Text(
                label,
                style: const TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w500,
                  letterSpacing: -.2,
                  color: JarvisColors.ink,
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
    color: selected ? JarvisColors.surfaceRaised : Colors.transparent,
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
            color: selected ? JarvisColors.ink : JarvisColors.inkSoft,
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
            decoration: const BoxDecoration(
              color: JarvisColors.ink,
              shape: BoxShape.circle,
            ),
            child: const Icon(
              PhosphorIconsRegular.user,
              size: 17,
              color: Colors.white,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Settings',
                  style: TextStyle(
                    fontSize: 14.5,
                    fontWeight: FontWeight.w500,
                    color: JarvisColors.ink,
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
                            ? JarvisColors.success
                            : JarvisColors.muted,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      connected ? 'Connected' : 'Offline',
                      style: const TextStyle(
                        fontSize: 12.5,
                        color: JarvisColors.muted,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
          const Icon(
            PhosphorIconsRegular.gearSix,
            size: 19,
            color: JarvisColors.inkSoft,
          ),
        ],
      ),
    ),
  );
}
