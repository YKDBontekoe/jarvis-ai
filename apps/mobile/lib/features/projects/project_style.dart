import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';

/// Accent colors a project can carry, in picker order. Matches the server list.
const projectColorKeys = [
  'blue',
  'green',
  'orange',
  'pink',
  'purple',
  'teal',
  'gray',
];

/// The tone of a project's accent, tuned separately for light and dark.
Color projectColor(BuildContext context, String? key) {
  final dark = JarvisColors.of(context).isDark;
  return switch (key) {
    'green' => dark ? const Color(0xff4ade80) : const Color(0xff16a34a),
    'orange' => dark ? const Color(0xfffb923c) : const Color(0xffea580c),
    'pink' => dark ? const Color(0xfff472b6) : const Color(0xffdb2777),
    'purple' => dark ? const Color(0xffa78bfa) : const Color(0xff7c3aed),
    'teal' => dark ? const Color(0xff2dd4bf) : const Color(0xff0d9488),
    'gray' => dark ? const Color(0xffa8a29e) : const Color(0xff78716c),
    _ => dark ? const Color(0xff60a5fa) : const Color(0xff2563eb),
  };
}

String projectColorName(String key) =>
    key.isEmpty ? key : key[0].toUpperCase() + key.substring(1);

/// "3 chats · 2 files · 1 task", leaving out what the project does not hold.
String projectCountsLabel(Map<String, dynamic> project) {
  String part(String key, String one, String many) {
    final count = asJsonInt(project[key]);
    return count == 0 ? '' : '$count ${count == 1 ? one : many}';
  }

  final parts = [
    part('conversationCount', 'chat', 'chats'),
    part('fileCount', 'file', 'files'),
    part('taskCount', 'task', 'tasks'),
  ].where((item) => item.isNotEmpty).toList();
  return parts.isEmpty ? 'Empty' : parts.join(' · ');
}

/// A tinted rounded square with a folder in the project's color.
class ProjectBadge extends StatelessWidget {
  const ProjectBadge({required this.color, this.size = 36, super.key});

  final String? color;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tone = projectColor(context, color);
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: tone.withValues(
          alpha: JarvisColors.of(context).isDark ? .2 : .12,
        ),
        borderRadius: BorderRadius.circular(size * .3),
      ),
      child: Icon(PhosphorIconsFill.folderSimple, size: size * .5, color: tone),
    );
  }
}
