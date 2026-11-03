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

  @override
  String get capturesTitle => 'Vastgelegd';

  @override
  String get capturesEmptyTitle => 'Nog niets gedeeld';

  @override
  String get capturesEmptyMessage =>
      'Gebruik Deel in een andere app en kies Jarvis. Wat je deelt wacht hier tot jij beslist wat ermee gebeurt.';

  @override
  String get capturesDiscardTitle => 'Dit weggooien?';

  @override
  String get capturesDiscardMessage =>
      'Het wordt van deze telefoon verwijderd. Er wordt niets verstuurd.';

  @override
  String get capturesKeep => 'Bewaren';

  @override
  String get capturesDiscard => 'Weggooien';

  @override
  String get capturesLinkNote =>
      'Voeg een notitie bij deze link toe (optioneel)';

  @override
  String get capturesEditFirst => 'Pas aan voordat je beslist';

  @override
  String get capturesSaveToFiles => 'Bewaar in Bestanden';

  @override
  String get capturesSaveToLibrary => 'Bewaar in Bibliotheek';

  @override
  String get capturesAsk => 'Vraag het Jarvis';

  @override
  String get capturesRemind => 'Herinnering maken';

  @override
  String get hideInSwitcherTitle => 'Verberg in appkiezer';

  @override
  String get hideInSwitcherSubtitle =>
      'Maak Jarvis onscherp als je omhoog veegt of van app wisselt';

  @override
  String get proactiveTitle => 'Lopend werk';

  @override
  String get proactiveNew => 'Nieuwe verantwoordelijkheid';

  @override
  String get proactiveDescription =>
      'De dingen die Jarvis voor je bijhoudt. Elke wijziging heeft een reden en een geschiedenis.';

  @override
  String get proactiveEmpty => 'Nog niets bijgehouden';

  @override
  String get proactiveConfirm => 'Deze verantwoordelijkheid wijzigen?';

  @override
  String get proactiveConfirmBody =>
      'Je keuze wordt bewaard. Afronden of stoppen beëindigt de controles.';

  @override
  String get proactiveContinue => 'Doorgaan';

  @override
  String get proactiveEdit => 'Verantwoordelijkheid bewerken';

  @override
  String get proactiveOpen => 'Openen';

  @override
  String get proactiveSuggested => 'Voorstel · controles uit';

  @override
  String get proactivePaused => 'Gepauzeerd';

  @override
  String get proactiveResolved => 'Afgerond';

  @override
  String get proactiveStopped => 'Gestopt';

  @override
  String get proactiveNeedsInput => 'Jij bent nodig';

  @override
  String get proactiveChecking => 'Controles ingeschakeld';

  @override
  String get proactiveTracked => 'Bijgehouden · controles uit';

  @override
  String get proactiveStopWhen => 'Stoppen wanneer';

  @override
  String get proactiveAccept => 'Accepteren';

  @override
  String get proactivePause => 'Pauzeren';

  @override
  String get proactiveEnable => 'Controles inschakelen';

  @override
  String get proactiveResolve => 'Afronden';

  @override
  String get proactiveStop => 'Stoppen';

  @override
  String get proactiveEvidence => 'Onderbouwing';

  @override
  String get proactiveSteps => 'Volgende stappen';

  @override
  String get proactiveDone => 'Klaar';

  @override
  String get proactiveSources => 'Bronnen';

  @override
  String get proactiveHistory => 'Wat Jarvis deed';

  @override
  String get proactiveUndo => 'Ongedaan maken';

  @override
  String get proactiveFeedback => 'Was dit nuttig?';

  @override
  String get proactiveUseful => 'Nuttig';

  @override
  String get proactiveNotUseful => 'Niet nuttig';

  @override
  String get proactiveTooEarly => 'Te vroeg';

  @override
  String get proactiveTooMuch => 'Te veel moeite';

  @override
  String get proactiveSave => 'Opslaan';

  @override
  String get proactiveName => 'Naam';

  @override
  String get proactiveRequired => 'Vul dit in';

  @override
  String get proactiveObjective => 'Wat moet Jarvis bijhouden?';

  @override
  String get proactiveKind => 'Soort werk';

  @override
  String get proactiveKindIntention => 'Open voornemen';

  @override
  String get proactiveKindProject => 'Projectvoortgang';

  @override
  String get proactiveKindFollow => 'Wachten op antwoord';

  @override
  String get proactiveKindContext => 'Context actueel houden';

  @override
  String get proactiveKindReturn => 'Retour';

  @override
  String get proactiveKindRefund => 'Terugbetaling';

  @override
  String get proactiveKindRepair => 'Reparatie';

  @override
  String get proactiveEnableExplanation =>
      'Opslaan houdt dit werk bij. Schakel controles apart in wanneer je zover bent. Bestaande toestemmingen blijven gelden.';

  @override
  String get proactiveLastCheck => 'Laatst gecontroleerd';

  @override
  String get proactiveNextCheck => 'Volgende controle';

  @override
  String get proactivePreparation => 'Voor je voorbereid';

  @override
  String get proactiveCapacity => 'Beschikbare tijd in minuten';

  @override
  String get proactiveValidMinutes => 'Vul een geldig aantal minuten in';

  @override
  String get proactiveDeadline => 'Deadline kiezen';

  @override
  String get proactiveClearDeadline => 'Deadline wissen';

  @override
  String get proactiveRemove => 'Verwijderen';

  @override
  String get proactiveAddStep => 'Stap toevoegen';

  @override
  String get proactiveEffort => 'Geschatte minuten, indien bekend';

  @override
  String get proactiveCompletion => 'Wanneer is deze stap klaar?';

  @override
  String get proactiveSource => 'Gekoppelde bron, optioneel';

  @override
  String get proactiveDependsOn => 'Wacht op deze stappen';

  @override
  String get proactiveCancel => 'Annuleren';

  @override
  String get proactiveKindDeadline => 'Deadlinebewaking';

  @override
  String get proactiveKindDay => 'Dagplanning aanpassen';

  @override
  String get proactiveKindMeeting => 'Vergaderingen opvolgen';

  @override
  String get proactiveKindCapacity => 'Gezamenlijke doelcapaciteit';

  @override
  String get proactiveKindTrip => 'Reisbewaking';

  @override
  String get proactiveKindConsistency => 'Samenhangende wijzigingen';

  @override
  String get proactiveKindDecision => 'Aannames bij beslissingen';

  @override
  String get proactiveKindResearch => 'Onderzoek bijwerken';

  @override
  String get proactiveKindMemory => 'Herinneringen vergelijken';

  @override
  String get proactiveKindTrial => 'Routine observeren';

  @override
  String get proactivePin => 'Dit blok vastzetten';

  @override
  String get proactiveUnpin => 'Dit blok laten verplaatsen';

  @override
  String get proactiveDraftStale =>
      'Onderliggende informatie is gewijzigd. Controleer de betrokken onderdelen of bouw ze opnieuw op uit bronnen. Je eigen tekst is behouden.';

  @override
  String get proactiveReviewMemories => 'Deze herinneringen beoordelen';

  @override
  String get proactiveKeepFirst => 'Eerste waarde behouden';

  @override
  String get proactiveKeepSecond => 'Tweede waarde behouden';

  @override
  String get proactiveKeepBoth => 'Beide met context bewaren';

  @override
  String get proactiveFirstContext => 'Wanneer geldt de eerste?';

  @override
  String get proactiveSecondContext => 'Wanneer geldt de tweede?';

  @override
  String get proactiveMemoryHistory =>
      'Jij maakt deze keuze. Eerdere versies blijven in de geschiedenis. Vastgezette herinneringen bewerk je in Geheugen.';

  @override
  String get proactiveAssumptions => 'Aannames om opnieuw te bekijken';

  @override
  String get proactiveAddAssumption => 'Aanname toevoegen';

  @override
  String get proactiveAssumptionHelp =>
      'Kies eerst een bron. Een getalsvoorwaarde vereist een bewaking met een actuele meting. Je beslist zelf over de uitkomst.';

  @override
  String get proactiveAssumptionStatement => 'Wat moet Jarvis signaleren?';

  @override
  String get proactiveReviewWhen => 'Opnieuw bekijken wanneer';

  @override
  String get proactiveSourceChanges => 'De bron verandert';

  @override
  String get proactiveAbove => 'De gemeten waarde stijgt boven';

  @override
  String get proactiveBelow => 'De gemeten waarde daalt onder';

  @override
  String get proactiveThreshold => 'Grenswaarde';

  @override
  String get proactivePreviousPreparation => 'Eerdere voorbereiding';

  @override
  String get proactivePreparationChanges =>
      'Wijzigingen sinds de vorige voorbereiding';

  @override
  String get proactiveCalendarCapacity =>
      'Neem de kalenderbeschikbaarheid van deze week mee';

  @override
  String get proactiveTripChanges => 'Gevolgde reiswijzigingen';

  @override
  String get proactiveImportantClaims => 'Belangrijke beweringen';

  @override
  String get proactiveAddClaim => 'Volg een belangrijke bewering';

  @override
  String get proactiveClaimStatement => 'Te controleren bewering';

  @override
  String get proactiveClaimHelp =>
      'Selecteer eerst een opgeslagen openbare webbron. Jarvis vergelijkt deze expliciete bewering met de actuele informatie uit die bron, citeert een gecontroleerd fragment en houdt dubbelzinnige of onbeschikbare informatie onbekend. Je oorspronkelijke bewering en uitkomst worden nooit automatisch gewijzigd.';

  @override
  String get proactiveAddTripMonitor => 'Volg een reiswijziging';

  @override
  String get proactiveShiftByDelay =>
      'Verschuif dit reisonderdeel met de waargenomen vertragingsminuten';

  @override
  String get proactiveTripItem => 'Geverifieerd reisonderdeel';

  @override
  String get proactiveTripDelayHelp =>
      'Selecteer eerst een reis en watch bij Bronnen. Gebruik een watch waarvan de numerieke waarde de vertragingsminuten voor dit onderdeel is. De oorspronkelijke tijd moet geverifieerd zijn. Recente waarnemingen bereiden gewijzigde tijden en alternatieven voor; boekingen blijven ongewijzigd. Stel de tijdzone van deze verantwoordelijkheid in op de lokale tijdzone van de reis.';

  @override
  String get proactiveWeeklyBudget =>
      'Gedeeld weekbudget in minuten (optioneel met kalender)';

  @override
  String get proactiveCalendarCapacityHelp =>
      'Gebruik de werktijden van Vandaag op maandag–vrijdag in de tijdzone van deze verantwoordelijkheid. Afspraken, ook dagvullende, tellen als bezet. Afgerond doelwerk gebruikt hetzelfde weekbudget. Bij een kalenderfout blijft de capaciteit onbekend. Schakel deze accountbrede verantwoordelijkheid na opslaan apart in.';

  @override
  String get proactiveTrigger => 'Aanleiding';

  @override
  String get proactiveBefore => 'Voorheen';

  @override
  String get proactiveAfter => 'Daarna';

  @override
  String get proactiveCheckSettings => 'Controles en onderbrekingen';

  @override
  String get proactiveCheckSettingsHelp =>
      'Opslaan houdt dit werk bij. Schakel doorlopende controles apart in. Optionele items delen één daglimiet; goedkeuringen en fouten blijven zichtbaar. Gelijke begin- en einduren schakelen stille uren uit.';

  @override
  String get proactiveCadence => 'Minuten tussen controles';

  @override
  String get proactiveDailyChecks => 'Maximumcontroles per dag';

  @override
  String get proactiveOptionalItems => 'Optionele items per dag';

  @override
  String get proactiveQuietStart => 'Stille uren beginnen om';

  @override
  String get proactiveQuietEnd => 'Stille uren eindigen om';

  @override
  String get proactiveTimeZone => 'Tijdzone (bijvoorbeeld Europe/Amsterdam)';

  @override
  String get proactiveExpiry => 'Stop controles op';

  @override
  String get proactiveMainSource => 'Hoofdrecord';

  @override
  String get proactiveReviewed => 'Beoordeeld';

  @override
  String get proactiveCaseNotes => 'Referenties, concepten en dossiernotities';
}
