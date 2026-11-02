import 'package:flutter/widgets.dart';
import 'package:share_plus/share_plus.dart';

/// Opens the system share sheet for a piece of text. The sheet is the phone's
/// own: Jarvis hands the text over and learns nothing about where it goes.
typedef TextSharer = Future<void> Function(String text, Rect? origin);

Future<void> _systemShare(String text, Rect? origin) async {
  await SharePlus.instance.share(
    ShareParams(text: text, sharePositionOrigin: origin),
  );
}

/// Replaceable in tests, where no platform share sheet exists.
TextSharer textSharer = _systemShare;

/// Shares [text], anchoring the iPad popover to [context]'s widget.
Future<void> shareText(BuildContext context, String text) {
  Rect? origin;
  final box = context.findRenderObject();
  if (box is RenderBox && box.hasSize) {
    origin = box.localToGlobal(Offset.zero) & box.size;
  }
  return textSharer(text, origin);
}
