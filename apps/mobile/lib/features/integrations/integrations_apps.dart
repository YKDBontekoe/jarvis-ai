part of 'integrations_screen.dart';

// The shared versions live in app_presentation.dart so Home tiles, chat and
// the app detail screen describe apps the same way.
typedef _AppInfo = AppInfo;

_AppInfo _appInfo(String raw, {bool selfBuilt = false}) =>
    appInfo(raw, selfBuilt: selfBuilt);

String _secretLabel(String name) => secretLabel(name);

typedef _Health = AppHealth;

typedef _Fix = AppFix;
