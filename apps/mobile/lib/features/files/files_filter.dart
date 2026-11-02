import '../../json_maps.dart';
import '../everything/feature_search.dart' show foldForSearch;

/// What a file is, for the type filter.
enum FileKind {
  pdf('PDF'),
  image('Images'),
  text('Text'),
  data('Data');

  const FileKind(this.label);

  final String label;
}

FileKind kindOfFile(String fileName) =>
    switch (fileName.split('.').last.toLowerCase()) {
      'pdf' => FileKind.pdf,
      'jpg' || 'jpeg' || 'png' || 'webp' => FileKind.image,
      'csv' || 'json' => FileKind.data,
      _ => FileKind.text,
    };

/// Where a file is in being read by Jarvis, for the status filter.
enum FileStage {
  ready('Ready'),
  processing('Processing'),
  failed('Failed');

  const FileStage(this.label);

  final String label;
}

FileStage stageOfFile(Map<String, dynamic> file) =>
    switch (asJsonString(file['processingStatus'])) {
      'indexed' || 'ready' => FileStage.ready,
      'failed' => FileStage.failed,
      _ => FileStage.processing,
    };

/// Collection names by file id, so each row can say which collections hold it.
Map<String, List<String>> collectionNamesByFile(
  List<Map<String, dynamic>> collections,
) {
  final result = <String, List<String>>{};
  for (final collection in collections) {
    final name = asJsonString(collection['name']);
    final ids = collection['fileIds'];
    if (name == null || ids is! List) continue;
    for (final id in ids) {
      if (id is String) result.putIfAbsent(id, () => []).add(name);
    }
  }
  return result;
}

/// Files whose name contains [query] and that match the chosen types and stage,
/// in their original order. Empty selections mean "any".
List<Map<String, dynamic>> filterFiles(
  List<Map<String, dynamic>> files, {
  String query = '',
  Set<FileKind> kinds = const {},
  FileStage? stage,
}) {
  final needle = foldForSearch(query);
  return [
    for (final file in files)
      if ((needle.isEmpty ||
              foldForSearch(asJsonString(file['fileName']) ?? '')
                  .contains(needle)) &&
          (kinds.isEmpty ||
              kinds.contains(
                kindOfFile(asJsonString(file['fileName']) ?? ''),
              )) &&
          (stage == null || stageOfFile(file) == stage))
        file,
  ];
}
