import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../http_urls.dart';
import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';

/// Plain-language facts about an app Jarvis can connect to.
typedef AppInfo = ({String name, IconData icon, String? what});

/// Turns a server or credential name into a name, icon, and one-line
/// description a non-technical owner recognises. Shared by Connected apps,
/// the app detail screen, Home tiles and chat.
AppInfo appInfo(String raw, {bool selfBuilt = false}) {
  final key = raw.toLowerCase().replaceFirst('jarvis-pack-', '');
  if (selfBuilt) {
    return (
      name: _titleCaseName(raw, fallback: 'App'),
      icon: PhosphorIconsRegular.magicWand,
      what: 'Built by Jarvis for you.',
    );
  }
  if (key.contains('github')) {
    return (
      name: 'GitHub',
      icon: PhosphorIconsRegular.code,
      what: 'Look at your repositories, issues, and pull requests.',
    );
  }
  if (key.contains('home-assistant') ||
      key.contains('home assistant') ||
      key.contains('homeassistant')) {
    return (
      name: 'Home Assistant',
      icon: PhosphorIconsRegular.house,
      what: 'Check and control lights, sensors, and devices at home.',
    );
  }
  if (key.contains('calendar')) {
    return (
      name: _titleCaseName(raw, fallback: 'Calendar'),
      icon: PhosphorIconsRegular.calendarBlank,
      what: 'See your events, and add new ones if you allow it.',
    );
  }
  if (key.contains('mail')) {
    return (
      name: _titleCaseName(raw, fallback: 'Mail'),
      icon: PhosphorIconsRegular.paperPlaneTilt,
      what: 'Search your email and draft replies.',
    );
  }
  if (key.contains('contact')) {
    return (
      name: _titleCaseName(raw, fallback: 'Contacts'),
      icon: PhosphorIconsRegular.addressBook,
      what: 'Look up people you know.',
    );
  }
  if (key.contains('notion') || key.contains('note')) {
    return (
      name: _titleCaseName(raw, fallback: 'Notes'),
      icon: PhosphorIconsRegular.notebook,
      what: null,
    );
  }
  if (key.contains('search') || key.contains('brave')) {
    return (
      name: _titleCaseName(raw, fallback: 'Search'),
      icon: PhosphorIconsRegular.globe,
      what: null,
    );
  }
  if (key.contains('stripe') ||
      key.contains('paypal') ||
      key.contains('bank')) {
    return (
      name: _titleCaseName(raw, fallback: 'Payments'),
      icon: PhosphorIconsRegular.wallet,
      what: null,
    );
  }
  return (
    name: _titleCaseName(raw, fallback: 'App'),
    icon: PhosphorIconsRegular.plugsConnected,
    what: null,
  );
}

String _titleCaseName(String raw, {required String fallback}) {
  final name = raw.startsWith('jarvis-pack-')
      ? raw.substring('jarvis-pack-'.length)
      : raw;
  if (name.trim().isEmpty || name == '(unnamed)') return fallback;
  return name[0].toUpperCase() + name.substring(1);
}

/// Friendly label for a stored secret's name.
String secretLabel(String name) => switch (name) {
  'token' => 'Access key',
  'ics_url' => 'Calendar link',
  'client_secret' => 'App secret',
  _ => name.replaceAll('_', ' '),
};

/// Names Jarvis stores to renew a sign-in. They are not keys the owner manages.
bool isInternalSecret(String name) => name.startsWith('oauth_');

/// "notion-search_pages" or "weather_current" becomes "Search pages" or
/// "Current", dropping a leading app prefix when the app's name is known.
String humanizeAppTool(String tool, {String? appName}) {
  var words = tool
      .replaceAll(RegExp(r'[-_.]+'), ' ')
      .replaceAllMapped(RegExp(r'([a-z])([A-Z])'), (m) => '${m[1]} ${m[2]}')
      .trim()
      .split(RegExp(r'\s+'))
      .where((word) => word.isNotEmpty)
      .toList();
  final prefix = appName?.toLowerCase().split(RegExp(r'\s+')).first;
  if (prefix != null &&
      words.length > 1 &&
      words.first.toLowerCase() == prefix) {
    words = words.sublist(1);
  }
  if (words.isEmpty) return tool;
  final text = words.join(' ').toLowerCase();
  return text[0].toUpperCase() + text.substring(1);
}

/// The one thing that would make an app work again.
enum AppFix { none, signIn, retry, addKeys, approve }

/// What an owner needs to know about a connection right now.
typedef AppHealth = ({String label, String detail, Color color, AppFix fix});

/// Plain-language status for a live connection, and the one fix that helps.
AppHealth appHealth(
  BuildContext context,
  Map<String, dynamic>? connection, {
  bool enabled = true,
}) {
  final colors = JarvisColors.of(context);
  if (!enabled) {
    return (
      label: 'Paused',
      detail: 'Jarvis won’t use it until you switch it back on.',
      color: colors.muted,
      fix: AppFix.none,
    );
  }
  if (connection == null) {
    return (
      label: 'Checking',
      detail: 'Jarvis checks it the next time it needs it.',
      color: colors.muted,
      fix: AppFix.none,
    );
  }
  final state = asJsonString(connection['state']) ?? 'unavailable';
  final toolCount = asJsonInt(connection['toolCount']);
  final issueCode = asJsonString(connection['issue']);
  final issue = switch (issueCode) {
    'too_many_tools' =>
      'It offers too many actions at once. Ask Jarvis to keep only the ones you need.',
    'no_matching_tools' =>
      'The actions you picked are no longer offered. Ask Jarvis to pick new ones.',
    'invalid_configuration' => 'Its setup needs fixing. Ask Jarvis to help.',
    'connection_timed_out' =>
      'It took too long to answer. It may be busy or offline.',
    'runner_required' =>
      'Apps Jarvis builds run in a separate, locked-down helper that this Jarvis server does not have yet.',
    'not_activated' => 'Review its code and approve it to switch it on.',
    'sign_in_expired' => 'The sign-in ran out. Sign in again to keep using it.',
    _ => null,
  };
  if (issueCode == 'not_activated') {
    return (
      label: 'Waiting for your OK',
      detail: issue!,
      color: colors.warning,
      fix: AppFix.approve,
    );
  }
  return switch (state) {
    'connected' => (
      label: 'Working',
      detail:
          'Jarvis can use ${toolCount == 1 ? '1 action' : '$toolCount actions'} here.',
      color: colors.success,
      fix: AppFix.none,
    ),
    'needs_credentials' => (
      label: 'Needs sign-in',
      detail: issue ?? 'Sign in so Jarvis can use it.',
      color: colors.warning,
      fix: AppFix.signIn,
    ),
    'needs_sign_in' => (
      label: 'Sign in again',
      detail: issue ?? 'The sign-in ran out. Sign in again to keep using it.',
      color: colors.warning,
      fix: AppFix.signIn,
    ),
    'paused' => (
      label: 'Paused',
      detail: 'Jarvis won’t use it until you switch it back on.',
      color: colors.muted,
      fix: AppFix.none,
    ),
    'disabled' => (
      label: 'Off',
      detail: issue ?? 'No actions are switched on for Jarvis.',
      color: colors.muted,
      fix: AppFix.none,
    ),
    _ => (
      label: 'Can’t connect',
      detail: issue ?? 'Jarvis couldn’t reach it. It may be offline.',
      color: colors.danger,
      fix: issueCode == 'runner_required' ? AppFix.none : AppFix.retry,
    ),
  };
}

/// The outcome of asking the server to connect to an app once.
typedef AppTestResult = ({
  bool ok,
  String state,
  int toolCount,
  String? errorKind,
  String message,
});

/// Connects to an app once and explains the result, so a saved key or a
/// sign-in is confirmed (or the problem named) straight away.
Future<AppTestResult> testAppConnection(Dio http, String serverKey) async {
  try {
    final response = await http.post<dynamic>(
      '/api/v1/mcp-servers/${Uri.encodeComponent(serverKey)}/test',
    );
    final data = jsonObject(response.data) ?? const {};
    final ok = data['ok'] == true;
    final count = asJsonInt(data['toolCount']);
    final kind = asJsonString(data['errorKind']);
    return (
      ok: ok,
      state: asJsonString(data['state']) ?? 'unavailable',
      toolCount: count,
      errorKind: kind,
      message: ok
          ? 'It works. Jarvis can use ${count == 1 ? '1 action' : '$count actions'}.'
          : appTestProblem(kind, asJsonString(data['message'])),
    );
  } on DioException catch (error) {
    return (
      ok: false,
      state: 'unavailable',
      toolCount: 0,
      errorKind: 'unavailable',
      message:
          asJsonString(jsonObject(error.response?.data)?['message']) ??
          'Jarvis could not check the app right now.',
    );
  }
}

/// What a failed check means for the owner, in one sentence.
String appTestProblem(String? kind, [String? fallback]) => switch (kind) {
  'unauthorized' =>
    'The app turned the key or sign-in down. Check it and try again.',
  'missing_key' => 'The app still needs its key.',
  'timeout' => 'The app took too long to answer. Try again in a moment.',
  'unreachable' => 'Jarvis could not reach the app. It may be offline.',
  'crashed' => 'The app stopped while starting.',
  'runner_required' =>
    'Apps Jarvis builds need the separate app helper on your Jarvis server.',
  'not_activated' => 'Approve the app’s code first.',
  'paused' => 'It works, but it is paused.',
  _ => fallback ?? 'The app is not available right now.',
};

/// How a sign-in attempt ended: `signed_in`, `needs_token` (the app has no
/// sign-in page, so paste a key), `pending` (the page opened; the result is
/// not known yet) or `failed` with a message.
typedef AppSignInOutcome = ({String status, String? message});

/// Opens an app's sign-in page and waits for Jarvis to receive the result.
/// [alive] stops the wait once the calling screen goes away.
Future<AppSignInOutcome> signInToApp(
  Dio http, {
  String? server,
  String? endpoint,
  required bool Function() alive,
}) async {
  final target = (server ?? '').trim();
  final url = (endpoint ?? '').trim();
  if (target.isEmpty && url.isEmpty) {
    return (
      status: 'failed',
      message:
          'Jarvis doesn’t know where to sign in for this app yet. Ask Jarvis in chat to set it up.',
    );
  }
  try {
    final response = await http.post<dynamic>(
      '/api/v1/integrations/oauth/sessions',
      data: {
        if (target.isNotEmpty) 'server': target,
        if (target.isEmpty && url.isNotEmpty) 'endpoint': url,
      },
    );
    final body = jsonObject(response.data) ?? const {};
    if (asJsonString(body['status']) == 'needs_token') {
      return (status: 'needs_token', message: null);
    }
    final authorize = parseHttpUrl(asJsonString(body['authorizationUrl']));
    if (authorize == null) {
      return (
        status: 'failed',
        message: 'This app has no sign-in page. Use an access key instead.',
      );
    }
    if (!await launchHttpUrl(authorize)) {
      return (status: 'failed', message: 'Could not open the sign-in page.');
    }
    final id = asJsonString(body['id']);
    if (id == null ||
        id.isEmpty ||
        id == '00000000-0000-0000-0000-000000000000') {
      return (status: 'pending', message: null);
    }
    for (var i = 0; i < 45; i++) {
      await Future<void>.delayed(const Duration(seconds: 2));
      if (!alive()) return (status: 'pending', message: null);
      try {
        final poll = await http.get<dynamic>(
          '/api/v1/integrations/oauth/sessions/$id',
        );
        final session = jsonObject(poll.data);
        final state = asJsonString(session?['status']);
        if (state == 'completed') return (status: 'signed_in', message: null);
        if (state == 'failed') {
          return (
            status: 'failed',
            message:
                asJsonString(session?['error']) ??
                'Sign-in didn’t finish. You can use an access key instead.',
          );
        }
      } catch (_) {
        break;
      }
    }
    return (status: 'pending', message: null);
  } on DioException catch (error) {
    return (
      status: 'failed',
      message:
          firstProblemMessage(error.response?.data) ??
          'Could not start sign-in.',
    );
  } catch (_) {
    return (status: 'failed', message: 'Could not start sign-in.');
  }
}
