import 'package:flutter/foundation.dart';

/// A request to open something, from outside the normal flow of the app: a
/// notification tap, the home screen widget, an app-icon shortcut, a Shortcuts
/// action or a saved capture.
@immutable
class IncomingRoute {
  const IncomingRoute({
    required this.source,
    required this.destination,
    required this.id,
    this.run,
  });

  /// Where it came from: `notification`, `widget`, `shortcut`, `capture`.
  final String source;

  /// What to open: `ask`, `voice`, `chats`, `home`, `chat:<id>`, `captures`, or
  /// any destination the shell's utility router knows (`reminders/new`, …).
  final String destination;

  /// Identity for de-duplication: the same id offered twice opens once.
  final String id;

  /// A source that already knows how to open its thing supplies it here.
  final VoidCallback? run;
}

/// The one place every outside request to open something waits, so it is
/// opened exactly once and only when the app can show it: after the session is
/// restored, after sign-in, and after the app lock is opened. Nothing is dropped
/// while the app is still starting or locked.
class IncomingRoutes extends ChangeNotifier {
  IncomingRoutes({DateTime Function()? clock}) : _clock = clock ?? DateTime.now;

  static final instance = IncomingRoutes();

  /// The same request arriving twice inside this window (a launch message plus
  /// an opened-app event, a double tap) counts as one.
  static const dedupeWindow = Duration(seconds: 5);

  final DateTime Function() _clock;
  IncomingRoute? _pending;
  final Map<String, DateTime> _handled = {};

  bool get hasPending => _pending != null;
  IncomingRoute? get pending => _pending;

  /// Queues [destination]. Returns false when it duplicates one that is waiting
  /// or was just opened.
  bool offer(
    String source,
    String destination, {
    String? id,
    VoidCallback? run,
  }) {
    final key = id ?? destination;
    if (_pending?.id == key) return false;
    final handledAt = _handled[key];
    if (handledAt != null && _clock().difference(handledAt) < dedupeWindow) {
      return false;
    }
    _pending = IncomingRoute(
      source: source,
      destination: destination,
      id: key,
      run: run,
    );
    notifyListeners();
    return true;
  }

  /// Hands out the waiting request, once, and remembers it was opened.
  IncomingRoute? take() {
    final route = _pending;
    if (route == null) return null;
    _pending = null;
    final now = _clock();
    _handled[route.id] = now;
    _handled.removeWhere((_, at) => now.difference(at) > dedupeWindow * 4);
    return route;
  }

  /// Forgets everything waiting, for example on sign-out.
  void clear() {
    _pending = null;
    _handled.clear();
  }
}
