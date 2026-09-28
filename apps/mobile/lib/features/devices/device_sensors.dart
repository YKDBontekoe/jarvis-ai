import 'package:battery_plus/battery_plus.dart';
import 'package:geolocator/geolocator.dart';

Future<({String? result, String? error})> readDeviceBattery() async {
  try {
    final battery = Battery();
    final level = await battery.batteryLevel;
    final state = await battery.batteryState;
    final charging =
        state == BatteryState.charging || state == BatteryState.full;
    return (
      result:
          'Battery $level%${charging ? ', charging' : ', not charging'} (${state.name}).',
      error: null,
    );
  } catch (_) {
    return (
      result: null,
      error: 'Battery level is not available on this device.',
    );
  }
}

Future<({int? percent, bool? charging})> readDeviceBatterySnapshot() async {
  try {
    final battery = Battery();
    final level = await battery.batteryLevel;
    final state = await battery.batteryState;
    return (
      percent: level,
      charging: state == BatteryState.charging || state == BatteryState.full,
    );
  } catch (_) {
    return (percent: null, charging: null);
  }
}

Future<({String? result, String? error})> readDeviceLocation() async {
  try {
    final enabled = await Geolocator.isLocationServiceEnabled();
    if (!enabled) {
      return (
        result: 'Location services are turned off on this device.',
        error: null,
      );
    }
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }
    if (permission == LocationPermission.denied ||
        permission == LocationPermission.deniedForever ||
        permission == LocationPermission.unableToDetermine) {
      return (
        result:
            'Location permission was denied. Enable it in system settings, then try again.',
        error: null,
      );
    }
    final position = await Geolocator.getCurrentPosition(
      locationSettings: const LocationSettings(
        accuracy: LocationAccuracy.high,
        timeLimit: Duration(seconds: 20),
      ),
    );
    final accuracy = position.accuracy.isFinite
        ? ' (±${position.accuracy.round()} m)'
        : '';
    return (
      result:
          '${position.latitude.toStringAsFixed(5)}, ${position.longitude.toStringAsFixed(5)}$accuracy at ${position.timestamp.toUtc().toIso8601String()}.',
      error: null,
    );
  } catch (_) {
    return (
      result: null,
      error: 'Location is not available on this device right now.',
    );
  }
}

Future<({double? latitude, double? longitude, double? accuracy})>
readDeviceLocationSnapshot({bool requestPermission = true}) async {
  try {
    final enabled = await Geolocator.isLocationServiceEnabled();
    if (!enabled) return (latitude: null, longitude: null, accuracy: null);
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied && requestPermission) {
      permission = await Geolocator.requestPermission();
    }
    if (permission == LocationPermission.denied ||
        permission == LocationPermission.deniedForever ||
        permission == LocationPermission.unableToDetermine) {
      return (latitude: null, longitude: null, accuracy: null);
    }
    final position = await Geolocator.getCurrentPosition(
      locationSettings: const LocationSettings(
        accuracy: LocationAccuracy.medium,
        timeLimit: Duration(seconds: 15),
      ),
    );
    return (
      latitude: position.latitude,
      longitude: position.longitude,
      accuracy: position.accuracy.isFinite ? position.accuracy : null,
    );
  } catch (_) {
    return (latitude: null, longitude: null, accuracy: null);
  }
}
