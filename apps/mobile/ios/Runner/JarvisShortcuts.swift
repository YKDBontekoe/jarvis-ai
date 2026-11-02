import AppIntents
import UIKit

@MainActor
enum UIApplicationOpen {
  static func open(_ url: URL) async {
    _ = await UIApplication.shared.open(url)
  }
}

// App Intents for the Shortcuts app and Siri suggestions (iOS 16+).
// Each intent only opens Jarvis at the right place through the same
// `jarvis://` links the widgets use; the Flutter side (IncomingRoutes) waits
// for sign-in and the app lock before showing anything. No intent reads or
// sends any Jarvis data, so a Shortcut cannot leak anything from a locked app.

private func openJarvis(_ destination: String) -> URL {
  URL(string: "jarvis://\(destination)?homeWidget")!
}

@available(iOS 16.0, *)
struct AskJarvisIntent: AppIntent {
  static var title: LocalizedStringResource = "Ask Jarvis"
  static var description = IntentDescription("Opens a new chat with Jarvis.")
  static var openAppWhenRun = true

  @MainActor
  func perform() async throws -> some IntentResult {
    await UIApplicationOpen.open(openJarvis("ask"))
    return .result()
  }
}

@available(iOS 16.0, *)
struct TalkToJarvisIntent: AppIntent {
  static var title: LocalizedStringResource = "Talk to Jarvis"
  static var description = IntentDescription("Opens Jarvis ready for a voice conversation.")
  static var openAppWhenRun = true

  @MainActor
  func perform() async throws -> some IntentResult {
    await UIApplicationOpen.open(openJarvis("voice"))
    return .result()
  }
}

@available(iOS 16.0, *)
struct OpenCapturesIntent: AppIntent {
  static var title: LocalizedStringResource = "Open Jarvis Captures"
  static var description = IntentDescription("Opens the things you shared to Jarvis and have not reviewed yet.")
  static var openAppWhenRun = true

  @MainActor
  func perform() async throws -> some IntentResult {
    await UIApplicationOpen.open(openJarvis("captures"))
    return .result()
  }
}

@available(iOS 16.0, *)
struct OpenChatsIntent: AppIntent {
  static var title: LocalizedStringResource = "Open Jarvis Chats"
  static var description = IntentDescription("Opens your chats with Jarvis.")
  static var openAppWhenRun = true

  @MainActor
  func perform() async throws -> some IntentResult {
    await UIApplicationOpen.open(openJarvis("chats"))
    return .result()
  }
}

@available(iOS 16.0, *)
struct JarvisShortcuts: AppShortcutsProvider {
  static var appShortcuts: [AppShortcut] {
    AppShortcut(
      intent: AskJarvisIntent(),
      phrases: ["Ask \(.applicationName)", "New chat in \(.applicationName)"],
      shortTitle: "Ask Jarvis", systemImageName: "bubble.left")
    AppShortcut(
      intent: TalkToJarvisIntent(),
      phrases: ["Talk to \(.applicationName)"],
      shortTitle: "Talk to Jarvis", systemImageName: "mic")
    AppShortcut(
      intent: OpenCapturesIntent(),
      phrases: ["Open \(.applicationName) captures"],
      shortTitle: "Captures", systemImageName: "tray")
    AppShortcut(
      intent: OpenChatsIntent(),
      phrases: ["Open \(.applicationName) chats"],
      shortTitle: "Chats", systemImageName: "bubble.left.and.bubble.right")
  }
}
