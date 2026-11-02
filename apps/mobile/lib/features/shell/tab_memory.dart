import 'package:flutter/material.dart';

import '../chats/chat_list.dart';
import 'jarvis_tab_bar.dart';

/// Where one tab's main list was scrolled to. Each time the tab's page is built
/// it asks the slot for a controller that starts at the remembered spot, and
/// hands it back when the page goes away. Two pages of the same tab can exist
/// side by side for a moment during a transition, so every page gets its own.
class TabScrollSlot {
  double offset = 0;
  final List<ScrollController> _live = [];

  /// A controller that opens where the list was left and remembers every move.
  ScrollController create() {
    final controller = ScrollController(initialScrollOffset: offset);
    controller.addListener(() {
      if (controller.hasClients && controller.positions.length == 1) {
        offset = controller.position.pixels;
      }
    });
    _live.add(controller);
    return controller;
  }

  /// Disposes a controller from [create] once its page is gone.
  void release(ScrollController controller) {
    _live.remove(controller);
    controller.dispose();
  }

  /// Scrolls the list that is showing back to the top.
  Future<void> scrollToTop() async {
    for (final controller in _live.reversed) {
      if (!controller.hasClients || controller.positions.length != 1) continue;
      if (controller.position.pixels <= 0) return;
      await controller.animateTo(
        0,
        duration: const Duration(milliseconds: 320),
        curve: Curves.easeOutCubic,
      );
      return;
    }
  }
}

/// What each tab was showing when the owner left it: how far its list was
/// scrolled, and the search and filter chosen there. Lives for the session
/// and is cleared on sign-out.
class TabMemory {
  final Map<JarvisTab, TabScrollSlot> _slots = {};

  /// The Chats filter and the search text of Chats and Everything.
  ChatFilter chatsFilter = ChatFilter.forYou;
  String chatsQuery = '';
  String everythingQuery = '';

  TabScrollSlot slotFor(JarvisTab tab) =>
      _slots.putIfAbsent(tab, TabScrollSlot.new);

  double offsetOf(JarvisTab tab) => _slots[tab]?.offset ?? 0;

  /// Tapping the tab you are already on: back to the top of its list.
  Future<void> scrollToTop(JarvisTab tab) async => _slots[tab]?.scrollToTop();

  /// Forgets everything, for example on sign-out.
  void clear() {
    for (final slot in _slots.values) {
      slot.offset = 0;
    }
    chatsFilter = ChatFilter.forYou;
    chatsQuery = '';
    everythingQuery = '';
  }
}
