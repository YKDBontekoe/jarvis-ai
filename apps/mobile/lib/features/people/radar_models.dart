import '../../json_maps.dart';

/// One thing the relationship radar noticed. [severity] is 1 for a note and 2 when it is worth a nudge.
class RadarSignalData {
  const RadarSignalData({
    required this.kind,
    required this.severity,
    required this.headline,
    required this.detail,
  });

  final String kind;
  final int severity;
  final String headline;
  final String detail;

  static RadarSignalData? fromJson(dynamic data) {
    final json = jsonObject(data);
    final headline = json == null ? null : jsonString(json, 'headline');
    if (json == null || headline == null) return null;
    return RadarSignalData(
      kind: jsonString(json, 'kind') ?? '',
      severity: asJsonInt(json['severity'], 1),
      headline: headline,
      detail: jsonString(json, 'detail') ?? '',
    );
  }
}

/// How one person and the owner keep in touch, worked out from message times only.
class RadarReportData {
  const RadarReportData({
    required this.personId,
    required this.name,
    required this.recentPerWeek,
    required this.baselinePerWeek,
    required this.weeklyMessages,
    required this.signals,
    this.lastMessageAt,
    this.myReplyMinutes,
    this.baselineReplyMinutes,
    this.myInitiationShare,
    this.unansweredInbound = 0,
    this.toneScore,
    this.toneReason,
  });

  final String personId;
  final String name;
  final DateTime? lastMessageAt;
  final double recentPerWeek;
  final double baselinePerWeek;
  final double? myReplyMinutes;
  final double? baselineReplyMinutes;
  final double? myInitiationShare;
  final int unansweredInbound;
  final List<int> weeklyMessages;
  final List<RadarSignalData> signals;
  final int? toneScore;
  final String? toneReason;

  int get severity => signals.isEmpty
      ? 0
      : signals.map((s) => s.severity).reduce((a, b) => a > b ? a : b);

  RadarSignalData? get topSignal => signals.isEmpty
      ? null
      : (signals.toList()..sort((a, b) => b.severity.compareTo(a.severity)))
            .first;

  bool get hasMessages => weeklyMessages.any((count) => count > 0);

  static double? _double(dynamic value) =>
      value is num ? value.toDouble() : null;

  static RadarReportData? fromJson(dynamic data) {
    final json = jsonObject(data);
    final id = json == null ? null : jsonString(json, 'personId');
    final name = json == null ? null : jsonString(json, 'name');
    if (json == null || id == null || name == null) return null;
    return RadarReportData(
      personId: id,
      name: name,
      lastMessageAt: DateTime.tryParse(
        jsonString(json, 'lastMessageAt') ?? '',
      )?.toLocal(),
      recentPerWeek: _double(json['recentPerWeek']) ?? 0,
      baselinePerWeek: _double(json['baselinePerWeek']) ?? 0,
      myReplyMinutes: _double(json['myReplyMinutes']),
      baselineReplyMinutes: _double(json['baselineReplyMinutes']),
      myInitiationShare: _double(json['myInitiationShare']),
      unansweredInbound: asJsonInt(json['unansweredInbound']),
      weeklyMessages: [
        for (final count in (json['weeklyMessages'] as List? ?? const []))
          asJsonInt(count),
      ],
      signals: [
        for (final signal in (json['signals'] as List? ?? const []))
          ?RadarSignalData.fromJson(signal),
      ],
      toneScore: json['toneScore'] is num ? asJsonInt(json['toneScore']) : null,
      toneReason: jsonString(json, 'toneReason'),
    );
  }
}

class RadarOverviewData {
  const RadarOverviewData({required this.toneEnabled, required this.people});

  final bool toneEnabled;
  final List<RadarReportData> people;

  static RadarOverviewData? fromJson(dynamic data) {
    final json = jsonObject(data);
    if (json == null) return null;
    return RadarOverviewData(
      toneEnabled: asJsonBool(json['toneEnabled']),
      people: [
        for (final item in (json['people'] as List? ?? const []))
          ?RadarReportData.fromJson(item),
      ],
    );
  }
}

/// A person and a chat that look like the same human, offered to link.
class LinkSuggestionData {
  const LinkSuggestionData({
    required this.personId,
    required this.personName,
    required this.connectionId,
    required this.chatId,
    required this.chatName,
  });

  final String personId;
  final String personName;
  final String connectionId;
  final String chatId;
  final String chatName;

  String get key => '$personId|$connectionId|$chatId';

  static List<LinkSuggestionData> listFromJson(dynamic data) => [
    for (final json in jsonMaps(data))
      if (jsonString(json, 'personId') case final personId?)
        if (jsonString(json, 'connectionId') case final connectionId?)
          if (jsonString(json, 'chatId') case final chatId?)
            LinkSuggestionData(
              personId: personId,
              personName: jsonString(json, 'personName') ?? 'Someone',
              connectionId: connectionId,
              chatId: chatId,
              chatName: jsonString(json, 'chatName') ?? chatId,
            ),
  ];
}

/// A one-to-one chat the owner can link to a person.
class LinkCandidateData {
  const LinkCandidateData({
    required this.connectionId,
    required this.chatId,
    required this.displayName,
    required this.readAlong,
  });

  final String connectionId;
  final String chatId;
  final String displayName;
  final bool readAlong;

  static List<LinkCandidateData> listFromJson(dynamic data) => [
    for (final json in jsonMaps(data))
      if (jsonString(json, 'connectionId') case final connectionId?)
        if (jsonString(json, 'chatId') case final chatId?)
          LinkCandidateData(
            connectionId: connectionId,
            chatId: chatId,
            displayName: jsonString(json, 'displayName') ?? chatId,
            readAlong: asJsonBool(json['readAlong']),
          ),
  ];
}

/// A chat linked to a person.
class PersonLinkData {
  const PersonLinkData({
    required this.id,
    required this.displayName,
    required this.readAlong,
  });

  final String id;
  final String displayName;
  final bool readAlong;

  static List<PersonLinkData> listFromJson(dynamic data) => [
    for (final json in jsonMaps(data))
      if (jsonString(json, 'id') case final id?)
        PersonLinkData(
          id: id,
          displayName:
              jsonString(json, 'displayName') ??
              jsonString(json, 'chatId') ??
              'Chat',
          readAlong: asJsonBool(json['readAlong']),
        ),
  ];
}

class PersonRadarData {
  const PersonRadarData({required this.links, this.report});

  final List<PersonLinkData> links;
  final RadarReportData? report;

  static PersonRadarData? fromJson(dynamic data) {
    final json = jsonObject(data);
    if (json == null) return null;
    return PersonRadarData(
      links: PersonLinkData.listFromJson(json['links']),
      report: RadarReportData.fromJson(json['report']),
    );
  }
}

/// "2 messages a week", "1 message a week", "0.4 messages a week".
String perWeekLabel(double rate) {
  final text = rate == rate.roundToDouble()
      ? rate.toStringAsFixed(0)
      : rate.toStringAsFixed(1);
  return '$text ${rate == 1 ? 'message' : 'messages'} a week';
}

/// "10 min", "3 h", "2 days".
String replyTimeLabel(double minutes) {
  if (minutes < 90) return '${minutes.round().clamp(1, 90)} min';
  if (minutes < 36 * 60) return '${(minutes / 60).round()} h';
  return '${(minutes / (24 * 60)).round()} days';
}

/// How the model judged recent messages: -2 tense to 2 warm.
String toneLabel(int score) => switch (score) {
  >= 2 => 'Warm',
  1 => 'Friendly',
  0 => 'Neutral',
  -1 => 'A little cool',
  _ => 'Tense',
};
