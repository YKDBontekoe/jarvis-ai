// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Dutch Flemish (`nl`).
class AppLocalizationsNl extends AppLocalizations {
  AppLocalizationsNl([String locale = 'nl']) : super(locale);

  @override
  String get tabHome => 'Home';

  @override
  String get tabChats => 'Chats';

  @override
  String get tabEverything => 'Alles';

  @override
  String get tabYou => 'Jij';

  @override
  String tabUnread(String label) {
    return '$label, ongelezen';
  }

  @override
  String get greetingMorning => 'Goedemorgen';

  @override
  String get greetingAfternoon => 'Goedemiddag';

  @override
  String get greetingEvening => 'Goedenavond';

  @override
  String get offline => 'Offline';

  @override
  String offlineShowingSaved(String age) {
    return 'Offline · gegevens van $age';
  }

  @override
  String get offlineRetry => 'Opnieuw';

  @override
  String get offlineChecking => 'Controleren…';

  @override
  String get ageJustNow => 'zojuist';

  @override
  String ageMinutes(int count) {
    return '$count min geleden';
  }

  @override
  String ageHours(int count) {
    return '$count uur geleden';
  }

  @override
  String get ageYesterday => 'gisteren';

  @override
  String ageDays(int count) {
    return '$count dagen geleden';
  }

  @override
  String get appearanceTitle => 'Weergave';

  @override
  String get languageSection => 'Taal';

  @override
  String get languageSystem => 'Taal van apparaat';

  @override
  String get languageSystemSubtitle =>
      'Engels of Nederlands, wat dit apparaat gebruikt';

  @override
  String get languageEnglish => 'English';

  @override
  String get languageEnglishSubtitle => 'De schermen van Jarvis in het Engels';

  @override
  String get languageDutch => 'Nederlands';

  @override
  String get languageDutchSubtitle =>
      'De schermen van Jarvis in het Nederlands';

  @override
  String get languageNote =>
      'Jarvis antwoordt in de taal waarin je schrijft, wat je hier ook kiest.';

  @override
  String get turnQualityTitle => 'Kwaliteit van beurten';

  @override
  String get turnQualityEmpty =>
      'Nog te weinig beurten. Antwoordtijden, fouten en goedkeuringen verschijnen hier na een paar gesprekken.';

  @override
  String get turnQualityTypicalReply => 'Gewoon antwoord';

  @override
  String get turnQualitySlowestReply => 'Traagste 1 op 10';

  @override
  String get turnQualityTypicalTurn => 'Gewone beurt';

  @override
  String get turnQualityFromCache => 'Uit cache';

  @override
  String get turnQualitySlowestTools => 'Traagste tools';

  @override
  String turnQualityToolAverage(String tool, String duration) {
    return '$tool: gemiddeld $duration';
  }

  @override
  String turnQualityModelFailures(String percent, int calls) {
    return '$percent% van $calls modelaanroepen mislukte';
  }

  @override
  String turnQualityTurns(int failed, int cancelled, int turns) {
    String _temp0 = intl.Intl.pluralLogic(
      failed,
      locale: localeName,
      other: '$failed beurten mislukt',
      one: '1 beurt mislukt',
    );
    return '$_temp0, $cancelled gestopt (van $turns gevolgd)';
  }

  @override
  String turnQualityTools(int calls, int percent) {
    String _temp0 = intl.Intl.pluralLogic(
      calls,
      locale: localeName,
      other: '$calls toolaanroepen',
      one: '1 toolaanroep',
    );
    return '$_temp0, $percent% mislukt';
  }

  @override
  String turnQualityApprovals(int asked, int denied, int auto) {
    String _temp0 = intl.Intl.pluralLogic(
      asked,
      locale: localeName,
      other: '$asked goedkeuringen',
      one: '1 goedkeuring',
    );
    return '$_temp0 gevraagd, $denied geweigerd, $auto automatisch afgehandeld';
  }

  @override
  String get turnQualityFlagSlowReplies =>
      'Antwoorden zijn traag: één op de tien duurt langer dan 30 s. Een sneller chatmodel in Instellingen → Modellen helpt het meest.';

  @override
  String get turnQualityFlagFailingTurns =>
      'Meer dan één op de tien beurten mislukte. De Codex-aanmelding of het serverlog zegt meestal waarom.';

  @override
  String get turnQualityFlagApprovalFriction =>
      'Je keurt veel met de hand goed. Sta een categorie altijd toe op de volgende goedkeuringskaart, of verhoog het autonomieniveau in Leren.';

  @override
  String get turnQualityFlagLowCache =>
      'Weinig prompts komen uit de cache, dus elk antwoord kost meer tijd.';
}
