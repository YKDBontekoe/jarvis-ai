import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/chat/composer_drafts.dart';
import 'package:jarvis_mobile/features/chat/share_text.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  group('ComposerDrafts', () {
    setUp(() => SharedPreferences.setMockInitialValues({}));

    test('keeps a draft per conversation across reopening', () async {
      final drafts = await ComposerDrafts.open();
      drafts.write('a', 'Remind me to call');
      drafts.write('b', 'Plan the trip');
      await drafts.flush();

      final reopened = await ComposerDrafts.open();
      expect(reopened.read('a'), 'Remind me to call');
      expect(reopened.read('b'), 'Plan the trip');
      expect(reopened.read('c'), '');
    });

    test('blank text and remove forget the draft', () async {
      final drafts = await ComposerDrafts.open();
      drafts.write('a', 'Hello');
      drafts.write('a', '   ');
      drafts.write('b', 'Bye');
      drafts.remove('b');
      await drafts.flush();

      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString(ComposerDrafts.storageKey), isNull);
    });

    test('photos are kept next to the text and survive reopening', () async {
      final drafts = await ComposerDrafts.open();
      drafts.write('a', 'What is this?');
      drafts.writePhotos('a', [(fileId: 'f1', fileName: 'leaf.png')]);
      // Typing again keeps the photos.
      drafts.write('a', 'What is this plant?');
      await drafts.flush();

      final reopened = await ComposerDrafts.open();
      expect(reopened.read('a'), 'What is this plant?');
      expect(reopened.readPhotos('a'), [(fileId: 'f1', fileName: 'leaf.png')]);
    });

    test('a photo alone is a draft, and clearing both forgets it', () async {
      final drafts = await ComposerDrafts.open();
      drafts.writePhotos('a', [(fileId: 'f1', fileName: 'leaf.png')]);
      await drafts.flush();
      expect((await ComposerDrafts.open()).readPhotos('a'), hasLength(1));

      drafts.writePhotos('a', const []);
      await drafts.flush();
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString(ComposerDrafts.storageKey), isNull);
    });

    test('drafts saved by older versions as plain text still load', () async {
      SharedPreferences.setMockInitialValues({
        ComposerDrafts.storageKey: jsonEncode({'a': 'Old draft'}),
      });
      final drafts = await ComposerDrafts.open();
      expect(drafts.read('a'), 'Old draft');
      expect(drafts.readPhotos('a'), isEmpty);
    });

    test(
      'previews summarise text and photo-only drafts for the Chats list',
      () async {
        final drafts = await ComposerDrafts.open();
        drafts.write('a', '  Book the hotel  ');
        drafts.writePhotos('b', [(fileId: 'f1', fileName: 'a.png')]);
        drafts.writePhotos('c', [
          (fileId: 'f1', fileName: 'a.png'),
          (fileId: 'f2', fileName: 'b.png'),
        ]);
        drafts.write('d', '   ');
        expect(drafts.previews(), {
          'a': 'Book the hotel',
          'b': 'Photo',
          'c': '2 photos',
        });
      },
    );

    test('drops the oldest drafts past the limit', () async {
      final drafts = await ComposerDrafts.open();
      for (var i = 0; i < ComposerDrafts.maxDrafts + 3; i++) {
        drafts.write('c$i', 'draft $i');
      }
      drafts.write('c0', 'back again');
      await drafts.flush();

      final prefs = await SharedPreferences.getInstance();
      final stored =
          jsonDecode(prefs.getString(ComposerDrafts.storageKey)!) as Map;
      expect(stored.length, ComposerDrafts.maxDrafts);
      expect(stored.containsKey('c1'), isFalse);
      expect(stored['c0'], 'back again');
    });

    test('ignores a corrupt stored value', () async {
      SharedPreferences.setMockInitialValues({
        ComposerDrafts.storageKey: '{not json',
      });
      final drafts = await ComposerDrafts.open();
      expect(drafts.read('a'), '');
    });

    test('clearAll removes every draft from the device', () async {
      final drafts = await ComposerDrafts.open();
      drafts.write('a', 'secret plan');
      await drafts.flush();

      await ComposerDrafts.clearAll();

      expect((await ComposerDrafts.open()).read('a'), '');
    });
  });

  group('user message actions', () {
    Widget host(Widget child) => MaterialApp(
      theme: buildJarvisTheme(),
      home: Scaffold(body: child),
    );

    testWidgets('long-press offers copy, edit and select', (tester) async {
      String? edited;
      await tester.pumpWidget(
        host(
          MessageBubble(
            message: const MessageEntry(role: 'user', content: 'Book a table'),
            onEdit: (text) => edited = text,
          ),
        ),
      );

      await tester.longPress(find.text('Book a table'));
      await tester.pumpAndSettle();
      expect(find.text('Copy'), findsOneWidget);
      expect(find.text('Select text'), findsOneWidget);

      await tester.tap(find.text('Edit as new message'));
      await tester.pumpAndSettle();
      expect(edited, 'Book a table');
    });

    testWidgets('long-press can share the message through the phone', (
      tester,
    ) async {
      final shared = <String>[];
      final original = textSharer;
      textSharer = (text, _) async => shared.add(text);
      addTearDown(() => textSharer = original);
      await tester.pumpWidget(
        host(
          const MessageBubble(
            message: MessageEntry(role: 'user', content: 'Dinner at 7'),
          ),
        ),
      );

      await tester.longPress(find.text('Dinner at 7'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('message-share')));
      await tester.pumpAndSettle();

      expect(shared, ['Dinner at 7']);
    });

    testWidgets('a reply can be shared from its menu', (tester) async {
      final shared = <String>[];
      final original = textSharer;
      textSharer = (text, _) async => shared.add(text);
      addTearDown(() => textSharer = original);
      await tester.pumpWidget(
        host(
          const MessageBubble(
            message: MessageEntry(role: 'assistant', content: 'It is 7 pm.'),
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('message-more')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Share'));
      await tester.pumpAndSettle();

      expect(shared, ['It is 7 pm.']);
    });

    testWidgets('edit is hidden when the chat cannot take it', (tester) async {
      await tester.pumpWidget(
        host(
          const MessageBubble(
            message: MessageEntry(role: 'user', content: 'Hi'),
          ),
        ),
      );

      await tester.longPress(find.text('Hi'));
      await tester.pumpAndSettle();
      expect(find.text('Copy'), findsOneWidget);
      expect(find.text('Edit as new message'), findsNothing);
    });

    testWidgets('the composer takes focus on request', (tester) async {
      final focus = ValueNotifier<int>(0);
      final controller = TextEditingController();
      addTearDown(focus.dispose);
      addTearDown(controller.dispose);
      await tester.pumpWidget(
        host(
          ChatComposer(
            controller: controller,
            onSend: () {},
            onVoice: null,
            sending: false,
            voiceActive: false,
            voiceStarting: false,
            focusRequests: focus,
          ),
        ),
      );
      final field = tester.widget<TextField>(find.byType(TextField));
      expect(field.focusNode!.hasFocus, isFalse);

      focus.value++;
      await tester.pump();

      expect(field.focusNode!.hasFocus, isTrue);
    });
  });
}
