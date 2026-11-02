import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:quick_actions/quick_actions.dart';

import 'incoming_routes.dart';

/// One entry in the iPhone app-icon menu (touch and hold the icon).
@immutable
class AppShortcutDefinition {
  const AppShortcutDefinition(
    this.type,
    this.destination,
    this.titleEn,
    this.titleNl,
  );

  /// Stable id the system reports back when it is chosen.
  final String type;

  /// What it opens, as [IncomingRoutes] understands it.
  final String destination;
  final String titleEn;
  final String titleNl;

  String title(String languageCode) => languageCode == 'nl' ? titleNl : titleEn;
}

/// The app-icon menu: a new chat, voice, a new reminder, and the captures.
const appShortcuts = [
  AppShortcutDefinition('new_chat', 'ask', 'New chat', 'Nieuwe chat'),
  AppShortcutDefinition('talk', 'voice', 'Talk to Jarvis', 'Praat met Jarvis'),
  AppShortcutDefinition(
    'new_reminder',
    'reminders/new',
    'New reminder',
    'Nieuwe herinnering',
  ),
  AppShortcutDefinition('captures', 'captures', 'Captures', 'Opgeslagen items'),
];

/// The destination of a shortcut [type] the system reported, or null.
String? shortcutDestination(String type) {
  for (final shortcut in appShortcuts) {
    if (shortcut.type == type) return shortcut.destination;
  }
  return null;
}

/// Registers the app-icon menu. Choosing an item (including the one that
/// launches the app) goes into [IncomingRoutes], so it waits for sign-in and the
/// app lock and then opens once.
class AppShortcuts {
  const AppShortcuts._();

  /// Whether this platform has an app-icon menu.
  @visibleForTesting
  static bool Function() supported = () =>
      !kIsWeb &&
      (defaultTargetPlatform == TargetPlatform.iOS ||
          defaultTargetPlatform == TargetPlatform.android);

  static Future<void> install({
    String languageCode = 'en',
    IncomingRoutes? routes,
    Future<void> Function(void Function(String type) handler)? initialize,
    Future<void> Function(List<ShortcutItem> items)? setItems,
  }) async {
    if (!supported()) return;
    const actions = QuickActions();
    final target = routes ?? IncomingRoutes.instance;
    try {
      await (initialize ?? actions.initialize)((type) {
        final destination = shortcutDestination(type);
        if (destination != null) target.offer('shortcut', destination);
      });
      await (setItems ?? actions.setShortcutItems)([
        for (final shortcut in appShortcuts)
          ShortcutItem(
            type: shortcut.type,
            localizedTitle: shortcut.title(languageCode),
          ),
      ]);
    } catch (_) {
      // No app-icon menu on this device; the app works the same without it.
    }
  }

  /// Removes the menu, for example on sign-out, so it cannot open an account that is gone.
  static Future<void> clear({Future<void> Function()? clear}) async {
    if (!supported()) return;
    try {
      await (clear ?? const QuickActions().clearShortcutItems)();
    } catch (_) {
      // Nothing to remove.
    }
  }
}
