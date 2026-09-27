List<Map<String, dynamic>> jsonMaps(dynamic data) {
  if (data is! List) return const [];
  final maps = <Map<String, dynamic>>[];
  for (final item in data) {
    if (item is Map) maps.add(Map<String, dynamic>.from(item));
  }
  return maps;
}

List<String> jsonStrings(dynamic data) {
  if (data is! List) return const [];
  return [for (final item in data) if (item is String) item];
}

String? jsonString(Map<dynamic, dynamic> map, String key) {
  final value = map[key];
  return value is String && value.isNotEmpty ? value : null;
}

String? asJsonString(dynamic value) => value is String ? value : null;

int asJsonInt(dynamic value, [int fallback = 0]) => switch (value) {
  int number => number,
  num number => number.toInt(),
  _ => fallback,
};

bool asJsonBool(dynamic value, [bool fallback = false]) =>
    value is bool flag ? flag : fallback;

/// Keeps a future TTL on pin/edit; expired or missing validity is sent as null.
String? activeValidUntil(Map<String, dynamic> memory, [DateTime? now]) {
  final raw = asJsonString(memory['validUntil']);
  final parsed = DateTime.tryParse(raw ?? '');
  if (parsed == null || !parsed.isAfter(now ?? DateTime.now())) return null;
  return raw;
}

String? firstProblemMessage(dynamic data) {
  if (data is! Map) return null;
  final map = Map<String, dynamic>.from(data);
  final detail = asJsonString(map['detail']);
  if (detail != null && detail.isNotEmpty) return detail;
  final errors = map['errors'];
  if (errors is Map) {
    for (final value in errors.values) {
      if (value is List && value.isNotEmpty && value.first is String) {
        final message = value.first as String;
        if (message.isNotEmpty) return message;
      }
    }
  }
  return asJsonString(map['title']);
}
