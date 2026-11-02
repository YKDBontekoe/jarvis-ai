import UIKit
import UniformTypeIdentifiers

// Share Extension: "Share → Jarvis" from any app. It only stages what was
// shared on this phone (in the App Group the app also reads) and says
// "Saved for review". Nothing is sent to Jarvis or the assistant here: the app
// shows each capture on its Captures page and the owner decides what to do.
//
// Staging format (read by lib/features/captures/capture_store.dart):
//   UserDefaults(suiteName: appGroup)["captures_index"]  JSON array of captures
//   <App Group container>/captures/<id>/<file name>      copied files
// A capture: { id, kind: "text" | "link" | "file", createdAt (ISO 8601),
//              account, text?, url?, fileName?, filePath?, mimeType?, sizeBytes }
// `account` is the value the app wrote to UserDefaults["capture_account"] at
// sign-in; with no signed-in account the extension refuses to save.

final class ShareViewController: UIViewController {
  private let appGroup = "group.com.example.jarvis_mobile"
  private let indexKey = "captures_index"
  private let accountKey = "capture_account"
  private let maxBytes = 20 * 1024 * 1024
  private let maxItems = 50
  private let allowedExtensions: Set<String> = [
    "pdf", "txt", "log", "md", "markdown", "csv", "json", "jpg", "jpeg", "png", "webp",
  ]

  private let titleLabel = UILabel()
  private let detailLabel = UILabel()
  private let spinner = UIActivityIndicatorView(style: .medium)

  override func viewDidLoad() {
    super.viewDidLoad()
    view.backgroundColor = UIColor.systemBackground
    titleLabel.font = .preferredFont(forTextStyle: .headline)
    titleLabel.adjustsFontForContentSizeCategory = true
    titleLabel.textAlignment = .center
    titleLabel.numberOfLines = 0
    detailLabel.font = .preferredFont(forTextStyle: .subheadline)
    detailLabel.adjustsFontForContentSizeCategory = true
    detailLabel.textColor = .secondaryLabel
    detailLabel.textAlignment = .center
    detailLabel.numberOfLines = 0
    let stack = UIStackView(arrangedSubviews: [spinner, titleLabel, detailLabel])
    stack.axis = .vertical
    stack.spacing = 10
    stack.alignment = .center
    stack.translatesAutoresizingMaskIntoConstraints = false
    view.addSubview(stack)
    NSLayoutConstraint.activate([
      stack.centerYAnchor.constraint(equalTo: view.centerYAnchor),
      stack.leadingAnchor.constraint(equalTo: view.layoutMarginsGuide.leadingAnchor),
      stack.trailingAnchor.constraint(equalTo: view.layoutMarginsGuide.trailingAnchor),
    ])
    spinner.startAnimating()
    titleLabel.text = "Saving…"
  }

  override func viewDidAppear(_ animated: Bool) {
    super.viewDidAppear(animated)
    Task { await stage() }
  }

  // MARK: - Staging

  private struct Problem: Error { let message: String }

  private func stage() async {
    let defaults = UserDefaults(suiteName: appGroup)
    let account = defaults?.string(forKey: accountKey) ?? ""
    guard !account.isEmpty else {
      finish(title: "Sign in to Jarvis first", detail: "Open Jarvis, sign in, then share again.", saved: 0)
      return
    }
    guard let container = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup) else {
      finish(title: "Could not save", detail: "Jarvis storage is not available.", saved: 0)
      return
    }

    var captures: [[String: Any]] = []
    var problems: [String] = []
    let providers = (extensionContext?.inputItems as? [NSExtensionItem] ?? [])
      .flatMap { $0.attachments ?? [] }

    for provider in providers {
      do {
        if let capture = try await capture(from: provider, account: account, container: container) {
          captures.append(capture)
        }
      } catch let problem as Problem {
        problems.append(problem.message)
      } catch {
        problems.append("Something could not be saved.")
      }
    }

    guard !captures.isEmpty else {
      finish(title: "Nothing was saved", detail: problems.first ?? "Jarvis cannot keep this.", saved: 0)
      return
    }
    append(captures, to: defaults)
    let detail = problems.isEmpty
      ? "Open Jarvis to decide what to do with it."
      : "Some items were skipped: \(problems.first ?? "")"
    finish(title: "Saved for review", detail: detail, saved: captures.count)
  }

  private func capture(from provider: NSItemProvider, account: String, container: URL) async throws -> [String: Any]? {
    let id = UUID().uuidString
    let now = ISO8601DateFormatter().string(from: Date())

    // Files and photos first: a photo also conforms to plain data.
    for type in [UTType.image, UTType.pdf, UTType.plainText, UTType.json, UTType.commaSeparatedText, UTType.fileURL, UTType.data] {
      guard provider.hasItemConformingToTypeIdentifier(type.identifier) else { continue }
      if type == .plainText, !provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
        continue // plain text without a file is a text capture, handled below
      }
      if let url = try? await loadFile(provider, type: type) {
        return try copyFile(url, id: id, account: account, container: container, createdAt: now)
      }
    }

    if provider.hasItemConformingToTypeIdentifier(UTType.url.identifier),
      let item = try? await provider.loadItem(forTypeIdentifier: UTType.url.identifier),
      let url = item as? URL, url.scheme == "https" || url.scheme == "http"
    {
      return [
        "id": id, "kind": "link", "createdAt": now, "account": account,
        "url": url.absoluteString, "sizeBytes": 0,
      ]
    }

    if provider.hasItemConformingToTypeIdentifier(UTType.plainText.identifier),
      let item = try? await provider.loadItem(forTypeIdentifier: UTType.plainText.identifier),
      let text = (item as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty
    {
      // A link pasted as text is still a link.
      if let url = URL(string: text), url.scheme == "https" || url.scheme == "http", !text.contains(" ") {
        return ["id": id, "kind": "link", "createdAt": now, "account": account, "url": text, "sizeBytes": 0]
      }
      return ["id": id, "kind": "text", "createdAt": now, "account": account, "text": text, "sizeBytes": 0]
    }
    return nil
  }

  private func loadFile(_ provider: NSItemProvider, type: UTType) async throws -> URL {
    try await withCheckedThrowingContinuation { continuation in
      provider.loadFileRepresentation(forTypeIdentifier: type.identifier) { url, error in
        if let url {
          // The system deletes this file when the closure returns, so copy it now.
          let copy = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathExtension(url.pathExtension)
          do {
            try FileManager.default.copyItem(at: url, to: copy)
            continuation.resume(returning: copy)
          } catch {
            continuation.resume(throwing: error)
          }
        } else {
          continuation.resume(throwing: error ?? Problem(message: "A file could not be read."))
        }
      }
    }
  }

  private func copyFile(_ source: URL, id: String, account: String, container: URL, createdAt: String) throws -> [String: Any] {
    let attributes = try FileManager.default.attributesOfItem(atPath: source.path)
    let size = (attributes[.size] as? NSNumber)?.intValue ?? 0
    let name = source.lastPathComponent
    let ext = source.pathExtension.lowercased()
    defer { try? FileManager.default.removeItem(at: source) }
    guard allowedExtensions.contains(ext) else {
      throw Problem(message: "Jarvis can keep photos, PDFs and text files, not .\(ext).")
    }
    guard size > 0 else { throw Problem(message: "This file is empty.") }
    guard size <= maxBytes else { throw Problem(message: "This file is larger than 20 MB.") }

    // Copied into protected storage before the extension finishes.
    let folder = container.appendingPathComponent("captures/\(id)", isDirectory: true)
    try FileManager.default.createDirectory(
      at: folder, withIntermediateDirectories: true,
      attributes: [.protectionKey: FileProtectionType.completeUntilFirstUserAuthentication])
    let destination = folder.appendingPathComponent(name)
    try FileManager.default.copyItem(at: source, to: destination)
    let mime = UTType(filenameExtension: ext)?.preferredMIMEType ?? "application/octet-stream"
    return [
      "id": id, "kind": "file", "createdAt": createdAt, "account": account,
      "fileName": name, "filePath": destination.path, "mimeType": mime, "sizeBytes": size,
    ]
  }

  private func append(_ captures: [[String: Any]], to defaults: UserDefaults?) {
    var all: [[String: Any]] = []
    if let json = defaults?.string(forKey: indexKey), let data = json.data(using: .utf8),
      let existing = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]]
    {
      all = existing
    }
    all.append(contentsOf: captures)
    if all.count > maxItems { all = Array(all.suffix(maxItems)) }
    if let data = try? JSONSerialization.data(withJSONObject: all), let json = String(data: data, encoding: .utf8) {
      defaults?.set(json, forKey: indexKey)
    }
  }

  // MARK: - Finish

  private func finish(title: String, detail: String, saved: Int) {
    DispatchQueue.main.async {
      self.spinner.stopAnimating()
      self.titleLabel.text = title
      self.detailLabel.text = detail
      UIAccessibility.post(notification: .announcement, argument: "\(title). \(detail)")
      // Long enough to read it; failures stay a little longer.
      let delay: TimeInterval = saved > 0 ? 1.2 : 2.6
      DispatchQueue.main.asyncAfter(deadline: .now() + delay) {
        if saved > 0 {
          self.extensionContext?.completeRequest(returningItems: nil)
        } else {
          self.extensionContext?.cancelRequest(withError: NSError(domain: "jarvis.share", code: 1))
        }
      }
    }
  }
}
