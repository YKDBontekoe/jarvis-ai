import '../../json_maps.dart';

/// Turns a conversation into Markdown for pasting elsewhere: a title, then
/// each message with who wrote it and when. Rows that are not user or
/// assistant messages, or have no text, are left out.
String conversationMarkdown({
  required String title,
  required Iterable<Map<String, dynamic>> messages,
}) {
  final buffer = StringBuffer(
    '# ${title.trim().isEmpty ? 'Conversation' : title.trim()}\n',
  );
  for (final message in messages) {
    final role = asJsonString(message['role']);
    final content = asJsonString(message['content'])?.trim();
    if (content == null || content.isEmpty) continue;
    final author = switch (role) {
      'user' => 'You',
      'assistant' => 'Jarvis',
      _ => null,
    };
    if (author == null) continue;
    final sent = jsonDate(message['createdAt'], local: true);
    buffer
      ..writeln()
      ..writeln(sent == null ? '**$author**' : '**$author** · ${_stamp(sent)}')
      ..writeln()
      ..writeln(content);
  }
  return buffer.toString();
}

String _stamp(DateTime date) {
  String two(int value) => value.toString().padLeft(2, '0');
  return '${date.year}-${two(date.month)}-${two(date.day)} '
      '${two(date.hour)}:${two(date.minute)}';
}
