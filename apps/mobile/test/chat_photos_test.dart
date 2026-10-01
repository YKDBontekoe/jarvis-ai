import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/theme.dart';

// A 1x1 transparent PNG.
final _png = Uint8List.fromList([
  0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, //
  0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
  0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
  0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
  0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
  0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
]);

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: Center(child: child)),
);

Widget _composer({
  required TextEditingController controller,
  required List<PendingPhoto> photos,
  required VoidCallback onSend,
  ValueChanged<PendingPhoto>? onRemove,
}) => ChatComposer(
  controller: controller,
  onSend: onSend,
  onVoice: null,
  sending: false,
  voiceActive: false,
  voiceStarting: false,
  photos: photos,
  onRemovePhoto: onRemove,
  onPhoto: () {},
);

IconButton _sendButton(WidgetTester tester) => tester.widget<IconButton>(
  find.ancestor(of: find.byTooltip('Send'), matching: find.byType(IconButton)),
);

void main() {
  testWidgets('a ready photo can be sent without text', (tester) async {
    final controller = TextEditingController();
    addTearDown(controller.dispose);
    var sent = 0;
    await tester.pumpWidget(
      _host(
        _composer(
          controller: controller,
          onSend: () => sent++,
          photos: [
            PendingPhoto(
              localId: 'p1',
              bytes: _png,
              fileName: 'leaf.png',
              fileId: 'f1',
            ),
          ],
        ),
      ),
    );

    expect(find.byTooltip('Add a photo'), findsOneWidget);
    expect(find.byTooltip('Remove photo'), findsOneWidget);
    await tester.tap(find.byTooltip('Send'));
    expect(sent, 1);
  });

  testWidgets('send waits while a photo is uploading', (tester) async {
    final controller = TextEditingController(text: 'What is this?');
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      _host(
        _composer(
          controller: controller,
          onSend: () {},
          photos: [PendingPhoto(localId: 'p1', bytes: _png, fileName: 'a.jpg')],
        ),
      ),
    );

    expect(_sendButton(tester).onPressed, isNull);
    expect(find.bySemanticsLabel('Photo uploading'), findsOneWidget);
  });

  testWidgets('removing a photo reports which one', (tester) async {
    final controller = TextEditingController();
    addTearDown(controller.dispose);
    PendingPhoto? removed;
    final photo = PendingPhoto(
      localId: 'p1',
      bytes: _png,
      fileName: 'a.jpg',
      failed: true,
    );
    await tester.pumpWidget(
      _host(
        _composer(
          controller: controller,
          onSend: () {},
          photos: [photo],
          onRemove: (value) => removed = value,
        ),
      ),
    );

    expect(find.bySemanticsLabel('Photo did not upload'), findsOneWidget);
    await tester.tap(find.byTooltip('Remove photo'));
    expect(removed, same(photo));
  });

  testWidgets('photo-only messages show the photo without stand-in text', (
    tester,
  ) async {
    final requested = <String>[];
    await tester.pumpWidget(
      _host(
        MessageBubble(
          message: const MessageEntry(
            role: 'user',
            content: 'Shared a photo.',
            photos: [MessagePhoto(fileId: 'f1', fileName: 'leaf.png')],
          ),
          photoLoader: (id) async {
            requested.add(id);
            return _png;
          },
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(requested, ['f1']);
    expect(find.text('Shared a photo.'), findsNothing);
    expect(find.byType(Image), findsOneWidget);

    await tester.tap(find.bySemanticsLabel('Photo leaf.png'));
    await tester.pumpAndSettle();
    expect(find.byType(PhotoViewerPage), findsOneWidget);
  });

  testWidgets('photos with a question keep the text', (tester) async {
    await tester.pumpWidget(
      _host(
        MessageBubble(
          message: MessageEntry(
            role: 'user',
            content: 'Is this plant healthy?',
            photos: [MessagePhoto(fileId: 'f1', fileName: 'a', bytes: _png)],
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Is this plant healthy?'), findsOneWidget);
    expect(find.byType(Image), findsOneWidget);
  });

  test('message photos parse from the API and skip bad rows', () {
    final photos = MessagePhoto.listFromJson([
      {'fileId': 'f1', 'fileName': 'a.jpg', 'contentType': 'image/jpeg'},
      {'fileId': 'f2', 'contentType': 'application/pdf'},
      {'fileName': 'no id'},
      'nope',
    ]);

    expect(photos.map((photo) => photo.fileId), ['f1']);
    expect(MessagePhoto.listFromJson(null), isEmpty);
  });
}
