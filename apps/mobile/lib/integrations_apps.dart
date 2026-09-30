part of 'integrations_screen.dart';

/// Plain-language facts about an app Jarvis can connect to.
typedef _AppInfo = ({String name, IconData icon, String? what});

/// Turns a server or credential name into a name, icon, and one-line
/// description a non-technical owner recognises.
_AppInfo _appInfo(String raw) {
  final key = raw.toLowerCase().replaceFirst('jarvis-pack-', '');
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
String _secretLabel(String name) => switch (name) {
  'token' => 'Access key',
  'ics_url' => 'Calendar link',
  'client_secret' => 'App secret',
  _ => name.replaceAll('_', ' '),
};

/// What an owner needs to know about a connection right now.
typedef _Health = ({String label, String detail, Color color, _Fix fix});

enum _Fix { none, signIn, retry }

extension _Plural on int {
  String actions() => this == 1 ? '1 action' : '$this actions';
}
