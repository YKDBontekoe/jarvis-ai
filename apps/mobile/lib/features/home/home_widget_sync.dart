import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:home_widget/home_widget.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'home_context.dart';
import 'home_insights.dart';

/// What the iOS widget reads from the shared App Group, as plain values:
/// WidgetKit cannot call the API, so Home writes what it already shows.
@immutable
class HomeWidgetPayload {
  const HomeWidgetPayload(this.values);

  final Map<String, Object> values;

  /// The Right now card (or the next plan), how much needs you and the
  /// first three of those things.
  ///
  /// With [hideDetails] nothing personal is written: the widget shows only how
  /// many things need you, never their titles, names or the day's plan.
  static HomeWidgetPayload build({
    required DateTime now,
    HomeContext? context,
    List<NeedsYouItem> needsYou = const [],
    bool hideDetails = false,
  }) {
    final hero = context?.hero;
    if (hideDetails) {
      return HomeWidgetPayload({
        'hero_kind': 'hidden',
        'hero_title': 'Jarvis',
        'hero_detail': '',
        'day_line': '',
        'needs_count': needsYou.length,
        'needs_items': '[]',
        'hide_details': true,
        'updated_at': now.toUtc().millisecondsSinceEpoch,
      });
    }
    return HomeWidgetPayload({
      'hero_kind': hero?.kind ?? 'none',
      'hero_title': hero?.title ?? 'Nothing pressing',
      'hero_detail': hero?.detail ?? context?.dayLine ?? '',
      if (hero?.startsAt case final start?)
        'hero_starts_at': start.toUtc().millisecondsSinceEpoch,
      'day_line': context?.dayLine ?? '',
      'needs_count': needsYou.length,
      'needs_items': jsonEncode([
        for (final item in needsYou.take(3))
          {'title': item.title, 'detail': item.detail, 'kind': item.kind.name},
      ]),
      'hide_details': false,
      'updated_at': now.toUtc().millisecondsSinceEpoch,
    });
  }

  @override
  bool operator ==(Object other) =>
      other is HomeWidgetPayload &&
      mapEquals(
        {...values}..remove('updated_at'),
        {...other.values}..remove('updated_at'),
      );

  @override
  int get hashCode => Object.hashAll(
    [
      for (final entry in values.entries)
        if (entry.key != 'updated_at') Object.hash(entry.key, entry.value),
    ]..sort(),
  );
}

/// Keeps the iOS home screen widget (`ios/JarvisWidget`) in step with Home.
/// Does nothing on web, Android and desktop.
class HomeWidgetSync {
  HomeWidgetSync._();

  static final instance = HomeWidgetSync._();

  /// Shared with the widget extension; both targets need it in their
  /// entitlements.
  static const appGroup = 'group.com.example.jarvis_mobile';

  /// The widget's `kind` in `JarvisWidget.swift`.
  static const iOSName = 'JarvisWidget';

  /// Whether this platform has the widget.
  @visibleForTesting
  static bool Function() supported = () =>
      !kIsWeb && defaultTargetPlatform == TargetPlatform.iOS;

  HomeWidgetPayload? _last;
  bool _ready = false;

  /// Writes [payload] and asks WidgetKit to reload, unless nothing changed.
  Future<void> push(HomeWidgetPayload payload) async {
    if (!supported() || payload == _last) return;
    _last = payload;
    try {
      if (!_ready) {
        await HomeWidget.setAppGroupId(appGroup);
        _ready = true;
      }
      for (final MapEntry(:key, :value) in payload.values.entries) {
        await HomeWidget.saveWidgetData<Object>(key, value);
      }
      await HomeWidget.updateWidget(iOSName: iOSName);
    } catch (_) {
      // No widget installed, or the App Group is not set up yet.
      _last = null;
    }
  }

  /// Removes everything the widget was given: on sign-out, so the next person
  /// to see the home screen sees nothing of this account.
  Future<void> clear() async {
    _last = null;
    if (!supported()) return;
    try {
      if (!_ready) {
        await HomeWidget.setAppGroupId(appGroup);
        _ready = true;
      }
      for (final key in payloadKeys) {
        await HomeWidget.saveWidgetData<Object>(key, null);
      }
      await HomeWidget.updateWidget(iOSName: iOSName);
    } catch (_) {
      // No widget installed, or the App Group is not set up yet.
    }
  }

  /// Every key written for the widget, so [clear] removes all of them.
  static const payloadKeys = [
    'hero_kind',
    'hero_title',
    'hero_detail',
    'hero_starts_at',
    'day_line',
    'needs_count',
    'needs_items',
    'hide_details',
    'updated_at',
  ];

  /// Where a tap on the widget asked to go: `ask`, `voice`, `chats` or `home`,
  /// including the tap that launched the app.
  Stream<String> clicks() async* {
    if (!supported()) return;
    try {
      final first = await HomeWidget.initiallyLaunchedFromHomeWidget();
      if (widgetDestination(first) case final destination?) {
        yield destination;
      }
    } catch (_) {
      return;
    }
    await for (final uri in HomeWidget.widgetClicked) {
      if (widgetDestination(uri) case final destination?) yield destination;
    }
  }
}

/// `jarvis://ask?homeWidget` → `ask`. The same addresses are used by the
/// widgets, the Shortcuts app and the Share Extension. Anything else is ignored.
String? widgetDestination(Uri? uri) {
  if (uri == null || uri.scheme != 'jarvis') return null;
  final host = uri.host;
  return const {'ask', 'voice', 'chats', 'home', 'captures'}.contains(host)
      ? host
      : null;
}

/// Whether the widgets show only counts. Off by default; kept on this device.
class WidgetPrivacy {
  const WidgetPrivacy._();

  static const hideDetailsKey = 'jarvis.widget.hide_details';

  static Future<bool> hideDetails() async {
    try {
      return (await SharedPreferences.getInstance()).getBool(hideDetailsKey) ??
          false;
    } catch (_) {
      return false;
    }
  }

  static Future<void> setHideDetails(bool value) async {
    try {
      await (await SharedPreferences.getInstance()).setBool(
        hideDetailsKey,
        value,
      );
    } catch (_) {
      // The choice then lasts only until the app closes.
    }
  }
}
