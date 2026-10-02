import 'package:flutter/widgets.dart';

import '../../json_maps.dart';

/// A chat on the owner's linked WhatsApp, with their read-along choices.
class WhatsAppChat {
  const WhatsAppChat({
    required this.chatId,
    required this.name,
    required this.isGroup,
    required this.readAlong,
    required this.autoReminders,
    this.lastMessageAt,
    this.preview,
    this.previewFromMe,
    this.unreadCount = 0,
  });

  final String chatId;
  final String name;
  final bool isGroup;
  final bool readAlong;
  final bool autoReminders;
  final DateTime? lastMessageAt;
  final String? preview;
  final bool? previewFromMe;
  final int unreadCount;

  static WhatsAppChat? fromJson(Map<String, dynamic>? json) {
    final chatId = asJsonString(json?['chatId']);
    if (json == null || chatId == null) return null;
    return WhatsAppChat(
      chatId: chatId,
      name: asJsonString(json['name']) ?? chatId,
      isGroup: asJsonBool(json['isGroup']),
      readAlong: asJsonBool(json['readAlong']),
      autoReminders: asJsonBool(json['autoReminders'], true),
      lastMessageAt: jsonDate(json['lastMessageAt'], local: true),
      preview: asJsonString(json['preview']),
      previewFromMe: json['previewFromMe'] is bool
          ? json['previewFromMe'] as bool
          : null,
      unreadCount: (json['unreadCount'] as num?)?.toInt() ?? 0,
    );
  }

  WhatsAppChat copyWith({bool? readAlong, bool? autoReminders}) => WhatsAppChat(
    chatId: chatId,
    name: name,
    isGroup: isGroup,
    readAlong: readAlong ?? this.readAlong,
    autoReminders: autoReminders ?? this.autoReminders,
    lastMessageAt: lastMessageAt,
    preview: preview,
    previewFromMe: previewFromMe,
    unreadCount: unreadCount,
  );

  /// The phone number for one-to-one chats, shown under a contact name.
  String? get phone => chatId.startsWith('+') && name != chatId ? chatId : null;

  /// One or two letters for the avatar.
  String get initials {
    final words = name
        .replaceAll(RegExp(r'[^\p{L}\p{N} ]', unicode: true), ' ')
        .split(' ')
        .where((word) => word.isNotEmpty)
        .toList();
    if (words.isEmpty) return '#';
    if (RegExp(r'^\d').hasMatch(words.first)) return '#';
    final first = words.first.characters.first;
    final second = words.length > 1 ? words.last.characters.first : '';
    return (first + second).toUpperCase();
  }
}

/// A stored message in a read-along chat.
class WhatsAppMessage {
  const WhatsAppMessage({
    required this.id,
    required this.fromMe,
    required this.text,
    required this.sentAt,
    this.sender,
    this.receivedAt,
  });

  final String id;
  final bool fromMe;
  final String? sender;
  final String text;
  final DateTime sentAt;
  final DateTime? receivedAt;

  static WhatsAppMessage? fromJson(Map<String, dynamic>? json) {
    final id = asJsonString(json?['id']);
    final sentAt = jsonDate(json?['sentAt'], local: true);
    if (json == null || id == null || sentAt == null) return null;
    return WhatsAppMessage(
      id: id,
      fromMe: asJsonBool(json['fromMe']),
      sender: asJsonString(json['sender']),
      text: asJsonString(json['text']) ?? '',
      sentAt: sentAt,
      receivedAt: jsonDate(json['receivedAt']),
    );
  }
}

String whatsAppConnectionLabel(String state) => switch (state) {
  'open' => 'Connected',
  'connecting' => 'Reconnecting…',
  'paused' => 'Paused',
  'none' || 'logged_out' || 'qr' => 'Link your account again',
  'unavailable' => 'WhatsApp is unavailable on this server',
  _ => 'Connection unavailable',
};

String whatsAppConnectionMessage(String state) => switch (state) {
  'connecting' =>
    'WhatsApp is reconnecting. Saved messages are still available.',
  'paused' => 'This account is paused. New messages are not being collected.',
  'none' || 'logged_out' || 'qr' =>
    'This account needs to be linked again. Open account settings to reconnect.',
  _ =>
    'WhatsApp could not be reached. Showing saved messages; Jarvis will retry automatically.',
};

String whatsAppChatPath(String channelId, String chatId) =>
    '/api/v1/channels/$channelId/chats/${Uri.encodeComponent(chatId)}';

/// "14:05", "Yesterday", "Mon" or "12 Sep", the way chat lists show time.
String whatsAppListTime(DateTime? time, {DateTime? now}) {
  if (time == null) return '';
  final today = now ?? DateTime.now();
  final day = DateTime(time.year, time.month, time.day);
  final days = DateTime(
    today.year,
    today.month,
    today.day,
  ).difference(day).inDays;
  if (days <= 0) return _hm(time);
  if (days == 1) return 'Yesterday';
  if (days < 7) {
    return const [
      'Mon',
      'Tue',
      'Wed',
      'Thu',
      'Fri',
      'Sat',
      'Sun',
    ][time.weekday - 1];
  }
  return '${time.day} ${_months[time.month - 1]}';
}

/// "Today", "Yesterday", or "Monday 12 September" for date separators.
String whatsAppDayLabel(DateTime time, {DateTime? now}) {
  final today = now ?? DateTime.now();
  final days = DateTime(
    today.year,
    today.month,
    today.day,
  ).difference(DateTime(time.year, time.month, time.day)).inDays;
  if (days == 0) return 'Today';
  if (days == 1) return 'Yesterday';
  const weekdays = [
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
  ];
  const months = [
    'January',
    'February',
    'March',
    'April',
    'May',
    'June',
    'July',
    'August',
    'September',
    'October',
    'November',
    'December',
  ];
  final year = time.year == today.year ? '' : ' ${time.year}';
  return '${weekdays[time.weekday - 1]} ${time.day} ${months[time.month - 1]}$year';
}

String whatsAppClock(DateTime time) => _hm(time);

String _hm(DateTime time) =>
    '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}';

const _months = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];
