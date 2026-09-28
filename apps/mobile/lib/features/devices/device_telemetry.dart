import 'package:dio/dio.dart';

import 'device_sensors.dart';

/// Posts the latest battery and (if already permitted) location snapshot so
/// device watches and the home briefing can use this phone without a live invoke.
Future<void> postDeviceTelemetry(Dio http) async {
  try {
    final battery = await readDeviceBatterySnapshot();
    final location = await readDeviceLocationSnapshot(requestPermission: false);
    if (battery.percent == null && location.latitude == null) return;
    await http.post<void>(
      '/api/v1/devices/telemetry',
      data: {
        if (location.latitude != null) 'latitude': location.latitude,
        if (location.longitude != null) 'longitude': location.longitude,
        if (location.accuracy != null) 'accuracyMeters': location.accuracy,
        if (battery.percent != null) 'batteryPercent': battery.percent,
        if (battery.charging != null) 'charging': battery.charging,
      },
    );
  } catch (_) {}
}
