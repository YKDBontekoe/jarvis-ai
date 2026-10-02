/// One link on the reading list, as returned by `/api/v1/reading`.
class ReadingItemData {
  const ReadingItemData({
    required this.id,
    required this.url,
    required this.status,
    required this.title,
    required this.keyPoints,
    required this.read,
    required this.createdAt,
    this.siteName,
    this.excerpt,
    this.summary,
    this.readingMinutes,
    this.note,
    this.failureReason,
  });

  final String id;
  final String url;

  /// `pending` while Jarvis fetches the page, then `ready` or `failed`.
  final String status;
  final String title;
  final String? siteName;
  final String? excerpt;
  final String? summary;
  final List<String> keyPoints;
  final int? readingMinutes;
  final String? note;
  final String? failureReason;
  final bool read;
  final DateTime createdAt;

  bool get pending => status == 'pending';
  bool get failed => status == 'failed';

  /// The summary when Jarvis wrote one, otherwise the page's own description.
  String? get blurb => summary ?? excerpt;

  /// The host without "www.", for when the page has no site name.
  String get host {
    final host = Uri.tryParse(url)?.host ?? '';
    return host.startsWith('www.') ? host.substring(4) : host;
  }

  String get source => siteName?.trim().isNotEmpty == true ? siteName! : host;

  ReadingItemData copyWith({bool? read}) => ReadingItemData(
    id: id,
    url: url,
    status: status,
    title: title,
    keyPoints: keyPoints,
    read: read ?? this.read,
    createdAt: createdAt,
    siteName: siteName,
    excerpt: excerpt,
    summary: summary,
    readingMinutes: readingMinutes,
    note: note,
    failureReason: failureReason,
  );

  static ReadingItemData? fromJson(dynamic json) {
    if (json is! Map) return null;
    final id = json['id'];
    final url = json['url'];
    if (id is! String || url is! String) return null;
    String? text(String key) {
      final value = json[key];
      return value is String && value.trim().isNotEmpty ? value : null;
    }

    return ReadingItemData(
      id: id,
      url: url,
      status: text('status') ?? 'pending',
      title: _readableTitle(text('title'), url),
      siteName: text('siteName'),
      excerpt: text('excerpt'),
      summary: text('summary'),
      keyPoints: [
        for (final point in (json['keyPoints'] as List?) ?? const [])
          if (point is String && point.trim().isNotEmpty) point,
      ],
      readingMinutes: (json['readingMinutes'] as num?)?.toInt(),
      note: text('note'),
      failureReason: text('failureReason'),
      read: json['read'] == true,
      createdAt:
          DateTime.tryParse(text('createdAt') ?? '')?.toLocal() ??
          DateTime.now(),
    );
  }

  static List<ReadingItemData> listFromJson(dynamic json) => [
    if (json is List)
      for (final item in json) ?ReadingItemData.fromJson(item),
  ];
}

// Before the page is read the API title is the link itself; drop the scheme
// and "www." so it reads like a name.
String _readableTitle(String? title, String url) {
  if (title != null && title != url) return title;
  return url
      .replaceFirst(RegExp(r'^https?://(www\.)?'), '')
      .replaceFirst(RegExp(r'/$'), '');
}

/// "5 min read", or null while the reading time is unknown.
String? readingTimeLabel(int? minutes) =>
    minutes == null ? null : '$minutes min read';

/// "3 unread · about 25 min" for the header of the unread tab.
String readingTotalsLabel(List<ReadingItemData> unread) {
  if (unread.isEmpty) return 'All caught up';
  final minutes = unread.fold<int>(
    0,
    (sum, item) => sum + (item.readingMinutes ?? 0),
  );
  final count = '${unread.length} unread';
  if (minutes == 0) return count;
  final time = minutes >= 90
      ? 'about ${(minutes / 60).toStringAsFixed(minutes % 60 == 0 ? 0 : 1)} h'
      : 'about $minutes min';
  return '$count · $time';
}

/// The chat prompt behind "Summarize with Jarvis".
const readingSummaryPrompt =
    'Summarize my reading list: what is on it, grouped by theme, and what '
    'should I read first?';

/// Finds a link in pasted text, so "Look at this https://…" still works.
String? extractLink(String? text) {
  if (text == null) return null;
  final match = RegExp(
    r'''(?:https?://|www\.)[^\s<>"']+|\b(?:[a-z0-9-]+\.)+[a-z]{2,}(?:/[^\s<>"']*)?''',
    caseSensitive: false,
  ).firstMatch(text.trim());
  return match?.group(0)?.replaceFirst(RegExp(r'''[.,)\]!?;:"'>]+$'''), '');
}
