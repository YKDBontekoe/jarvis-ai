List<Map<String, dynamic>> jsonMaps(dynamic data) {
  if (data is! List) return const [];
  final maps = <Map<String, dynamic>>[];
  for (final item in data) {
    final map = jsonObject(item);
    if (map != null) maps.add(map);
  }
  return maps;
}

/// Copies a JSON object. Wrong types, including `null`, become `null`.
Map<String, dynamic>? jsonObject(dynamic data) {
  if (data is! Map) return null;
  if (data is Map<String, dynamic>) return Map<String, dynamic>.from(data);
  final map = <String, dynamic>{};
  data.forEach((key, value) {
    if (key is String) map[key] = value;
  });
  return map;
}

List<String> jsonStrings(dynamic data) {
  if (data is! List) return const [];
  return [
    for (final item in data)
      if (item is String) item,
  ];
}

String? jsonString(Map<dynamic, dynamic> map, String key) {
  final value = map[key];
  return value is String && value.isNotEmpty ? value : null;
}

/// Non-empty string `id` from a JSON object. Wrong types and blanks are null.
String? jsonId(Map<dynamic, dynamic>? map) =>
    map == null ? null : jsonString(map, 'id');

String? asJsonString(dynamic value) => value is String ? value : null;

int asJsonInt(dynamic value, [int fallback = 0]) => switch (value) {
  int number => number,
  num number => number.toInt(),
  _ => fallback,
};

bool asJsonBool(dynamic value, [bool fallback = false]) =>
    value is bool ? value : fallback;

/// Keeps a future TTL on pin/edit; expired or missing validity is sent as null.
String? activeValidUntil(Map<String, dynamic> memory, [DateTime? now]) {
  final raw = asJsonString(memory['validUntil']);
  final parsed = DateTime.tryParse(raw ?? '');
  if (parsed == null || !parsed.isAfter(now ?? DateTime.now())) return null;
  return raw;
}

String? firstProblemMessage(dynamic data) {
  final map = jsonObject(data);
  if (map == null) return null;
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
  final message = asJsonString(map['message']);
  if (message != null && message.isNotEmpty) return message;
  return asJsonString(map['title']);
}
