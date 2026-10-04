import 'package:flutter/cupertino.dart';

/// A single, optically consistent iOS symbol family for the main destinations.
enum TabGlyph { home, chats, everything, you }

class TabIcon extends StatelessWidget {
  const TabIcon({
    required this.glyph,
    required this.color,
    this.selected = false,
    this.size = 25,
    super.key,
  });

  final TabGlyph glyph;
  final Color color;
  final bool selected;
  final double size;

  @override
  Widget build(BuildContext context) => Icon(
    switch (glyph) {
      TabGlyph.home =>
        selected ? CupertinoIcons.house_fill : CupertinoIcons.house,
      TabGlyph.chats =>
        selected
            ? CupertinoIcons.chat_bubble_2_fill
            : CupertinoIcons.chat_bubble_2,
      TabGlyph.everything =>
        selected
            ? CupertinoIcons.square_grid_2x2_fill
            : CupertinoIcons.square_grid_2x2,
      TabGlyph.you =>
        selected
            ? CupertinoIcons.person_crop_circle_fill
            : CupertinoIcons.person_crop_circle,
    },
    size: size,
    color: color,
  );
}
