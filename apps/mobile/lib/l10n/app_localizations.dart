import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_nl.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations? of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations);
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('nl'),
  ];

  /// No description provided for @tabHome.
  ///
  /// In en, this message translates to:
  /// **'Home'**
  String get tabHome;

  /// No description provided for @tabChats.
  ///
  /// In en, this message translates to:
  /// **'Chats'**
  String get tabChats;

  /// No description provided for @tabEverything.
  ///
  /// In en, this message translates to:
  /// **'Everything'**
  String get tabEverything;

  /// No description provided for @tabYou.
  ///
  /// In en, this message translates to:
  /// **'You'**
  String get tabYou;

  /// Screen reader label of a tab with unread items.
  ///
  /// In en, this message translates to:
  /// **'{label}, unread'**
  String tabUnread(String label);

  /// No description provided for @greetingMorning.
  ///
  /// In en, this message translates to:
  /// **'Good morning'**
  String get greetingMorning;

  /// No description provided for @greetingAfternoon.
  ///
  /// In en, this message translates to:
  /// **'Good afternoon'**
  String get greetingAfternoon;

  /// No description provided for @greetingEvening.
  ///
  /// In en, this message translates to:
  /// **'Good evening'**
  String get greetingEvening;

  /// No description provided for @offline.
  ///
  /// In en, this message translates to:
  /// **'Offline'**
  String get offline;

  /// No description provided for @offlineShowingSaved.
  ///
  /// In en, this message translates to:
  /// **'Offline · showing data from {age}'**
  String offlineShowingSaved(String age);

  /// No description provided for @offlineRetry.
  ///
  /// In en, this message translates to:
  /// **'Retry'**
  String get offlineRetry;

  /// No description provided for @offlineChecking.
  ///
  /// In en, this message translates to:
  /// **'Checking…'**
  String get offlineChecking;

  /// No description provided for @ageJustNow.
  ///
  /// In en, this message translates to:
  /// **'just now'**
  String get ageJustNow;

  /// No description provided for @ageMinutes.
  ///
  /// In en, this message translates to:
  /// **'{count} min ago'**
  String ageMinutes(int count);

  /// No description provided for @ageHours.
  ///
  /// In en, this message translates to:
  /// **'{count} h ago'**
  String ageHours(int count);

  /// No description provided for @ageYesterday.
  ///
  /// In en, this message translates to:
  /// **'yesterday'**
  String get ageYesterday;

  /// No description provided for @ageDays.
  ///
  /// In en, this message translates to:
  /// **'{count} days ago'**
  String ageDays(int count);

  /// No description provided for @appearanceTitle.
  ///
  /// In en, this message translates to:
  /// **'Appearance'**
  String get appearanceTitle;

  /// No description provided for @languageSection.
  ///
  /// In en, this message translates to:
  /// **'Language'**
  String get languageSection;

  /// No description provided for @languageSystem.
  ///
  /// In en, this message translates to:
  /// **'Use device language'**
  String get languageSystem;

  /// No description provided for @languageSystemSubtitle.
  ///
  /// In en, this message translates to:
  /// **'English or Dutch, whichever this device uses'**
  String get languageSystemSubtitle;

  /// No description provided for @languageEnglish.
  ///
  /// In en, this message translates to:
  /// **'English'**
  String get languageEnglish;

  /// No description provided for @languageEnglishSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Jarvis\'s screens in English'**
  String get languageEnglishSubtitle;

  /// No description provided for @languageDutch.
  ///
  /// In en, this message translates to:
  /// **'Nederlands'**
  String get languageDutch;

  /// No description provided for @languageDutchSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Jarvis\'s screens in Dutch'**
  String get languageDutchSubtitle;

  /// No description provided for @languageNote.
  ///
  /// In en, this message translates to:
  /// **'Jarvis answers in the language you write in, whatever you pick here.'**
  String get languageNote;

  /// No description provided for @turnQualityTitle.
  ///
  /// In en, this message translates to:
  /// **'Turn quality'**
  String get turnQualityTitle;

  /// No description provided for @turnQualityEmpty.
  ///
  /// In en, this message translates to:
  /// **'Not enough turns yet. Reply times, failures and approvals show up here after a few chats.'**
  String get turnQualityEmpty;

  /// No description provided for @turnQualityTypicalReply.
  ///
  /// In en, this message translates to:
  /// **'Typical reply'**
  String get turnQualityTypicalReply;

  /// No description provided for @turnQualitySlowestReply.
  ///
  /// In en, this message translates to:
  /// **'Slowest 1 in 10'**
  String get turnQualitySlowestReply;

  /// No description provided for @turnQualityTypicalTurn.
  ///
  /// In en, this message translates to:
  /// **'Typical turn'**
  String get turnQualityTypicalTurn;

  /// No description provided for @turnQualityFromCache.
  ///
  /// In en, this message translates to:
  /// **'From cache'**
  String get turnQualityFromCache;

  /// No description provided for @turnQualitySlowestTools.
  ///
  /// In en, this message translates to:
  /// **'Slowest tools'**
  String get turnQualitySlowestTools;

  /// No description provided for @turnQualityToolAverage.
  ///
  /// In en, this message translates to:
  /// **'{tool}: {duration} on average'**
  String turnQualityToolAverage(String tool, String duration);

  /// No description provided for @turnQualityModelFailures.
  ///
  /// In en, this message translates to:
  /// **'{percent}% of {calls} model calls failed'**
  String turnQualityModelFailures(String percent, int calls);

  /// No description provided for @turnQualityTurns.
  ///
  /// In en, this message translates to:
  /// **'{failed, plural, =1{1 turn failed} other{{failed} turns failed}}, {cancelled} stopped (of {turns} traced)'**
  String turnQualityTurns(int failed, int cancelled, int turns);

  /// No description provided for @turnQualityTools.
  ///
  /// In en, this message translates to:
  /// **'{calls, plural, =1{1 tool call} other{{calls} tool calls}}, {percent}% failed'**
  String turnQualityTools(int calls, int percent);

  /// No description provided for @turnQualityApprovals.
  ///
  /// In en, this message translates to:
  /// **'{asked, plural, =1{1 approval} other{{asked} approvals}} asked, {denied} declined, {auto} handled automatically'**
  String turnQualityApprovals(int asked, int denied, int auto);

  /// No description provided for @turnQualityFlagSlowReplies.
  ///
  /// In en, this message translates to:
  /// **'Replies are slow: one in ten takes over 30 s. A faster chat model in Settings → Models helps most.'**
  String get turnQualityFlagSlowReplies;

  /// No description provided for @turnQualityFlagFailingTurns.
  ///
  /// In en, this message translates to:
  /// **'More than one in ten turns failed. The Codex sign-in or the server log usually says why.'**
  String get turnQualityFlagFailingTurns;

  /// No description provided for @turnQualityFlagApprovalFriction.
  ///
  /// In en, this message translates to:
  /// **'You approve a lot by hand. Always allow a category on its next approval card, or raise the autonomy level in Learning.'**
  String get turnQualityFlagApprovalFriction;

  /// No description provided for @turnQualityFlagLowCache.
  ///
  /// In en, this message translates to:
  /// **'Few prompts are reused from cache, so each reply costs more time.'**
  String get turnQualityFlagLowCache;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'nl'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'nl':
      return AppLocalizationsNl();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
