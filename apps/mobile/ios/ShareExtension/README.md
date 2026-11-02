# Share to Jarvis (iOS Share Extension)

"Share → Jarvis" from any app saves text, links, photos, PDFs and text files **on this phone only**. Nothing is sent to Jarvis or the assistant from the extension; the app lists each item on its **Captures** page (Home tile or More → Captures) where you pick what to do with it: ask Jarvis, keep as a note, add to Files, or remove it.

How it works:

- At sign-in the app writes the signed-in account id to the App Group key `capture_account`. With no value the extension says "Sign in to Jarvis first" and saves nothing, so captures are always bound to an account and server.
- Staged items go to the App Group `group.com.example.jarvis_mobile`: the JSON list `captures_index` plus copied files under `captures/<id>/` (protected until first unlock). The app reads them in `lib/features/captures/capture_store.dart` and the format is documented at the top of `ShareViewController.swift`.
- Limits: 20 MB per file, photos, PDFs and `txt log md markdown csv json` only, 50 captures kept. Signing out clears the list and the staged files.
- Success reads "Saved for review"; failures say why (file too large, type not supported, not signed in) and announce it for VoiceOver.

## Adding the target in Xcode

Once, on a Mac (the project file is not edited by hand):

1. Open `ios/Runner.xcworkspace`, File → New → Target → **Share Extension**, product name `ShareExtension`, embed in **Runner**.
2. Delete the generated files, then add `ShareViewController.swift`, `Info.plist` and `ShareExtension.entitlements` from this folder to the target.
3. Build Settings: **Info.plist File** `ShareExtension/Info.plist`, **Code Signing Entitlements** `ShareExtension/ShareExtension.entitlements`, deployment target iOS 16 or later, bundle id `com.example.jarvis_mobile.ShareExtension`.
4. Signing & Capabilities: add **App Groups** `group.com.example.jarvis_mobile` to the extension and enable it on its app id in the Developer portal (same as the widget; see `../JarvisWidget/README.md`).

## Shortcuts (App Intents)

`../Runner/JarvisShortcuts.swift` adds four Shortcuts and Siri phrases (Ask, Talk, Captures, Chats). Add the file to the **Runner** target (iOS 16+). Each only opens Jarvis through a `jarvis://` link; the app waits for sign-in and the app lock first.

## Status

The Swift code has not been compiled here: cloud sessions have no Xcode. Expect small compile fixes the first time the targets are added. The Dart side (capture store, Captures page, links) is covered by `test/captures_test.dart` and `test/home_widget_sync_test.dart`.
