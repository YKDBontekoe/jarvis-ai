import 'package:flutter/rendering.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

void main() {
  late SemanticsHandle handle;
  setUp(() => handle = SemanticsBinding.instance.ensureSemantics());
  tearDown(() => handle.dispose());

  Future<SemanticsNode> notice(WidgetTester tester, NoticeTone tone) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: InlineNotice(message: 'Could not save', tone: tone),
        ),
      ),
    );
    return tester.getSemantics(find.text('Could not save'));
  }

  testWidgets('failures and confirmations are live regions', (tester) async {
    for (final tone in [NoticeTone.danger, NoticeTone.success]) {
      final node = await notice(tester, tone);
      expect(
        node.getSemanticsData().flagsCollection.isLiveRegion,
        isTrue,
        reason: '$tone must be announced when it appears',
      );
    }
  });

  testWidgets('calm notices stay quiet', (tester) async {
    final node = await notice(tester, NoticeTone.info);
    expect(node.getSemanticsData().flagsCollection.isLiveRegion, isFalse);
  });
}
