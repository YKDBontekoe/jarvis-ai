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
  String tabNeedsYou(String label) {
    return '$label, something needs you';
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

  @override
  String get capturesTitle => 'Captures';

  @override
  String get capturesEmptyTitle => 'Nothing shared yet';

  @override
  String get capturesEmptyMessage =>
      'Use Share in another app and choose Jarvis. What you share waits here until you decide what to do with it.';

  @override
  String get capturesDiscardTitle => 'Discard this capture?';

  @override
  String get capturesDiscardMessage =>
      'It will be removed from this phone. Nothing is sent anywhere.';

  @override
  String get capturesKeep => 'Keep';

  @override
  String get capturesDiscard => 'Discard';

  @override
  String get capturesLinkNote => 'Add a note about this link (optional)';

  @override
  String get capturesEditFirst => 'Edit before you decide';

  @override
  String get capturesSaveToFiles => 'Save to Files';

  @override
  String get capturesSaveToLibrary => 'Save to Library';

  @override
  String get capturesAsk => 'Ask Jarvis';

  @override
  String get capturesRemind => 'Create reminder';

  @override
  String get hideInSwitcherTitle => 'Hide in app switcher';

  @override
  String get hideInSwitcherSubtitle =>
      'Blur Jarvis when you swipe up or switch apps';

  @override
  String get proactiveTitle => 'Ongoing work';

  @override
  String get proactiveNew => 'New responsibility';

  @override
  String get proactiveDescription =>
      'The things you asked Jarvis to keep moving. Every change has a reason and a history.';

  @override
  String get proactiveEmpty => 'Nothing tracked yet';

  @override
  String get proactiveConfirm => 'Update this responsibility?';

  @override
  String get proactiveConfirmBody =>
      'Your choice is kept in the history. Resolving or stopping ends ongoing checks.';

  @override
  String get proactiveContinue => 'Continue';

  @override
  String get proactiveEdit => 'Edit responsibility';

  @override
  String get proactiveOpen => 'Open';

  @override
  String get proactiveSuggested => 'Suggested · checks off';

  @override
  String get proactivePaused => 'Paused';

  @override
  String get proactiveResolved => 'Resolved';

  @override
  String get proactiveStopped => 'Stopped';

  @override
  String get proactiveNeedsInput => 'Needs you';

  @override
  String get proactiveChecking => 'Ongoing checks enabled';

  @override
  String get proactiveTracked => 'Tracked · checks off';

  @override
  String get proactiveStopWhen => 'Stop when';

  @override
  String get proactiveAccept => 'Accept';

  @override
  String get proactivePause => 'Pause';

  @override
  String get proactiveEnable => 'Enable checks';

  @override
  String get proactiveResolve => 'Mark resolved';

  @override
  String get proactiveStop => 'Stop';

  @override
  String get proactiveEvidence => 'Evidence';

  @override
  String get proactiveSteps => 'Next steps';

  @override
  String get proactiveDone => 'Done';

  @override
  String get proactiveSources => 'Sources';

  @override
  String get proactiveHistory => 'What Jarvis did';

  @override
  String get proactiveUndo => 'Undo';

  @override
  String get proactiveFeedback => 'Did this help?';

  @override
  String get proactiveUseful => 'Useful';

  @override
  String get proactiveNotUseful => 'Not useful';

  @override
  String get proactiveTooEarly => 'Too early';

  @override
  String get proactiveTooMuch => 'Too much effort';

  @override
  String get proactiveSave => 'Save';

  @override
  String get proactiveName => 'Name';

  @override
  String get proactiveRequired => 'Fill this in';

  @override
  String get proactiveObjective => 'What should Jarvis keep moving?';

  @override
  String get proactiveKind => 'Kind of work';

  @override
  String get proactiveKindIntention => 'Unfinished intention';

  @override
  String get proactiveKindProject => 'Project follow-through';

  @override
  String get proactiveKindFollow => 'Waiting for a reply';

  @override
  String get proactiveKindContext => 'Keep context current';

  @override
  String get proactiveKindReturn => 'Return';

  @override
  String get proactiveKindRefund => 'Refund';

  @override
  String get proactiveKindRepair => 'Repair';

  @override
  String get proactiveEnableExplanation =>
      'Saving tracks this work. Enable ongoing checks separately when you are ready. Existing permissions still apply.';

  @override
  String get proactiveLastCheck => 'Last checked';

  @override
  String get proactiveNextCheck => 'Next check';

  @override
  String get proactivePreparation => 'Prepared for you';

  @override
  String get proactiveCapacity => 'Time available, in minutes';

  @override
  String get proactiveValidMinutes => 'Enter a valid number of minutes';

  @override
  String get proactiveDeadline => 'Choose a deadline';

  @override
  String get proactiveClearDeadline => 'Clear deadline';

  @override
  String get proactiveRemove => 'Remove';

  @override
  String get proactiveAddStep => 'Add a step';

  @override
  String get proactiveEffort => 'Estimated minutes, if known';

  @override
  String get proactiveCompletion => 'How will you know it is done?';

  @override
  String get proactiveSource => 'Linked source, optional';

  @override
  String get proactiveDependsOn => 'Waits for these steps';

  @override
  String get proactiveCancel => 'Cancel';

  @override
  String get proactiveKindDeadline => 'Deadline risk';

  @override
  String get proactiveKindDay => 'Adaptive day plan';

  @override
  String get proactiveKindMeeting => 'Meeting continuity';

  @override
  String get proactiveKindCapacity => 'Shared goal capacity';

  @override
  String get proactiveKindTrip => 'Trip monitoring';

  @override
  String get proactiveKindConsistency => 'Related changes';

  @override
  String get proactiveKindDecision => 'Decision assumptions';

  @override
  String get proactiveKindResearch => 'Research updates';

  @override
  String get proactiveKindMemory => 'Memory comparison';

  @override
  String get proactiveKindTrial => 'Routine observation';

  @override
  String get proactivePin => 'Keep this block in place';

  @override
  String get proactiveUnpin => 'Allow this block to move';

  @override
  String get proactiveDraftStale =>
      'Supporting information changed. Review the affected sections or rebuild them from sources. Your wording has been kept.';

  @override
  String get proactiveReviewMemories => 'Review these memories';

  @override
  String get proactiveKeepFirst => 'Keep first value';

  @override
  String get proactiveKeepSecond => 'Keep second value';

  @override
  String get proactiveKeepBoth => 'Keep both with context';

  @override
  String get proactiveFirstContext => 'When does the first apply?';

  @override
  String get proactiveSecondContext => 'When does the second apply?';

  @override
  String get proactiveMemoryHistory =>
      'This is your choice. Earlier versions stay in Memory history. Pinned memories must be edited in Memory.';

  @override
  String get proactiveAssumptions => 'Assumptions to revisit';

  @override
  String get proactiveAddAssumption => 'Add an assumption';

  @override
  String get proactiveAssumptionHelp =>
      'Choose a source first. A numeric condition needs a watch with a current observation. Nothing resolves the decision for you.';

  @override
  String get proactiveAssumptionStatement => 'What should Jarvis flag?';

  @override
  String get proactiveReviewWhen => 'Revisit when';

  @override
  String get proactiveSourceChanges => 'The source changes';

  @override
  String get proactiveAbove => 'The observed value rises above';

  @override
  String get proactiveBelow => 'The observed value falls below';

  @override
  String get proactiveThreshold => 'Threshold';

  @override
  String get proactivePreviousPreparation => 'Previous preparation';

  @override
  String get proactivePreparationChanges =>
      'Changes since the previous preparation';

  @override
  String get proactiveCalendarCapacity =>
      'Include this week\'s calendar availability';

  @override
  String get proactiveTripChanges => 'Monitored trip changes';

  @override
  String get proactiveImportantClaims => 'Important claims';

  @override
  String get proactiveAddClaim => 'Track an important claim';

  @override
  String get proactiveClaimStatement => 'Claim to keep checking';

  @override
  String get proactiveClaimHelp =>
      'Select a saved public web source first. Jarvis compares this explicit claim with that source\'s current evidence, cites a verified quotation and keeps ambiguous or unavailable evidence unknown. It never changes your original claim or resolves an outcome automatically.';

  @override
  String get proactiveAddTripMonitor => 'Watch a trip change';

  @override
  String get proactiveShiftByDelay =>
      'Shift this itinerary item by observed delay minutes';

  @override
  String get proactiveTripItem => 'Verified itinerary item';

  @override
  String get proactiveTripDelayHelp =>
      'Select a trip and watch in Sources first. Use a watch whose numeric value is delay minutes for this item. The original time must be verified. Fresh observations prepare affected times and alternatives; bookings stay unchanged. Set this case\'s time zone to the trip\'s local time zone.';

  @override
  String get proactiveWeeklyBudget =>
      'Shared weekly budget, in minutes (optional with calendar)';

  @override
  String get proactiveCalendarCapacityHelp =>
      'Use Today\'s working hours Monday–Friday in this case\'s time zone. Events, including all-day events, count as busy. Completed goal work uses the same weekly budget. Calendar failure stays unknown. Enable this owner-wide responsibility separately after saving.';

  @override
  String get proactiveTrigger => 'Trigger';

  @override
  String get proactiveBefore => 'Before';

  @override
  String get proactiveAfter => 'After';

  @override
  String get proactiveCheckSettings => 'Checks and interruptions';

  @override
  String get proactiveCheckSettingsHelp =>
      'Saving only tracks this work. Enable ongoing checks separately. Optional items share one daily limit; approvals and failures remain visible. Equal quiet-hour values disable quiet hours.';

  @override
  String get proactiveCadence => 'Minutes between checks';

  @override
  String get proactiveDailyChecks => 'Maximum checks per day';

  @override
  String get proactiveOptionalItems => 'Optional items per day';

  @override
  String get proactiveQuietStart => 'Quiet hours start';

  @override
  String get proactiveQuietEnd => 'Quiet hours end';

  @override
  String get proactiveTimeZone => 'Time zone (for example Europe/Amsterdam)';

  @override
  String get proactiveExpiry => 'Stop checking at';

  @override
  String get proactiveMainSource => 'Main record';

  @override
  String get proactiveReviewed => 'Reviewed';

  @override
  String get proactiveCaseNotes => 'References, drafts and case notes';
}
