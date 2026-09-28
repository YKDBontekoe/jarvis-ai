import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../http_urls.dart';
import '../../ui/jarvis_ui.dart';
import 'device_sensors.dart';

Future<({String? result, String? error})> performDeviceCapability({
  required String capability,
  required Map<Object?, Object?>? event,
  required bool mounted,
  required BuildContext context,
}) async {
  try {
    switch (capability) {
      case 'clipboard':
        final data = await Clipboard.getData('text/plain');
        final text = (data?.text ?? '').trim();
        return (
          result: text.isEmpty ? 'The clipboard is empty.' : text,
          error: null,
        );
      case 'open_url':
        final args = event?['arguments'];
        final url = args is Map ? asJsonString(args['url']) : null;
        if (url == null) {
          return (result: null, error: 'No URL was provided.');
        }
        final parsed = parseHttpUrl(url);
        if (parsed == null) {
          return (result: null, error: 'That URL cannot be opened.');
        }
        if (!mounted) {
          return (result: null, error: 'Could not open that URL.');
        }
        final allowed = await showJarvisConfirm(
          context,
          title: 'Open this link?',
          message: url,
          confirmLabel: 'Open',
        );
        if (!allowed) {
          return (result: 'The owner declined to open $url.', error: null);
        }
        final opened = await launchHttpUrl(parsed);
        if (!opened) {
          return (result: null, error: 'That URL cannot be opened.');
        }
        return (result: 'Opened $url', error: null);
      case 'notify':
        final args = event?['arguments'];
        final title = args is Map
            ? asJsonString(args['title']) ?? 'Jarvis'
            : 'Jarvis';
        final body = args is Map ? asJsonString(args['body']) ?? '' : '';
        if (mounted) {
          ScaffoldMessenger.of(
            context,
          ).showSnackBar(SnackBar(content: Text('$title\n$body')));
        }
        return (result: 'Shown on this device.', error: null);
      case 'battery':
        return readDeviceBattery();
      case 'location':
        return readDeviceLocation();
      default:
        return (
          result: null,
          error: 'This device does not handle $capability.',
        );
    }
  } catch (_) {
    return (result: null, error: 'The device could not complete $capability.');
  }
}
