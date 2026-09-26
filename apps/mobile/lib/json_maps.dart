List<Map<String, dynamic>> jsonMaps(dynamic data) {
  if (data is! List) return const [];
  final maps = <Map<String, dynamic>>[];
  for (final item in data) {
    if (item is Map) maps.add(Map<String, dynamic>.from(item));
  }
  return maps;
}
