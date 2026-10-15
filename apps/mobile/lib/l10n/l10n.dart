import 'package:flutter/widgets.dart';

import 'app_localizations.dart';

export 'app_localizations.dart';

final _english = lookupAppLocalizations(const Locale('en'));

/// English strings, for code that runs without a [BuildContext] (formatters
/// called from tests and background work).
AppLocalizations get englishStrings => _english;

extension JarvisL10n on BuildContext {
  /// The app's strings in the current language. Falls back to English where no
  /// localizations are mounted, such as a bare `MaterialApp` in widget tests.
  AppLocalizations get l10n => AppLocalizations.of(this) ?? _english;
}
