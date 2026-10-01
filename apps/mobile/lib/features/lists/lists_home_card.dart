import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'list_models.dart';

/// Home card with the lists that still have open items, or a short invitation
/// to start one when there are none.
class ListsHomeCard extends StatelessWidget {
  const ListsHomeCard({required this.lists, required this.onOpen, super.key});

  final List<PersonalListData> lists;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final active = [
      for (final list in lists)
        if (list.open.isNotEmpty) list,
    ];
    return SurfaceCard(
      key: const Key('home-lists'),
      onTap: onOpen,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.checkSquare, size: 40),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Lists',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      lists.isEmpty
                          ? 'Ask Jarvis to put milk on your shopping list.'
                          : active.isEmpty
                          ? 'Everything is checked off.'
                          : '${active.fold<int>(0, (sum, list) => sum + list.open.length)} open across ${active.length} ${active.length == 1 ? 'list' : 'lists'}',
                      style: TextStyle(
                        color: colors.inkSoft,
                        fontSize: 13,
                        height: 1.35,
                      ),
                    ),
                  ],
                ),
              ),
              Icon(
                PhosphorIconsRegular.caretRight,
                size: 16,
                color: colors.muted,
              ),
            ],
          ),
          for (final list in active.take(3)) ...[
            const SizedBox(height: 12),
            _ListLine(list: list),
          ],
        ],
      ),
    );
  }
}

class _ListLine extends StatelessWidget {
  const _ListLine({required this.list});

  final PersonalListData list;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final open = list.open;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 1),
          child: Icon(
            listKindIcon(list.kind),
            size: 16,
            color: listKindColor(colors, list.kind),
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Text.rich(
            TextSpan(
              children: [
                TextSpan(
                  text: '${list.name}  ',
                  style: TextStyle(
                    fontWeight: FontWeight.w600,
                    color: colors.ink,
                  ),
                ),
                TextSpan(
                  text:
                      open.take(3).map((item) => item.text).join(', ') +
                      (open.length > 3 ? ' +${open.length - 3}' : ''),
                  style: TextStyle(color: colors.inkSoft),
                ),
              ],
            ),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 13.5),
          ),
        ),
      ],
    );
  }
}
