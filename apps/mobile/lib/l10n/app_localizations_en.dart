// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get tabHome => 'Home';

  @override
  String get tabChats => 'Chats';

  @override
  String get tabEverything => 'Everything';

  @override
  String get tabYou => 'You';

  @override
  String tabUnread(String label) {
    return '$label, unread';
  }

  @override
  String get greetingMorning => 'Good morning';

  @override
  String get greetingAfternoon => 'Good afternoon';

  @override
  String get greetingEvening => 'Good evening';

  @override
  String get offline => 'Offline';

  @override
  String offlineShowingSaved(String age) {
    return 'Offline · showing data from $age';
  }

  @override
  String get offlineRetry => 'Retry';

  @override
  String get offlineChecking => 'Checking…';

  @override
  String get ageJustNow => 'just now';

  @override
  String ageMinutes(int count) {
    return '$count min ago';
  }

  @override
  String ageHours(int count) {
    return '$count h ago';
  }

  @override
  String get ageYesterday => 'yesterday';

  @override
  String ageDays(int count) {
    return '$count days ago';
  }

  @override
  String get appearanceTitle => 'Appearance';

  @override
  String get languageSection => 'Language';

  @override
  String get languageSystem => 'Use device language';

  @override
  String get languageSystemSubtitle =>
      'English or Dutch, whichever this device uses';

  @override
  String get languageEnglish => 'English';

  @override
  String get languageEnglishSubtitle => 'Jarvis\'s screens in English';

  @override
  String get languageDutch => 'Nederlands';

  @override
  String get languageDutchSubtitle => 'Jarvis\'s screens in Dutch';

  @override
  String get languageNote =>
      'Jarvis answers in the language you write in, whatever you pick here.';

  @override
  String get turnQualityTitle => 'Turn quality';

  @override
  String get turnQualityEmpty =>
      'Not enough turns yet. Reply times, failures and approvals show up here after a few chats.';

  @override
  String get turnQualityTypicalReply => 'Typical reply';

  @override
  String get turnQualitySlowestReply => 'Slowest 1 in 10';

  @override
  String get turnQualityTypicalTurn => 'Typical turn';

  @override
  String get turnQualityFromCache => 'From cache';

  @override
  String get turnQualitySlowestTools => 'Slowest tools';

  @override
  String turnQualityToolAverage(String tool, String duration) {
    return '$tool: $duration on average';
  }

  @override
  String turnQualityModelFailures(String percent, int calls) {
    return '$percent% of $calls model calls failed';
  }

  @override
  String turnQualityTurns(int failed, int cancelled, int turns) {
    String _temp0 = intl.Intl.pluralLogic(
      failed,
      locale: localeName,
      other: '$failed turns failed',
      one: '1 turn failed',
    );
    return '$_temp0, $cancelled stopped (of $turns traced)';
  }

  @override
  String turnQualityTools(int calls, int percent) {
    String _temp0 = intl.Intl.pluralLogic(
      calls,
      locale: localeName,
      other: '$calls tool calls',
      one: '1 tool call',
    );
    return '$_temp0, $percent% failed';
  }

  @override
  String turnQualityApprovals(int asked, int denied, int auto) {
    String _temp0 = intl.Intl.pluralLogic(
      asked,
      locale: localeName,
      other: '$asked approvals',
      one: '1 approval',
    );
    return '$_temp0 asked, $denied declined, $auto handled automatically';
  }

  @override
  String get turnQualityFlagSlowReplies =>
      'Replies are slow: one in ten takes over 30 s. A faster chat model in Settings → Models helps most.';

  @override
  String get turnQualityFlagFailingTurns =>
      'More than one in ten turns failed. The Codex sign-in or the server log usually says why.';

  @override
  String get turnQualityFlagApprovalFriction =>
      'You approve a lot by hand. Always allow a category on its next approval card, or raise the autonomy level in Learning.';

  @override
  String get turnQualityFlagLowCache =>
      'Few prompts are reused from cache, so each reply costs more time.';
}
