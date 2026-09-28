/// Display label for a voice id reported by the installed Codex CLI.
String voiceLabel(String? id) {
  final voice = id?.trim() ?? '';
  if (voice.isEmpty) return 'Codex';
  return '${voice[0].toUpperCase()}${voice.substring(1)}';
}
