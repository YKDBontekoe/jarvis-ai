import 'dart:convert';

import 'package:flutter/widgets.dart';

import '../../json_maps.dart';

/// Something in a chat the owner should answer.
class WhatsAppReplyItem {
  const WhatsAppReplyItem({required this.who, required this.about});

  final String who;
  final String about;

  static WhatsAppReplyItem? fromJson(Map<String, dynamic>? json) {
    final about = asJsonString(json?['about']);
    if (json == null || about == null) return null;
    return WhatsAppReplyItem(
      who: asJsonString(json['who']) ?? '',
      about: about,
    );
  }
}

/// A short summary of a chat's unread messages and what to reply to, written
/// by Jarvis in the background. It goes away once the chat is read.
class WhatsAppCatchUp {
  const WhatsAppCatchUp({
    required this.summary,
    this.toReply = const [],
    this.messageCount = 0,
  });

  final String summary;
  final List<WhatsAppReplyItem> toReply;
  final int messageCount;

  static WhatsAppCatchUp? fromJson(Map<String, dynamic>? json) {
    final summary = asJsonString(json?['summary']);
    if (json == null || summary == null) return null;
    return WhatsAppCatchUp(
      summary: summary,
      toReply: [
        for (final item in jsonMaps(json['toReply']))
          ?WhatsAppReplyItem.fromJson(item),
      ],
      messageCount: (json['messageCount'] as num?)?.toInt() ?? 0,
    );
  }
}

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
    this.catchUp,
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
  final WhatsAppCatchUp? catchUp;

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
      catchUp: WhatsAppCatchUp.fromJson(jsonObject(json['catchUp'])),
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
    catchUp: catchUp,
  );

  /// The phone number for one-to-one chats, shown under a contact name.
  String? get phone => chatId.startsWith('+') && name != chatId ? chatId : null;

  /// One or two letters for the avatar.
  String get initials => whatsAppInitials(name);
}

/// One or two letters for an avatar when a picture is missing.
String whatsAppInitials(String name) {
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

/// A stored message in a read-along chat.
class WhatsAppMessage {
  const WhatsAppMessage({
    required this.id,
    required this.fromMe,
    required this.text,
    required this.sentAt,
    this.sender,
    this.senderId,
    this.receivedAt,
    this.media,
    this.quote,
  });

  final String id;
  final bool fromMe;
  final String? sender;

  /// Phone number or @lid of a group participant, used to load their picture.
  final String? senderId;
  final String text;
  final DateTime sentAt;
  final DateTime? receivedAt;
  final WhatsAppMedia? media;
  final WhatsAppQuote? quote;

  static WhatsAppMessage? fromJson(Map<String, dynamic>? json) {
    final id = asJsonString(json?['id']);
    final sentAt = jsonDate(json?['sentAt'], local: true);
    if (json == null || id == null || sentAt == null) return null;
    return WhatsAppMessage(
      id: id,
      fromMe: asJsonBool(json['fromMe']),
      sender: asJsonString(json['sender']),
      senderId: asJsonString(json['senderId']),
      text: asJsonString(json['text']) ?? '',
      sentAt: sentAt,
      receivedAt: jsonDate(json['receivedAt']),
      media: WhatsAppMedia.fromJson(json['media']),
      quote: WhatsAppQuote.fromJson(json['quote']),
    );
  }
}

/// A photo, sticker, video, voice note, document, location, contact or poll.
class WhatsAppMedia {
  const WhatsAppMedia({
    required this.kind,
    this.mime,
    this.fileName,
    this.seconds,
    this.width,
    this.height,
    this.animated = false,
    this.voice = false,
    this.latitude,
    this.longitude,
    this.place,
    this.contactName,
    this.pollOptions = const [],
    this.hasContent = false,
  });

  final String kind;
  final String? mime;
  final String? fileName;
  final int? seconds;
  final int? width;
  final int? height;
  final bool animated;
  final bool voice;
  final double? latitude;
  final double? longitude;
  final String? place;
  final String? contactName;
  final List<String> pollOptions;
  final bool hasContent;

  static WhatsAppMedia? fromJson(Object? value) {
    final json = jsonObject(value);
    final kind = asJsonString(json?['kind']);
    if (json == null || kind == null || !whatsAppMediaKinds.contains(kind)) {
      return null;
    }
    return WhatsAppMedia(
      kind: kind,
      mime: asJsonString(json['mime']),
      fileName: asJsonString(json['fileName']),
      seconds: (json['seconds'] as num?)?.toInt(),
      width: (json['width'] as num?)?.toInt(),
      height: (json['height'] as num?)?.toInt(),
      animated: asJsonBool(json['animated']),
      voice: asJsonBool(json['voice']),
      latitude: (json['latitude'] as num?)?.toDouble(),
      longitude: (json['longitude'] as num?)?.toDouble(),
      place: asJsonString(json['place']),
      contactName: asJsonString(json['contactName']),
      pollOptions: [
        for (final option
            in json['pollOptions'] is List
                ? json['pollOptions'] as List
                : const [])
          if (option is String && option.trim().isNotEmpty) option.trim(),
      ],
      hasContent: asJsonBool(json['hasContent']),
    );
  }

  /// Short label such as "Photo" or "Voice message".
  String get label => whatsAppMediaLabel(kind, voice: voice);
}

/// The message this one replies to.
class WhatsAppQuote {
  const WhatsAppQuote({this.author, required this.text});

  final String? author;
  final String text;

  static WhatsAppQuote? fromJson(Object? value) {
    final json = jsonObject(value);
    final text = asJsonString(json?['text'])?.trim();
    if (json == null || text == null || text.isEmpty) return null;
    return WhatsAppQuote(author: asJsonString(json['author']), text: text);
  }
}

const whatsAppMediaKinds = {
  'image',
  'sticker',
  'video',
  'gif',
  'audio',
  'document',
  'location',
  'contact',
  'poll',
};

String whatsAppMediaLabel(String kind, {bool voice = false}) => switch (kind) {
  'image' => 'Photo',
  'sticker' => 'Sticker',
  'video' => 'Video',
  'gif' => 'GIF',
  'audio' => voice ? 'Voice message' : 'Audio',
  'document' => 'Document',
  'location' => 'Location',
  'contact' => 'Contact',
  'poll' => 'Poll',
  _ => 'Attachment',
};

final _legacyMedia = RegExp(
  r'^\[(Photo|Video|GIF|Voice message|Audio|Document|Sticker|Location|Live location|Contact card|Contact cards|Poll)\](?:\s+([\s\S]*))?$',
);

/// A message saved before files were downloaded, recognised from its "[Photo] caption" text.
WhatsAppMedia? whatsAppLegacyMedia(String text) {
  final match = _legacyMedia.firstMatch(text.trim());
  if (match == null) return null;
  final label = match.group(1)!;
  final extra = match.group(2)?.trim();
  final caption = extra == null || extra.isEmpty ? null : extra;
  final kind = switch (label) {
    'Photo' => 'image',
    'Sticker' => 'sticker',
    'Video' => 'video',
    'GIF' => 'gif',
    'Voice message' || 'Audio' => 'audio',
    'Document' => 'document',
    'Location' || 'Live location' => 'location',
    'Contact card' || 'Contact cards' => 'contact',
    'Poll' => 'poll',
    _ => null,
  };
  if (kind == null) return null;
  return WhatsAppMedia(
    kind: kind,
    voice: label == 'Voice message',
    fileName: kind == 'document' ? caption : null,
    place: kind == 'location'
        ? caption
        : label == 'Live location'
        ? caption
        : null,
    contactName: kind == 'contact' ? caption : null,
  );
}

/// Caption after the "[Photo]" label, or null when the text is only the label.
String? whatsAppMediaCaption(String text) {
  final match = _legacyMedia.firstMatch(text.trim());
  if (match == null) {
    final trimmed = text.trim();
    return trimmed.isEmpty ? null : trimmed;
  }
  final extra = match.group(2)?.trim();
  return extra == null || extra.isEmpty ? null : extra;
}

/// "Photo · fiezbiez" for a chat-list preview that was stored as "[Photo] fiezbiez".
String whatsAppPreviewText(String text) {
  final flat = text.replaceAll(RegExp(r'\s+'), ' ').trim();
  final match = _legacyMedia.firstMatch(flat);
  if (match == null) return flat;
  final label = switch (match.group(1)!) {
    'Contact card' => 'Contact',
    'Contact cards' => 'Contacts',
    final other => other,
  };
  final extra = match.group(2)?.trim();
  return extra == null || extra.isEmpty ? label : '$label · $extra';
}

/// One grapheme that is only emoji, joiners, and presentation selectors.
final _oneEmoji = RegExp(
  r'^(?:'
  r'\u200d|\ufe0f|\u20e3|'
  r'[\u{1f3fb}-\u{1f3ff}]|'
  r'[\u{1f1e6}-\u{1f1ff}]|'
  r'[\u{1f000}-\u{1faff}]|'
  r'[\u{2600}-\u{27bf}]|'
  r'[\u{2300}-\u{23ff}]|'
  r'[\u{2b00}-\u{2bff}]'
  r')+$',
  unicode: true,
);

/// One to four emoji and nothing else, drawn large the way a sticker-sized reaction reads.
bool whatsAppEmojiOnly(String text) {
  final trimmed = text.trim();
  if (trimmed.isEmpty) return false;
  final glyphs = trimmed.characters;
  if (glyphs.isEmpty || glyphs.length > 4) return false;
  for (final glyph in glyphs) {
    if (!_oneEmoji.hasMatch(glyph)) return false;
  }
  return true;
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

/// Path of one chat action. The chat id is passed separately as the `chatId`
/// query value: group ids contain `@`, which proxies drop from the path and,
/// on some fronts, from the query string too.
String whatsAppChatPath(String channelId, {String? action}) {
  final tail = action == null ? 'open' : 'open/$action';
  return '/api/v1/channels/$channelId/chats/$tail';
}

/// Phone numbers stay readable. Anything with `@` (a group or an unresolved
/// lid) is `b64.` plus base64url, so the request URL never contains `@`.
String whatsAppChatToken(String chatId) {
  if (!chatId.contains('@') && !chatId.contains('%')) return chatId;
  final encoded = base64Url.encode(utf8.encode(chatId)).replaceAll('=', '');
  return 'b64.$encoded';
}

/// Reverses [whatsAppChatToken]. Values that are not tokens are returned as they are.
String whatsAppChatIdFromToken(String token) {
  if (!token.startsWith('b64.') || token.length < 8) return token;
  try {
    final encoded = token.substring(4);
    final pad = '=' * ((4 - encoded.length % 4) % 4);
    return utf8.decode(base64Url.decode('$encoded$pad'));
  } on FormatException {
    return token;
  }
}

Map<String, dynamic> whatsAppChatQuery(
  String chatId, [
  Map<String, dynamic>? extra,
]) => {'chatId': whatsAppChatToken(chatId), ...?extra};

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
