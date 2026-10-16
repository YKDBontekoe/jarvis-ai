import SwiftUI
import WidgetKit

// Home screen widgets for Jarvis. The app writes what Home shows into the
// shared App Group (lib/features/home/home_widget_sync.dart); these views only
// read it. Nothing here talks to the network.

private let appGroup = "group.com.example.jarvis_mobile"

// MARK: - Data

struct NeedsYouLine: Codable, Hashable {
  let title: String
  let detail: String
  let kind: String
}

struct JarvisEntry: TimelineEntry {
  let date: Date
  let heroKind: String
  let heroTitle: String
  let heroDetail: String
  let heroStartsAt: Date?
  let dayLine: String
  let needsCount: Int
  let needs: [NeedsYouLine]

  static let placeholder = JarvisEntry(
    date: Date(),
    heroKind: "soon",
    heroTitle: "Design review",
    heroDetail: "With the product team",
    heroStartsAt: Date().addingTimeInterval(34 * 60),
    dayLine: "Design review soon, then you are free.",
    needsCount: 3,
    needs: [
      NeedsYouLine(title: "Rosa", detail: "Je bent de beste · 14 h", kind: "person"),
      NeedsYouLine(title: "Bring the charger", detail: "Overdue · 20 min", kind: "reminder"),
      NeedsYouLine(title: "Create calendar event", detail: "Jarvis is waiting for your OK", kind: "approval"),
    ]
  )

  /// Reads what the app last wrote. Missing values fall back to a calm state.
  static func load(now: Date = Date()) -> JarvisEntry {
    let store = UserDefaults(suiteName: appGroup)
    let startsAt = (store?.object(forKey: "hero_starts_at") as? NSNumber).map {
      Date(timeIntervalSince1970: $0.doubleValue / 1000)
    }
    var needs: [NeedsYouLine] = []
    if let json = store?.string(forKey: "needs_items"), let data = json.data(using: .utf8) {
      needs = (try? JSONDecoder().decode([NeedsYouLine].self, from: data)) ?? []
    }
    return JarvisEntry(
      date: now,
      heroKind: store?.string(forKey: "hero_kind") ?? "none",
      heroTitle: store?.string(forKey: "hero_title") ?? "Nothing pressing",
      heroDetail: store?.string(forKey: "hero_detail") ?? "",
      heroStartsAt: startsAt,
      dayLine: store?.string(forKey: "day_line") ?? "",
      needsCount: store?.integer(forKey: "needs_count") ?? 0,
      needs: needs
    )
  }
}

struct JarvisProvider: TimelineProvider {
  func placeholder(in context: Context) -> JarvisEntry { .placeholder }

  func getSnapshot(in context: Context, completion: @escaping (JarvisEntry) -> Void) {
    completion(context.isPreview ? .placeholder : .load())
  }

  func getTimeline(in context: Context, completion: @escaping (Timeline<JarvisEntry>) -> Void) {
    let entry = JarvisEntry.load()
    // The app reloads the timeline whenever Home changes. Without it, look
    // again after the plan starts (so "in 5 min" becomes "Now"), or in 30 min.
    var next = Date().addingTimeInterval(30 * 60)
    if let start = entry.heroStartsAt, start > Date(), start < next {
      next = start
    }
    completion(Timeline(entries: [entry], policy: .after(next)))
  }
}

// MARK: - Look

private extension Color {
  static let jarvisInk = Color(red: 0.067, green: 0.067, blue: 0.086)
  static let jarvisAccent = Color(red: 0.357, green: 0.306, blue: 0.941)
  static let jarvisAccentLight = Color(red: 0.702, green: 0.671, blue: 1.0)
  static let jarvisNight = Color(red: 0.078, green: 0.102, blue: 0.2)
  static let jarvisWarning = Color(red: 0.85, green: 0.55, blue: 0.15)
}

private func label(for kind: String) -> String {
  switch kind {
  case "brief": return "MORNING BRIEF"
  case "battery": return "BEFORE YOU GO"
  case "wind_down": return "TOMORROW"
  case "soon": return "RIGHT NOW"
  default: return "TODAY"
  }
}

private func background(for kind: String) -> Color {
  switch kind {
  case "wind_down": return .jarvisNight
  case "battery": return .jarvisWarning.opacity(0.9)
  default: return .jarvisInk
  }
}

/// The widget background: `containerBackground` on iOS 17, a plain fill before.
private struct WidgetBackground: ViewModifier {
  let color: Color

  func body(content: Content) -> some View {
    if #available(iOSApplicationExtension 17.0, *) {
      content.containerBackground(color, for: .widget)
    } else {
      content.padding().background(color)
    }
  }
}

// MARK: - Right now

struct RightNowView: View {
  let entry: JarvisEntry

  var body: some View {
    VStack(alignment: .leading, spacing: 4) {
      Text(label(for: entry.heroKind))
        .font(.system(size: 10, weight: .semibold))
        .tracking(1)
        .foregroundStyle(Color.white.opacity(0.6))
      Spacer(minLength: 0)
      Text(entry.heroTitle.replacingOccurrences(of: "Tomorrow: ", with: ""))
        .font(.system(size: 18, weight: .bold))
        .foregroundStyle(.white)
        .lineLimit(3)
        .minimumScaleFactor(0.8)
      if let start = entry.heroStartsAt, entry.heroKind == "soon" {
        if start > entry.date {
          // Counts down by itself between timeline reloads.
          Text(start, style: .relative)
            .font(.system(size: 15, weight: .semibold).monospacedDigit())
            .foregroundStyle(Color.jarvisAccentLight)
        } else {
          Text("Now")
            .font(.system(size: 15, weight: .semibold))
            .foregroundStyle(Color.jarvisAccentLight)
        }
      } else if !entry.heroDetail.isEmpty {
        Text(entry.heroDetail)
          .font(.system(size: 12))
          .foregroundStyle(Color.white.opacity(0.75))
          .lineLimit(2)
      }
    }
    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
  }
}

// MARK: - Needs you

struct NeedsYouView: View {
  let entry: JarvisEntry

  var body: some View {
    HStack(alignment: .top, spacing: 14) {
      RightNowView(entry: entry)
        .frame(maxWidth: .infinity)
      VStack(alignment: .leading, spacing: 8) {
        HStack(spacing: 6) {
          Text("Needs you")
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(.white)
          if entry.needsCount > 0 {
            Text("\(entry.needsCount)")
              .font(.system(size: 11, weight: .bold))
              .foregroundStyle(.white)
              .padding(.horizontal, 6)
              .padding(.vertical, 1)
              .background(Capsule().fill(Color.jarvisAccent))
          }
        }
        if entry.needs.isEmpty {
          Text("You're all caught up")
            .font(.system(size: 12))
            .foregroundStyle(Color.white.opacity(0.7))
        }
        ForEach(entry.needs, id: \.self) { line in
          VStack(alignment: .leading, spacing: 1) {
            Text(line.title)
              .font(.system(size: 12, weight: .semibold))
              .foregroundStyle(.white)
              .lineLimit(1)
            Text(line.detail)
              .font(.system(size: 11))
              .foregroundStyle(
                line.kind == "reminder" ? Color.jarvisWarning : Color.white.opacity(0.65)
              )
              .lineLimit(1)
          }
        }
        Spacer(minLength: 0)
      }
      .frame(maxWidth: .infinity, alignment: .leading)
    }
  }
}

struct JarvisWidgetView: View {
  @Environment(\.widgetFamily) private var family
  let entry: JarvisEntry

  var body: some View {
    Group {
      switch family {
      case .systemMedium:
        NeedsYouView(entry: entry)
      default:
        RightNowView(entry: entry)
      }
    }
    .modifier(WidgetBackground(color: background(for: entry.heroKind)))
    .widgetURL(URL(string: "jarvis://home?homeWidget"))
  }
}

/// Right now (small) and Right now with Needs you (medium). The `kind`
/// matches `HomeWidgetSync.iOSName`, so the app can reload it.
struct JarvisWidget: Widget {
  let kind = "JarvisWidget"

  var body: some WidgetConfiguration {
    StaticConfiguration(kind: kind, provider: JarvisProvider()) { entry in
      JarvisWidgetView(entry: entry)
    }
    .configurationDisplayName("Jarvis")
    .description("What needs you right now.")
    .supportedFamilies([.systemSmall, .systemMedium])
  }
}

// MARK: - Ask

struct AskEntry: TimelineEntry {
  let date: Date
}

struct AskProvider: TimelineProvider {
  func placeholder(in context: Context) -> AskEntry { AskEntry(date: Date()) }

  func getSnapshot(in context: Context, completion: @escaping (AskEntry) -> Void) {
    completion(AskEntry(date: Date()))
  }

  func getTimeline(in context: Context, completion: @escaping (Timeline<AskEntry>) -> Void) {
    completion(Timeline(entries: [AskEntry(date: Date())], policy: .never))
  }
}

struct AskView: View {
  @Environment(\.widgetFamily) private var family

  var body: some View {
    VStack(alignment: .leading, spacing: 10) {
      Circle()
        .fill(
          LinearGradient(
            colors: [.jarvisAccent, Color(red: 0.75, green: 0.45, blue: 0.95)],
            startPoint: .topLeading,
            endPoint: .bottomTrailing
          )
        )
        .frame(width: 30, height: 30)
      Spacer(minLength: 0)
      Text("Ask Jarvis")
        .font(.system(size: 17, weight: .bold))
        .foregroundStyle(.white)
      if family == .systemMedium {
        HStack(spacing: 8) {
          // A small widget opens its one widgetURL wherever it is tapped; the
          // medium size gives Type and Talk their own targets.
          Link(destination: URL(string: "jarvis://ask?homeWidget")!) {
            Label("Type", systemImage: "text.bubble")
              .labelStyle(.iconOnly)
              .frame(width: 40, height: 32)
              .background(Capsule().fill(Color.white.opacity(0.14)))
          }
          Link(destination: URL(string: "jarvis://voice?homeWidget")!) {
            Label("Talk", systemImage: "waveform")
              .labelStyle(.iconOnly)
              .frame(width: 40, height: 32)
              .background(Capsule().fill(Color.white.opacity(0.14)))
          }
        }
        .foregroundStyle(.white)
      } else {
        Text("Tap to start a chat")
          .font(.system(size: 12))
          .foregroundStyle(Color.white.opacity(0.65))
      }
    }
    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    .modifier(WidgetBackground(color: .jarvisInk))
    .widgetURL(URL(string: "jarvis://ask?homeWidget"))
  }
}

/// A shortcut into a new chat or voice. It shows no data, so it never reloads.
struct JarvisAskWidget: Widget {
  let kind = "JarvisAskWidget"

  var body: some WidgetConfiguration {
    StaticConfiguration(kind: kind, provider: AskProvider()) { _ in
      AskView()
    }
    .configurationDisplayName("Ask Jarvis")
    .description("Type or talk to Jarvis.")
    .supportedFamilies([.systemSmall, .systemMedium])
  }
}

@main
struct JarvisWidgets: WidgetBundle {
  var body: some Widget {
    JarvisWidget()
    JarvisAskWidget()
  }
}
