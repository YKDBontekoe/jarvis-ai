import 'dart:async';
import 'dart:convert';

import 'package:flutter/rendering.dart';
import 'package:flutter/widgets.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Where someone stopped reading in a conversation: the message at the top of
/// the screen and how far into it they were.
typedef ReadingSpot = ({String messageId, double within});

/// Reading spots per conversation, kept on this device so a long chat opens
/// where it was left. Cleared on sign-out.
class ReadingPositions {
  ReadingPositions._(this._prefs, this._spots);

  ReadingPositions.memory() : this._(null, {});

  static const storageKey = 'jarvis.reading_positions.v1';
  static const maxItems = 30;
  static const expiry = Duration(days: 30);

  final SharedPreferences? _prefs;
  final Map<String, ({ReadingSpot spot, DateTime savedAt})> _spots;

  static Future<ReadingPositions> open({DateTime? now}) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      return ReadingPositions._(
        prefs,
        _decode(prefs.getString(storageKey), now ?? DateTime.now()),
      );
    } catch (_) {
      return ReadingPositions.memory();
    }
  }

  static Future<void> clearAll() async {
    try {
      await (await SharedPreferences.getInstance()).remove(storageKey);
    } catch (_) {
      // Nothing was stored when device storage is unavailable.
    }
  }

  static Map<String, ({ReadingSpot spot, DateTime savedAt})> _decode(
    String? raw,
    DateTime now,
  ) {
    if (raw == null || raw.isEmpty) return {};
    try {
      final decoded = jsonDecode(raw);
      if (decoded is! Map) return {};
      final result = <String, ({ReadingSpot spot, DateTime savedAt})>{};
      for (final entry in decoded.entries) {
        final value = entry.value;
        if (entry.key is! String || value is! Map) continue;
        final id = value['m'];
        final within = value['w'];
        final at = DateTime.tryParse('${value['t']}');
        if (id is! String || within is! num || at == null) continue;
        if (now.difference(at) > expiry) continue;
        result[entry.key as String] = (
          spot: (messageId: id, within: within.toDouble()),
          savedAt: at,
        );
      }
      return result;
    } catch (_) {
      return {};
    }
  }

  ReadingSpot? read(String conversationId) => _spots[conversationId]?.spot;

  void write(String conversationId, ReadingSpot spot, {DateTime? now}) {
    _spots.remove(conversationId);
    _spots[conversationId] = (spot: spot, savedAt: now ?? DateTime.now());
    while (_spots.length > maxItems) {
      _spots.remove(_spots.keys.first);
    }
  }

  void remove(String conversationId) => _spots.remove(conversationId);

  Future<void> flush() async {
    final prefs = _prefs;
    if (prefs == null) return;
    try {
      if (_spots.isEmpty) {
        await prefs.remove(storageKey);
      } else {
        await prefs.setString(
          storageKey,
          jsonEncode({
            for (final entry in _spots.entries)
              entry.key: {
                'm': entry.value.spot.messageId,
                'w': entry.value.spot.within,
                't': entry.value.savedAt.toUtc().toIso8601String(),
              },
          }),
        );
      }
    } catch (_) {
      // A spot that cannot be saved still works for this session.
    }
  }
}

/// Finds the message at the top of a transcript and scrolls back to one. Every
/// message with an id wears a [GlobalKey] from [keyFor], so its place on screen
/// can be read; messages that are not built are simply not candidates.
class TranscriptAnchors {
  final Map<String, GlobalKey> _keys = {};

  GlobalKey keyFor(String messageId) =>
      _keys.putIfAbsent(messageId, GlobalKey.new);

  void reset() => _keys.clear();

  /// The built message whose top is closest above (or at) the top of the
  /// screen, with how far the screen is below that top.
  ReadingSpot? anchor(ScrollController controller) {
    if (!controller.hasClients) return null;
    final pixels = controller.position.pixels;
    ReadingSpot? best;
    var bestTop = double.negativeInfinity;
    for (final entry in _keys.entries) {
      final top = _topOf(entry.value);
      if (top == null || top > pixels + 1) continue;
      if (top > bestTop) {
        bestTop = top;
        best = (messageId: entry.key, within: pixels - top);
      }
    }
    return best;
  }

  double? _topOf(GlobalKey key) {
    final object = key.currentContext?.findRenderObject();
    if (object == null || !object.attached) return null;
    return RenderAbstractViewport.maybeOf(object)
        ?.getOffsetToReveal(object, 0)
        .offset;
  }

  /// Scrolls so [spot] is where it was. [estimate] is a rough pixel offset to
  /// move to first when the message is not built yet (it is lazily built), after
  /// which the exact spot is found over a few frames. Returns true once placed.
  Future<bool> reveal(
    ScrollController controller,
    ReadingSpot spot, {
    required double Function() estimate,
    int attempts = 8,

    /// Waits for the next frame; replaced in tests that pump frames themselves.
    Future<void> Function()? nextFrame,
  }) async {
    for (var i = 0; i < attempts; i++) {
      if (!controller.hasClients) return false;
      final key = _keys[spot.messageId];
      final top = key == null ? null : _topOf(key);
      if (top != null) {
        controller.jumpTo(
          (top + spot.within).clamp(0.0, controller.position.maxScrollExtent),
        );
        return true;
      }
      // Not built yet: move near it so it is, then look again next frame.
      controller.jumpTo(
        estimate().clamp(0.0, controller.position.maxScrollExtent),
      );
      await (nextFrame?.call() ?? WidgetsBinding.instance.endOfFrame);
    }
    return false;
  }
}
