import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:geolocator/geolocator.dart';

import '../../json_maps.dart';

/// True when at least one reminder in [reminders] is waiting for a place.
bool hasPendingPlaceReminder(Iterable<Map<String, dynamic>> reminders) =>
    reminders.any(
      (reminder) =>
          (asJsonString(reminder['status']) ?? 'pending') == 'pending' &&
          jsonObject(reminder['place']) != null,
    );

/// Whether a new fix is worth sending: the phone moved far enough, or the
/// last report is old enough that the server should hear from it again.
bool shouldReportPosition({
  required ({double latitude, double longitude, DateTime at})? last,
  required double latitude,
  required double longitude,
  required DateTime at,
}) {
  if (last == null) return true;
  if (at.difference(last.at) >= const Duration(minutes: 5)) return true;
  if (at.difference(last.at) < const Duration(seconds: 20)) return false;
  return Geolocator.distanceBetween(
        last.latitude,
        last.longitude,
        latitude,
        longitude,
      ) >=
      60;
}

/// Follows the phone's position while the owner has place reminders, and
/// reports it so the server can fire "when I arrive at …" reminders.
///
/// It only runs with location permission already granted. With "Always" on
/// iOS it keeps running in the background; otherwise it works while Jarvis is
/// open. It stops as soon as no place reminder is waiting.
class PlaceReminderTracker {
  PlaceReminderTracker._();

  static final instance = PlaceReminderTracker._();

  Dio? _http;
  StreamSubscription<Position>? _positions;
  bool _background = false;
  ({double latitude, double longitude, DateTime at})? _lastReport;
  int _generation = 0;

  bool get isTracking => _positions != null;

  /// Fetches reminders with [http] and starts or stops following the phone.
  Future<void> refresh(Dio http) async {
    _http = http;
    final generation = ++_generation;
    try {
      final response = await http.get<dynamic>('/api/v1/reminders');
      if (generation != _generation) return;
      await update(jsonMaps(response.data), http: http);
    } catch (_) {
      // Keep whatever state we had; the next refresh tries again.
    }
  }

  /// Starts or stops following the phone for an already loaded list.
  Future<void> update(List<Map<String, dynamic>> reminders, {Dio? http}) async {
    if (http != null) _http = http;
    if (!hasPendingPlaceReminder(reminders)) {
      await stop();
      return;
    }
    await _start();
  }

  Future<void> _start() async {
    if (_http == null || kIsWeb) return;
    LocationPermission permission;
    try {
      if (!await Geolocator.isLocationServiceEnabled()) return;
      permission = await Geolocator.checkPermission();
    } catch (_) {
      return;
    }
    if (permission != LocationPermission.whileInUse &&
        permission != LocationPermission.always) {
      await stop();
      return;
    }
    final background = permission == LocationPermission.always;
    if (_positions != null && background == _background) return;
    await _positions?.cancel();
    _background = background;
    _positions = Geolocator.getPositionStream(
      locationSettings: _settings(background),
    ).listen(_report, onError: (_) => unawaited(stop()), cancelOnError: true);
  }

  static LocationSettings _settings(bool background) {
    const distance = 50;
    switch (defaultTargetPlatform) {
      case TargetPlatform.iOS:
      case TargetPlatform.macOS:
        return AppleSettings(
          accuracy: LocationAccuracy.medium,
          distanceFilter: distance,
          activityType: ActivityType.other,
          pauseLocationUpdatesAutomatically: true,
          allowBackgroundLocationUpdates: background,
          showBackgroundLocationIndicator: false,
        );
      case TargetPlatform.android:
        return AndroidSettings(
          accuracy: LocationAccuracy.medium,
          distanceFilter: distance,
          intervalDuration: const Duration(seconds: 30),
        );
      default:
        return const LocationSettings(
          accuracy: LocationAccuracy.medium,
          distanceFilter: distance,
        );
    }
  }

  Future<void> _report(Position position) async {
    final http = _http;
    if (http == null) return;
    final now = DateTime.now();
    if (!shouldReportPosition(
      last: _lastReport,
      latitude: position.latitude,
      longitude: position.longitude,
      at: now,
    )) {
      return;
    }
    _lastReport = (
      latitude: position.latitude,
      longitude: position.longitude,
      at: now,
    );
    try {
      await http.post<void>(
        '/api/v1/devices/telemetry',
        data: {
          'latitude': position.latitude,
          'longitude': position.longitude,
          if (position.accuracy.isFinite) 'accuracyMeters': position.accuracy,
        },
      );
    } catch (_) {
      // A missed report is retried by the next movement.
    }
  }

  /// Stops following the phone, for example after sign-out.
  Future<void> stop() async {
    final positions = _positions;
    _positions = null;
    _lastReport = null;
    await positions?.cancel();
  }

  /// Forgets the session's HTTP client so nothing is sent after sign-out.
  Future<void> detach() async {
    _generation++;
    _http = null;
    await stop();
  }
}
