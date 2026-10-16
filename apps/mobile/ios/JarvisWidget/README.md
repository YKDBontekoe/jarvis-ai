# Jarvis home screen widgets (iOS)

Two WidgetKit widgets, written in SwiftUI:

- **Jarvis** (`JarvisWidget`, small and medium): the Right now card (a plan starting soon with a live countdown, the morning brief, a low battery or tomorrow). The medium size adds the first three things in Needs you. Tapping it opens Home.
- **Ask Jarvis** (`JarvisAskWidget`, small and medium): opens a new chat. The medium size has separate Type and Talk buttons.

The widgets never call the API. After each Home refresh the app writes what Home shows into the App Group `group.com.example.jarvis_mobile` and asks WidgetKit to reload (`lib/features/home/home_widget_sync.dart`, through the `home_widget` package). Keys: `hero_kind`, `hero_title`, `hero_detail`, `hero_starts_at` (ms since epoch, UTC), `day_line`, `needs_count`, `needs_items` (JSON list of `{title, detail, kind}`), `updated_at`.

Taps open `jarvis://home`, `jarvis://ask` or `jarvis://voice` (with `?homeWidget`); the `jarvis` URL scheme is registered in `Runner/Info.plist` and the app routes these in `chat_screen.dart`.

## Adding the target in Xcode

The extension target is not in `Runner.xcodeproj` yet; the project file is not edited by hand. Once, on a Mac:

1. Open `ios/Runner.xcworkspace`.
2. File → New → Target → **Widget Extension**. Product name `JarvisWidget`, no Live Activity, no configuration intent. Embed in **Runner**.
3. Delete the Swift files Xcode generated in the new group, then add `JarvisWidget.swift`, `Info.plist` and `JarvisWidget.entitlements` from this folder to the `JarvisWidget` target.
4. In the target's Build Settings set **Info.plist File** to `JarvisWidget/Info.plist`, **Code Signing Entitlements** to `JarvisWidget/JarvisWidget.entitlements`, the deployment target to iOS 16 or later, and the bundle id to `com.example.jarvis_mobile.JarvisWidget` (your app id plus `.JarvisWidget`).
5. Under Signing & Capabilities add **App Groups** with `group.com.example.jarvis_mobile` to both **Runner** and **JarvisWidget**, and enable the group on both app ids in the Apple Developer portal so the release provisioning profiles (`.github/workflows/release-ios.yml`) carry it. `Runner/Runner.entitlements` does not list the group yet on purpose: a profile without the capability would fail release signing. Until then the app's writes stay in its own defaults and the widget shows its calm state. If you change the bundle id, change the group in both entitlements, in `JarvisWidget.swift` (`appGroup`) and in `HomeWidgetSync.appGroup`.
6. Build and run Runner, open Home once, then add the widget from the home screen.

## Status

The Swift code has not been compiled here: this repository's CI and cloud sessions have no Xcode. Expect to fix small compile issues the first time the target is added. The Dart side (payload, sync, deep links) is covered by `test/home_widget_sync_test.dart`.
