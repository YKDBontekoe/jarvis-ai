import 'package:url_launcher/url_launcher.dart';

bool isHttpUrl(Uri uri) => uri.isScheme('http') || uri.isScheme('https');

/// Parses http(s) URLs only. `file:`, `javascript:`, and relative hrefs are null.
Uri? parseHttpUrl(String? raw) {
  if (raw == null || raw.isEmpty) return null;
  final uri = Uri.tryParse(raw);
  if (uri == null || !isHttpUrl(uri)) return null;
  return uri;
}

/// HTTPS URL with a host and no embedded credentials, for server-side polling.
Uri? parsePublicHttpsUrl(String? raw) {
  final uri = parseHttpUrl(raw?.trim());
  if (uri == null ||
      !uri.isScheme('https') ||
      uri.host.isEmpty ||
      uri.userInfo.isNotEmpty) {
    return null;
  }
  return uri;
}

Future<bool> launchHttpUrl(Uri uri) async {
  try {
    return await launchUrl(uri, mode: LaunchMode.externalApplication);
  } catch (_) {
    return false;
  }
}
