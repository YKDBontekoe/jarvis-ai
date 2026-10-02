import Flutter
import UIKit
import UserNotifications

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    // Become the notification delegate before Firebase starts. FlutterAppDelegate forwards every callback to
    // firebase_messaging, so taps still reach onMessageOpenedApp, now with the button id.
    let center = UNUserNotificationCenter.current()
    center.delegate = self
    center.setNotificationCategories(NotificationActions.categories)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
  }

  override func userNotificationCenter(
    _ center: UNUserNotificationCenter,
    didReceive response: UNNotificationResponse,
    withCompletionHandler completionHandler: @escaping () -> Void
  ) {
    // Done and Snooze run without opening the app. iOS may suspend it as soon as the handler returns, so keep it
    // awake long enough for Dart to call the API.
    if NotificationActions.runsInBackground(response.actionIdentifier) {
      NotificationActions.keepAwake(UIApplication.shared)
    }
    super.userNotificationCenter(center, didReceive: response, withCompletionHandler: completionHandler)
  }
}

/// Buttons on Jarvis notifications. The push payload picks one with `aps.category`; the ids must match
/// `lib/push/notification_actions.dart` and `NotificationQuickActions` on the server.
enum NotificationActions {
  static let reminderCategory = "jarvis.reminder"
  static let approvalCategory = "jarvis.approval"
  static let reminderDone = "jarvis.reminder.done"
  static let reminderSnooze = "jarvis.reminder.snooze"
  static let approvalOpen = "jarvis.approval.open"

  private static let awakeSeconds: TimeInterval = 20

  static var categories: Set<UNNotificationCategory> {
    let done = UNNotificationAction(
      identifier: reminderDone, title: "Done", options: [],
      icon: UNNotificationActionIcon(systemImageName: "checkmark.circle"))
    let snooze = UNNotificationAction(
      identifier: reminderSnooze, title: "Snooze 10 min", options: [],
      icon: UNNotificationActionIcon(systemImageName: "clock.arrow.circlepath"))
    // Opening needs an unlocked phone because the approval itself is decided in the app.
    let open = UNNotificationAction(
      identifier: approvalOpen, title: "Open", options: [.foreground, .authenticationRequired],
      icon: UNNotificationActionIcon(systemImageName: "arrow.up.forward.app"))
    return [
      UNNotificationCategory(
        identifier: reminderCategory, actions: [done, snooze], intentIdentifiers: [], options: []),
      UNNotificationCategory(
        identifier: approvalCategory, actions: [open], intentIdentifiers: [], options: []),
    ]
  }

  static func runsInBackground(_ actionIdentifier: String) -> Bool {
    actionIdentifier == reminderDone || actionIdentifier == reminderSnooze
  }

  static func keepAwake(_ application: UIApplication) {
    var task = UIBackgroundTaskIdentifier.invalid
    let end = {
      guard task != .invalid else { return }
      application.endBackgroundTask(task)
      task = .invalid
    }
    task = application.beginBackgroundTask(withName: "jarvis.notification-action", expirationHandler: end)
    DispatchQueue.main.asyncAfter(deadline: .now() + awakeSeconds, execute: end)
  }
}
