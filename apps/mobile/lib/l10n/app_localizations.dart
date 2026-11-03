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

  /// No description provided for @capturesTitle.
  ///
  /// In en, this message translates to:
  /// **'Captures'**
  String get capturesTitle;

  /// No description provided for @capturesEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'Nothing shared yet'**
  String get capturesEmptyTitle;

  /// No description provided for @capturesEmptyMessage.
  ///
  /// In en, this message translates to:
  /// **'Use Share in another app and choose Jarvis. What you share waits here until you decide what to do with it.'**
  String get capturesEmptyMessage;

  /// No description provided for @capturesDiscardTitle.
  ///
  /// In en, this message translates to:
  /// **'Discard this capture?'**
  String get capturesDiscardTitle;

  /// No description provided for @capturesDiscardMessage.
  ///
  /// In en, this message translates to:
  /// **'It will be removed from this phone. Nothing is sent anywhere.'**
  String get capturesDiscardMessage;

  /// No description provided for @capturesKeep.
  ///
  /// In en, this message translates to:
  /// **'Keep'**
  String get capturesKeep;

  /// No description provided for @capturesDiscard.
  ///
  /// In en, this message translates to:
  /// **'Discard'**
  String get capturesDiscard;

  /// No description provided for @capturesLinkNote.
  ///
  /// In en, this message translates to:
  /// **'Add a note about this link (optional)'**
  String get capturesLinkNote;

  /// No description provided for @capturesEditFirst.
  ///
  /// In en, this message translates to:
  /// **'Edit before you decide'**
  String get capturesEditFirst;

  /// No description provided for @capturesSaveToFiles.
  ///
  /// In en, this message translates to:
  /// **'Save to Files'**
  String get capturesSaveToFiles;

  /// No description provided for @capturesSaveToLibrary.
  ///
  /// In en, this message translates to:
  /// **'Save to Library'**
  String get capturesSaveToLibrary;

  /// No description provided for @capturesAsk.
  ///
  /// In en, this message translates to:
  /// **'Ask Jarvis'**
  String get capturesAsk;

  /// No description provided for @capturesRemind.
  ///
  /// In en, this message translates to:
  /// **'Create reminder'**
  String get capturesRemind;

  /// No description provided for @hideInSwitcherTitle.
  ///
  /// In en, this message translates to:
  /// **'Hide in app switcher'**
  String get hideInSwitcherTitle;

  /// No description provided for @hideInSwitcherSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Blur Jarvis when you swipe up or switch apps'**
  String get hideInSwitcherSubtitle;

  /// Proactive work: Title
  ///
  /// In en, this message translates to:
  /// **'Ongoing work'**
  String get proactiveTitle;

  /// Proactive work: New
  ///
  /// In en, this message translates to:
  /// **'New responsibility'**
  String get proactiveNew;

  /// Proactive work: Description
  ///
  /// In en, this message translates to:
  /// **'The things you asked Jarvis to keep moving. Every change has a reason and a history.'**
  String get proactiveDescription;

  /// Proactive work: Empty
  ///
  /// In en, this message translates to:
  /// **'Nothing tracked yet'**
  String get proactiveEmpty;

  /// Proactive work: Confirm
  ///
  /// In en, this message translates to:
  /// **'Update this responsibility?'**
  String get proactiveConfirm;

  /// Proactive work: ConfirmBody
  ///
  /// In en, this message translates to:
  /// **'Your choice is kept in the history. Resolving or stopping ends ongoing checks.'**
  String get proactiveConfirmBody;

  /// Proactive work: Continue
  ///
  /// In en, this message translates to:
  /// **'Continue'**
  String get proactiveContinue;

  /// Proactive work: Edit
  ///
  /// In en, this message translates to:
  /// **'Edit responsibility'**
  String get proactiveEdit;

  /// Proactive work: Open
  ///
  /// In en, this message translates to:
  /// **'Open'**
  String get proactiveOpen;

  /// Proactive work: Suggested
  ///
  /// In en, this message translates to:
  /// **'Suggested · checks off'**
  String get proactiveSuggested;

  /// Proactive work: Paused
  ///
  /// In en, this message translates to:
  /// **'Paused'**
  String get proactivePaused;

  /// Proactive work: Resolved
  ///
  /// In en, this message translates to:
  /// **'Resolved'**
  String get proactiveResolved;

  /// Proactive work: Stopped
  ///
  /// In en, this message translates to:
  /// **'Stopped'**
  String get proactiveStopped;

  /// Proactive work: NeedsInput
  ///
  /// In en, this message translates to:
  /// **'Needs you'**
  String get proactiveNeedsInput;

  /// Proactive work: Checking
  ///
  /// In en, this message translates to:
  /// **'Ongoing checks enabled'**
  String get proactiveChecking;

  /// Proactive work: Tracked
  ///
  /// In en, this message translates to:
  /// **'Tracked · checks off'**
  String get proactiveTracked;

  /// Proactive work: StopWhen
  ///
  /// In en, this message translates to:
  /// **'Stop when'**
  String get proactiveStopWhen;

  /// Proactive work: Accept
  ///
  /// In en, this message translates to:
  /// **'Accept'**
  String get proactiveAccept;

  /// Proactive work: Pause
  ///
  /// In en, this message translates to:
  /// **'Pause'**
  String get proactivePause;

  /// Proactive work: Enable
  ///
  /// In en, this message translates to:
  /// **'Enable checks'**
  String get proactiveEnable;

  /// Proactive work: Resolve
  ///
  /// In en, this message translates to:
  /// **'Mark resolved'**
  String get proactiveResolve;

  /// Proactive work: Stop
  ///
  /// In en, this message translates to:
  /// **'Stop'**
  String get proactiveStop;

  /// Proactive work: Evidence
  ///
  /// In en, this message translates to:
  /// **'Evidence'**
  String get proactiveEvidence;

  /// Proactive work: Steps
  ///
  /// In en, this message translates to:
  /// **'Next steps'**
  String get proactiveSteps;

  /// Proactive work: Done
  ///
  /// In en, this message translates to:
  /// **'Done'**
  String get proactiveDone;

  /// Proactive work: Sources
  ///
  /// In en, this message translates to:
  /// **'Sources'**
  String get proactiveSources;

  /// Proactive work: History
  ///
  /// In en, this message translates to:
  /// **'What Jarvis did'**
  String get proactiveHistory;

  /// Proactive work: Undo
  ///
  /// In en, this message translates to:
  /// **'Undo'**
  String get proactiveUndo;

  /// Proactive work: Feedback
  ///
  /// In en, this message translates to:
  /// **'Did this help?'**
  String get proactiveFeedback;

  /// Proactive work: Useful
  ///
  /// In en, this message translates to:
  /// **'Useful'**
  String get proactiveUseful;

  /// Proactive work: NotUseful
  ///
  /// In en, this message translates to:
  /// **'Not useful'**
  String get proactiveNotUseful;

  /// Proactive work: TooEarly
  ///
  /// In en, this message translates to:
  /// **'Too early'**
  String get proactiveTooEarly;

  /// Proactive work: TooMuch
  ///
  /// In en, this message translates to:
  /// **'Too much effort'**
  String get proactiveTooMuch;

  /// Proactive work: Save
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get proactiveSave;

  /// Proactive work: Name
  ///
  /// In en, this message translates to:
  /// **'Name'**
  String get proactiveName;

  /// Proactive work: Required
  ///
  /// In en, this message translates to:
  /// **'Fill this in'**
  String get proactiveRequired;

  /// Proactive work: Objective
  ///
  /// In en, this message translates to:
  /// **'What should Jarvis keep moving?'**
  String get proactiveObjective;

  /// Proactive work: Kind
  ///
  /// In en, this message translates to:
  /// **'Kind of work'**
  String get proactiveKind;

  /// Proactive work: KindIntention
  ///
  /// In en, this message translates to:
  /// **'Unfinished intention'**
  String get proactiveKindIntention;

  /// Proactive work: KindProject
  ///
  /// In en, this message translates to:
  /// **'Project follow-through'**
  String get proactiveKindProject;

  /// Proactive work: KindFollow
  ///
  /// In en, this message translates to:
  /// **'Waiting for a reply'**
  String get proactiveKindFollow;

  /// Proactive work: KindContext
  ///
  /// In en, this message translates to:
  /// **'Keep context current'**
  String get proactiveKindContext;

  /// Proactive work: KindReturn
  ///
  /// In en, this message translates to:
  /// **'Return'**
  String get proactiveKindReturn;

  /// Proactive work: KindRefund
  ///
  /// In en, this message translates to:
  /// **'Refund'**
  String get proactiveKindRefund;

  /// Proactive work: KindRepair
  ///
  /// In en, this message translates to:
  /// **'Repair'**
  String get proactiveKindRepair;

  /// Proactive work: EnableExplanation
  ///
  /// In en, this message translates to:
  /// **'Saving tracks this work. Enable ongoing checks separately when you are ready. Existing permissions still apply.'**
  String get proactiveEnableExplanation;

  /// Proactive work: LastCheck
  ///
  /// In en, this message translates to:
  /// **'Last checked'**
  String get proactiveLastCheck;

  /// Proactive work: NextCheck
  ///
  /// In en, this message translates to:
  /// **'Next check'**
  String get proactiveNextCheck;

  /// Proactive work: Preparation
  ///
  /// In en, this message translates to:
  /// **'Prepared for you'**
  String get proactivePreparation;

  /// Proactive work: Capacity
  ///
  /// In en, this message translates to:
  /// **'Time available, in minutes'**
  String get proactiveCapacity;

  /// Proactive work: ValidMinutes
  ///
  /// In en, this message translates to:
  /// **'Enter a valid number of minutes'**
  String get proactiveValidMinutes;

  /// Proactive work: Deadline
  ///
  /// In en, this message translates to:
  /// **'Choose a deadline'**
  String get proactiveDeadline;

  /// Proactive work: ClearDeadline
  ///
  /// In en, this message translates to:
  /// **'Clear deadline'**
  String get proactiveClearDeadline;

  /// Proactive work: Remove
  ///
  /// In en, this message translates to:
  /// **'Remove'**
  String get proactiveRemove;

  /// Proactive work: AddStep
  ///
  /// In en, this message translates to:
  /// **'Add a step'**
  String get proactiveAddStep;

  /// Proactive work: Effort
  ///
  /// In en, this message translates to:
  /// **'Estimated minutes, if known'**
  String get proactiveEffort;

  /// Proactive work: Completion
  ///
  /// In en, this message translates to:
  /// **'How will you know it is done?'**
  String get proactiveCompletion;

  /// Proactive work: Source
  ///
  /// In en, this message translates to:
  /// **'Linked source, optional'**
  String get proactiveSource;

  /// Proactive work: DependsOn
  ///
  /// In en, this message translates to:
  /// **'Waits for these steps'**
  String get proactiveDependsOn;

  /// Proactive work: Cancel
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get proactiveCancel;

  /// Proactive work: KindDeadline
  ///
  /// In en, this message translates to:
  /// **'Deadline risk'**
  String get proactiveKindDeadline;

  /// Proactive work: KindDay
  ///
  /// In en, this message translates to:
  /// **'Adaptive day plan'**
  String get proactiveKindDay;

  /// Proactive work: KindMeeting
  ///
  /// In en, this message translates to:
  /// **'Meeting continuity'**
  String get proactiveKindMeeting;

  /// Proactive work: KindCapacity
  ///
  /// In en, this message translates to:
  /// **'Shared goal capacity'**
  String get proactiveKindCapacity;

  /// Proactive work: KindTrip
  ///
  /// In en, this message translates to:
  /// **'Trip monitoring'**
  String get proactiveKindTrip;

  /// Proactive work: KindConsistency
  ///
  /// In en, this message translates to:
  /// **'Related changes'**
  String get proactiveKindConsistency;

  /// Proactive work: KindDecision
  ///
  /// In en, this message translates to:
  /// **'Decision assumptions'**
  String get proactiveKindDecision;

  /// Proactive work: KindResearch
  ///
  /// In en, this message translates to:
  /// **'Research updates'**
  String get proactiveKindResearch;

  /// Proactive work: KindMemory
  ///
  /// In en, this message translates to:
  /// **'Memory comparison'**
  String get proactiveKindMemory;

  /// Proactive work: KindTrial
  ///
  /// In en, this message translates to:
  /// **'Routine observation'**
  String get proactiveKindTrial;

  /// Proactive work: Pin
  ///
  /// In en, this message translates to:
  /// **'Keep this block in place'**
  String get proactivePin;

  /// Proactive work: Unpin
  ///
  /// In en, this message translates to:
  /// **'Allow this block to move'**
  String get proactiveUnpin;

  /// A document has changed source facts; owner edits were preserved.
  ///
  /// In en, this message translates to:
  /// **'Supporting information changed. Review the affected sections or rebuild them from sources. Your wording has been kept.'**
  String get proactiveDraftStale;

  /// Proactive memory comparison: proactiveReviewMemories
  ///
  /// In en, this message translates to:
  /// **'Review these memories'**
  String get proactiveReviewMemories;

  /// Proactive memory comparison: proactiveKeepFirst
  ///
  /// In en, this message translates to:
  /// **'Keep first value'**
  String get proactiveKeepFirst;

  /// Proactive memory comparison: proactiveKeepSecond
  ///
  /// In en, this message translates to:
  /// **'Keep second value'**
  String get proactiveKeepSecond;

  /// Proactive memory comparison: proactiveKeepBoth
  ///
  /// In en, this message translates to:
  /// **'Keep both with context'**
  String get proactiveKeepBoth;

  /// Proactive memory comparison: proactiveFirstContext
  ///
  /// In en, this message translates to:
  /// **'When does the first apply?'**
  String get proactiveFirstContext;

  /// Proactive memory comparison: proactiveSecondContext
  ///
  /// In en, this message translates to:
  /// **'When does the second apply?'**
  String get proactiveSecondContext;

  /// Proactive memory comparison: proactiveMemoryHistory
  ///
  /// In en, this message translates to:
  /// **'This is your choice. Earlier versions stay in Memory history. Pinned memories must be edited in Memory.'**
  String get proactiveMemoryHistory;

  /// Proactive assumption monitoring: proactiveAssumptions
  ///
  /// In en, this message translates to:
  /// **'Assumptions to revisit'**
  String get proactiveAssumptions;

  /// Proactive assumption monitoring: proactiveAddAssumption
  ///
  /// In en, this message translates to:
  /// **'Add an assumption'**
  String get proactiveAddAssumption;

  /// Proactive assumption monitoring: proactiveAssumptionHelp
  ///
  /// In en, this message translates to:
  /// **'Choose a source first. A numeric condition needs a watch with a current observation. Nothing resolves the decision for you.'**
  String get proactiveAssumptionHelp;

  /// Proactive assumption monitoring: proactiveAssumptionStatement
  ///
  /// In en, this message translates to:
  /// **'What should Jarvis flag?'**
  String get proactiveAssumptionStatement;

  /// Proactive assumption monitoring: proactiveReviewWhen
  ///
  /// In en, this message translates to:
  /// **'Revisit when'**
  String get proactiveReviewWhen;

  /// Proactive assumption monitoring: proactiveSourceChanges
  ///
  /// In en, this message translates to:
  /// **'The source changes'**
  String get proactiveSourceChanges;

  /// Proactive assumption monitoring: proactiveAbove
  ///
  /// In en, this message translates to:
  /// **'The observed value rises above'**
  String get proactiveAbove;

  /// Proactive assumption monitoring: proactiveBelow
  ///
  /// In en, this message translates to:
  /// **'The observed value falls below'**
  String get proactiveBelow;

  /// Proactive assumption monitoring: proactiveThreshold
  ///
  /// In en, this message translates to:
  /// **'Threshold'**
  String get proactiveThreshold;

  /// No description provided for @proactivePreviousPreparation.
  ///
  /// In en, this message translates to:
  /// **'Previous preparation'**
  String get proactivePreviousPreparation;

  /// No description provided for @proactivePreparationChanges.
  ///
  /// In en, this message translates to:
  /// **'Changes since the previous preparation'**
  String get proactivePreparationChanges;

  /// No description provided for @proactiveCalendarCapacity.
  ///
  /// In en, this message translates to:
  /// **'Include this week\'s calendar availability'**
  String get proactiveCalendarCapacity;

  /// No description provided for @proactiveTripChanges.
  ///
  /// In en, this message translates to:
  /// **'Monitored trip changes'**
  String get proactiveTripChanges;

  /// No description provided for @proactiveImportantClaims.
  ///
  /// In en, this message translates to:
  /// **'Important claims'**
  String get proactiveImportantClaims;

  /// No description provided for @proactiveAddClaim.
  ///
  /// In en, this message translates to:
  /// **'Track an important claim'**
  String get proactiveAddClaim;

  /// No description provided for @proactiveClaimStatement.
  ///
  /// In en, this message translates to:
  /// **'Claim to keep checking'**
  String get proactiveClaimStatement;

  /// No description provided for @proactiveClaimHelp.
  ///
  /// In en, this message translates to:
  /// **'Select a saved public web source first. Jarvis compares this explicit claim with that source\'s current evidence, cites a verified quotation and keeps ambiguous or unavailable evidence unknown. It never changes your original claim or resolves an outcome automatically.'**
  String get proactiveClaimHelp;

  /// No description provided for @proactiveAddTripMonitor.
  ///
  /// In en, this message translates to:
  /// **'Watch a trip change'**
  String get proactiveAddTripMonitor;

  /// No description provided for @proactiveShiftByDelay.
  ///
  /// In en, this message translates to:
  /// **'Shift this itinerary item by observed delay minutes'**
  String get proactiveShiftByDelay;

  /// No description provided for @proactiveTripItem.
  ///
  /// In en, this message translates to:
  /// **'Verified itinerary item'**
  String get proactiveTripItem;

  /// No description provided for @proactiveTripDelayHelp.
  ///
  /// In en, this message translates to:
  /// **'Select a trip and watch in Sources first. Use a watch whose numeric value is delay minutes for this item. The original time must be verified. Fresh observations prepare affected times and alternatives; bookings stay unchanged. Set this case\'s time zone to the trip\'s local time zone.'**
  String get proactiveTripDelayHelp;

  /// No description provided for @proactiveWeeklyBudget.
  ///
  /// In en, this message translates to:
  /// **'Shared weekly budget, in minutes (optional with calendar)'**
  String get proactiveWeeklyBudget;

  /// No description provided for @proactiveCalendarCapacityHelp.
  ///
  /// In en, this message translates to:
  /// **'Use Today\'s working hours Monday–Friday in this case\'s time zone. Events, including all-day events, count as busy. Completed goal work uses the same weekly budget. Calendar failure stays unknown. Enable this owner-wide responsibility separately after saving.'**
  String get proactiveCalendarCapacityHelp;

  /// No description provided for @proactiveTrigger.
  ///
  /// In en, this message translates to:
  /// **'Trigger'**
  String get proactiveTrigger;

  /// No description provided for @proactiveBefore.
  ///
  /// In en, this message translates to:
  /// **'Before'**
  String get proactiveBefore;

  /// No description provided for @proactiveAfter.
  ///
  /// In en, this message translates to:
  /// **'After'**
  String get proactiveAfter;

  /// No description provided for @proactiveCheckSettings.
  ///
  /// In en, this message translates to:
  /// **'Checks and interruptions'**
  String get proactiveCheckSettings;

  /// No description provided for @proactiveCheckSettingsHelp.
  ///
  /// In en, this message translates to:
  /// **'Saving only tracks this work. Enable ongoing checks separately. Optional items share one daily limit; approvals and failures remain visible. Equal quiet-hour values disable quiet hours.'**
  String get proactiveCheckSettingsHelp;

  /// No description provided for @proactiveCadence.
  ///
  /// In en, this message translates to:
  /// **'Minutes between checks'**
  String get proactiveCadence;

  /// No description provided for @proactiveDailyChecks.
  ///
  /// In en, this message translates to:
  /// **'Maximum checks per day'**
  String get proactiveDailyChecks;

  /// No description provided for @proactiveOptionalItems.
  ///
  /// In en, this message translates to:
  /// **'Optional items per day'**
  String get proactiveOptionalItems;

  /// No description provided for @proactiveQuietStart.
  ///
  /// In en, this message translates to:
  /// **'Quiet hours start'**
  String get proactiveQuietStart;

  /// No description provided for @proactiveQuietEnd.
  ///
  /// In en, this message translates to:
  /// **'Quiet hours end'**
  String get proactiveQuietEnd;

  /// No description provided for @proactiveTimeZone.
  ///
  /// In en, this message translates to:
  /// **'Time zone (for example Europe/Amsterdam)'**
  String get proactiveTimeZone;

  /// No description provided for @proactiveExpiry.
  ///
  /// In en, this message translates to:
  /// **'Stop checking at'**
  String get proactiveExpiry;

  /// No description provided for @proactiveMainSource.
  ///
  /// In en, this message translates to:
  /// **'Main record'**
  String get proactiveMainSource;

  /// Acknowledge the current assumption evidence without resolving the decision.
  ///
  /// In en, this message translates to:
  /// **'Reviewed'**
  String get proactiveReviewed;

  /// Owner-authored references, drafts and notes kept with a responsibility.
  ///
  /// In en, this message translates to:
  /// **'References, drafts and case notes'**
  String get proactiveCaseNotes;
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
