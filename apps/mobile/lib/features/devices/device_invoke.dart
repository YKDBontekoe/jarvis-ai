import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../json_maps.dart';

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
        final parsed = Uri.tryParse(url);
        if (parsed == null ||
            !(parsed.isScheme('http') || parsed.isScheme('https'))) {
          return (result: null, error: 'That URL cannot be opened.');
        }
        await launchUrl(parsed, mode: LaunchMode.externalApplication);
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
        return (
          result: 'Battery level is not available in this client yet.',
          error: null,
        );
      case 'location':
        return (
          result:
              'Location is turned off or not available on this device build.',
          error: null,
        );
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
