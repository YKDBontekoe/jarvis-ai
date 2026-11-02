import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/theme.dart';

final _png = Uint8List.fromList([
  0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, //
  0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
  0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
  0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
  0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
  0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
]);

void main() {
  late TextEditingController controller;
  var sent = 0;
  late List<PendingPhoto> photos;

  setUp(() {
    controller = TextEditingController();
    sent = 0;
    photos = [];
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(420, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: Align(
            alignment: Alignment.bottomCenter,
            child: StatefulBuilder(
              builder: (context, setState) => ChatComposer(
                controller: controller,
                onSend: () => sent++,
                onVoice: null,
                sending: false,
                voiceActive: false,
                voiceStarting: false,
                photos: photos,
              ),
            ),
          ),
        ),
      ),
    );
  }

  testWidgets('the expand button appears once there is text', (tester) async {
    await show(tester);
    expect(find.byKey(const Key('composer-expand')), findsNothing);
    await tester.enterText(find.byType(TextField), 'Hello');
    await tester.pump();
    expect(find.byKey(const Key('composer-expand')), findsOneWidget);
  });

  testWidgets('expanding keeps the text and cursor; collapsing keeps edits', (
    tester,
  ) async {
    await show(tester);
    await tester.enterText(find.byType(TextField), 'First line\nSecond line');
    controller.selection = const TextSelection.collapsed(offset: 5);
    await tester.pump();

    await tester.tap(find.byKey(const Key('composer-expand')));
    await tester.pumpAndSettle();
    final expanded = find.byKey(const Key('composer-expanded-field'));
    expect(expanded, findsOneWidget);
    expect(
      tester.widget<TextField>(expanded).controller!.text,
      'First line\nSecond line',
    );
    expect(controller.selection.baseOffset, 5);

    await tester.enterText(expanded, 'First line\nSecond line\nThird line');
    await tester.tap(find.byKey(const Key('composer-collapse')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('composer-expanded-field')), findsNothing);
    expect(controller.text, 'First line\nSecond line\nThird line');
    expect(sent, 0, reason: 'collapsing never sends');
  });

  testWidgets('attached photos are still shown in the expanded editor', (
    tester,
  ) async {
    photos = [
      PendingPhoto(
        localId: 'p1',
        bytes: _png,
        fileName: 'leaf.png',
        fileId: 'f1',
      ),
    ];
    await show(tester);
    await tester.enterText(find.byType(TextField), 'What is this?');
    await tester.pump();
    await tester.tap(find.byKey(const Key('composer-expand')));
    await tester.pumpAndSettle();

    expect(find.byTooltip('Remove photo'), findsWidgets);
    expect(find.byType(ComposerPhotoStrip), findsOneWidget);
  });

  testWidgets('Send in the expanded editor closes it and sends once', (
    tester,
  ) async {
    await show(tester);
    await tester.enterText(find.byType(TextField), 'Book the hotel');
    await tester.pump();
    await tester.tap(find.byKey(const Key('composer-expand')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('composer-expanded-send')));
    await tester.pumpAndSettle();
    expect(sent, 1);
    expect(find.byKey(const Key('composer-expanded-field')), findsNothing);
  });

  testWidgets('Send is off in the expanded editor while the message is empty', (
    tester,
  ) async {
    await show(tester);
    await tester.enterText(find.byType(TextField), 'x');
    await tester.pump();
    await tester.tap(find.byKey(const Key('composer-expand')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('composer-expanded-field')),
      '   ',
    );
    await tester.pump();
    expect(
      tester
          .widget<IconButton>(find.byKey(const Key('composer-expanded-send')))
          .onPressed,
      isNull,
    );
  });
}
