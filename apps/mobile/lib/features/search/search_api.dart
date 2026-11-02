import 'package:dio/dio.dart';

import '../../json_maps.dart';
import 'search_models.dart';

Future<
  ({List<FederatedSearchHit> results, List<SearchProviderStatus> providers})
>
federatedSearch(
  Dio http,
  String query, {
  Set<String>? kinds,
  DateTime? from,

  /// Searches only inside this conversation, its whole transcript, newest first.
  String? conversationId,
  CancelToken? cancelToken,
}) async {
  final trimmed = query.trim();
  if (trimmed.isEmpty) {
    return (
      results: <FederatedSearchHit>[],
      providers: <SearchProviderStatus>[],
    );
  }
  final response = await http.get<Object?>(
    '/api/v1/search',
    cancelToken: cancelToken,
    queryParameters: {
      'query': trimmed,
      'conversationId': ?conversationId,
      if (kinds != null && kinds.isNotEmpty) 'kinds': kinds.join(','),
      if (from != null)
        'from':
            '${from.year.toString().padLeft(4, '0')}-${from.month.toString().padLeft(2, '0')}-${from.day.toString().padLeft(2, '0')}',
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

/// Plain-language line for sources that did not answer; results from the rest stay visible.
String incompleteSearchMessage(List<SearchProviderStatus> failed) {
  final names = failed
      .map((p) => searchProviderLabel(p.providerId))
      .toSet()
      .toList();
  final which = names.length <= 2
      ? names.join(' and ')
      : '${names.take(2).join(', ')} and ${names.length - 2} more';
  return 'Search is incomplete: $which did not respond. '
      'Results below may be missing items.';
}

/// Friendly label for a provider id such as `memory` or `graph_entity`.
String searchProviderLabel(String id) {
  if (id.isEmpty) return 'One source';
  final label = searchKindLabels[id];
  if (label != null) return label;
  final spaced = id.replaceAll(RegExp(r'[_\-.]+'), ' ').trim();
  return spaced.isEmpty
      ? 'One source'
      : spaced[0].toUpperCase() + spaced.substring(1);
}
