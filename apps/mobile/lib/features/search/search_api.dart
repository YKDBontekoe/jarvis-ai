import 'package:dio/dio.dart';

import '../../json_maps.dart';
import 'search_models.dart';

Future<({List<FederatedSearchHit> results, List<SearchProviderStatus> providers})> federatedSearch(
  Dio http,
  String query, {
  Set<String>? kinds,
}) async {
  final trimmed = query.trim();
  if (trimmed.isEmpty) {
    return (results: <FederatedSearchHit>[], providers: <SearchProviderStatus>[]);
  }
  final response = await http.get<Object?>(
    '/api/v1/search',
    queryParameters: {
      'query': trimmed,
      if (kinds != null && kinds.isNotEmpty) 'kinds': kinds.join(','),
    },
  );
  final body = jsonObject(response.data);
  final results = <FederatedSearchHit>[];
  final rawResults = body?['results'];
  if (rawResults is List) {
    for (final item in rawResults) {
      final map = jsonObject(item);
      if (map == null) continue;
      final hit = FederatedSearchHit.fromJson(map);
      if (hit != null) results.add(hit);
    }
  }
  final providers = <SearchProviderStatus>[];
  final rawProviders = body?['providers'];
  if (rawProviders is List) {
    for (final item in rawProviders) {
      final map = jsonObject(item);
      if (map == null) continue;
      providers.add(
        SearchProviderStatus(
          providerId: asJsonString(map['providerId']) ?? '',
          succeeded: map['succeeded'] == true,
          error: asJsonString(map['error']),
        ),
      );
    }
  }
  return (results: results, providers: providers);
}

class SearchProviderStatus {
  const SearchProviderStatus({
    required this.providerId,
    required this.succeeded,
    this.error,
  });

  final String providerId;
  final bool succeeded;
  final String? error;
}
