/// Flattens Markdown into a single readable line for previews (cards, lists,
/// notifications) where rendering full Markdown would be too heavy.
String plainPreview(String markdown, {int maxLength = 200}) {
  var text = markdown
      .replaceAll('\r', '')
      // [label](url) -> label
      .replaceAllMapped(RegExp(r'\[([^\]]+)\]\([^)]*\)'), (m) => m[1]!)
      // headings, blockquotes, list markers at line starts
      .replaceAll(RegExp(r'^\s{0,3}(#{1,6}|>|[-*+]|\d+[.)])\s+', multiLine: true), '')
      // fenced code markers and inline emphasis / code ticks
      .replaceAll(RegExp(r'```\w*'), '')
      .replaceAll(RegExp(r'(\*\*|__|\*|`)'), '');
  text = text.split('\n').map((line) => line.trim()).where((line) => line.isNotEmpty).join(' · ');
  text = text.replaceAll(RegExp(r'\s+'), ' ').trim();
  if (text.length <= maxLength) return text;
  return '${text.substring(0, maxLength - 1).trimRight()}…';
}

/// The one-line subject of a coding task: its "Change:" line when it has one
/// (self-fix prompts do), otherwise the first non-empty line.
String codingTaskTitle(String task, {int maxLength = 90}) {
  final lines = task
      .split('\n')
      .map((line) => line.trim())
      .where((line) => line.isNotEmpty)
      .toList();
  final change = lines.where((line) => line.toLowerCase().startsWith('change:'));
  final title = change.isNotEmpty
      ? change.first.substring(7).trim()
      : (lines.isEmpty ? 'Coding task' : lines.first);
  return title.length <= maxLength
      ? title
      : '${title.substring(0, maxLength - 1).trimRight()}…';
}
