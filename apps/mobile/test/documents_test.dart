import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/documents/document_screen.dart';
import 'package:jarvis_mobile/features/documents/documents_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _path = '/api/v1/documents/d1';

Map<String, Object?> _section(
  String id,
  String heading,
  String body, {
  List<String> refs = const [],
  bool edited = false,
  String? previous,
}) => {
  'id': id,
  'heading': heading,
  'body': body,
  'refs': refs,
  'edited': edited,
  'previous': previous,
};

Map<String, Object?> _view({
  int version = 1,
  int saved = 0,
  List<Map<String, Object?>>? sections,
}) => {
  'draft': {
    'id': 'd1',
    'title': 'Kitchen: update',
    'version': version,
    'savedVersion': saved,
    'sections':
        sections ??
        [
          _section(
            'where',
            'Where we are',
            'Kitchen: cook for ten.',
            refs: ['project:p1'],
          ),
          _section('next', 'Next step', '[ADD: the next step]'),
        ],
  },
  'checklist': [
    {
      'text': '1 section(s) still have [ADD…] placeholders to fill or remove.',
      'ok': false,
    },
    {'text': 'Saving or exporting does not send anything.', 'ok': true},
  ],
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 3600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      KeyedSubtree(key: UniqueKey(), child: _host(child)),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'the list starts empty kindly and creating posts the choices then opens the draft',
    (tester) async {
      http
        ..on('GET', '/api/v1/documents', <Object>[])
        ..on('GET', '/api/v1/projects', [
          {'id': 'p1', 'name': 'Kitchen'},
        ])
        ..on('POST', '/api/v1/documents', _view(), status: 201)
        ..on('GET', _path, _view());
      await show(tester, DocumentsScreen(http: http.client()));
      expect(find.byKey(const Key('document-start')), findsOneWidget);

      await tester.tap(find.byKey(const Key('document-start')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<FilledButton>(find.byKey(const Key('document-create')))
            .onPressed,
        isNull,
      );
      await tester.tap(find.byKey(const Key('document-project')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Kitchen').last);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('document-audience-client')));
      await tester.tap(find.byKey(const Key('document-language-nl')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('document-create')));
      await tester.pumpAndSettle();

      expect(http.sent('POST', '/api/v1/documents').single.body, {
        'kind': 'project_update',
        'projectId': 'p1',
        'audience': 'client',
        'language': 'nl',
      });
      expect(find.byType(DocumentScreen), findsOneWidget);
    },
  );

  testWidgets(
    'shows sections with their sources, placeholders and the review list',
    (tester) async {
      http.on('GET', _path, _view());
      await show(tester, DocumentScreen(http: http.client(), documentId: 'd1'));

      expect(find.textContaining('Sources: project:p1'), findsOneWidget);
      expect(find.text('[ADD: the next step]'), findsOneWidget);
      expect(find.textContaining('placeholders to fill'), findsOneWidget);
      expect(find.textContaining('does not send anything'), findsOneWidget);
      expect(find.text('Not saved yet'), findsOneWidget);
      expect(find.text('VERSION 1'), findsOneWidget);
    },
  );

  testWidgets(
    'editing one section sends only that section and marks it edited with an undo',
    (tester) async {
      http
        ..on('GET', _path, _view())
        ..on(
          'PUT',
          '$_path/sections/next',
          _view(
            version: 2,
            sections: [
              _section(
                'where',
                'Where we are',
                'Kitchen: cook for ten.',
                refs: ['project:p1'],
              ),
              _section(
                'next',
                'Next step',
                'Order the worktop.',
                edited: true,
                previous: '[ADD: the next step]',
              ),
            ],
          ),
        );
      await show(tester, DocumentScreen(http: http.client(), documentId: 'd1'));
      expect(find.byKey(const Key('document-undo-next')), findsNothing);

      await tester.tap(find.byKey(const Key('document-edit-next')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('document-edit-field')),
        'Order the worktop.',
      );
      await tester.tap(find.byKey(const Key('document-edit-save')));
      await tester.pumpAndSettle();

      expect(http.sent('PUT', '$_path/sections/next').single.body, {
        'body': 'Order the worktop.',
      });
      expect(find.text('Order the worktop.'), findsOneWidget);
      expect(find.text('EDITED'), findsOneWidget);
      expect(find.text('Next step'), findsOneWidget);
      expect(find.byKey(const Key('document-undo-next')), findsOneWidget);
      expect(
        find.textContaining('Kitchen: cook for ten.'),
        findsOneWidget,
      ); // the other section is untouched
    },
  );

  testWidgets('shorten, rebuild and undo call their routes', (tester) async {
    http
      ..on('GET', _path, _view())
      ..on('POST', '$_path/sections/where/revise', _view(version: 2))
      ..on('POST', '$_path/sections/where/undo', _view(version: 3));
    await show(tester, DocumentScreen(http: http.client(), documentId: 'd1'));

    await tester.tap(find.byKey(const Key('document-shorten-where')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('document-rebuild-where')));
    await tester.pumpAndSettle();

    final posts = http
        .sent('POST', '$_path/sections/where/revise')
        .map((r) => (r.body as Map)['action']);
    expect(posts, ['shorten', 'rebuild']);
  });

  testWidgets('a refused revision shows the reason', (tester) async {
    http
      ..on('GET', _path, _view())
      ..on('POST', '$_path/sections/where/revise', {
        'message': 'That section is already short.',
      }, status: 409);
    await show(tester, DocumentScreen(http: http.client(), documentId: 'd1'));

    await tester.tap(find.byKey(const Key('document-shorten-where')));
    await tester.pumpAndSettle();

    expect(find.textContaining('already short'), findsOneWidget);
  });

  testWidgets(
    'saving says it went to the Library with its sources and updates the version line',
    (tester) async {
      http
        ..on('GET', _path, _view())
        ..on('POST', '$_path/save', _view(saved: 1));
      await show(tester, DocumentScreen(http: http.client(), documentId: 'd1'));

      await tester.tap(find.byKey(const Key('document-save')));
      await tester.pumpAndSettle();

      expect(
        find.textContaining('Saved to your Library with its sources'),
        findsOneWidget,
      );
      expect(find.text('Saved to your Library'), findsOneWidget);
    },
  );

  testWidgets('copy as Markdown fetches the export and sends nothing', (
    tester,
  ) async {
    http
      ..on('GET', _path, _view())
      ..on('GET', '$_path/export', '# Kitchen: update');
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      (call) async => null,
    );
    await show(tester, DocumentScreen(http: http.client(), documentId: 'd1'));

    await tester.tap(find.byKey(const Key('document-export')));
    await tester.pumpAndSettle();

    expect(http.sent('GET', '$_path/export').single.query['format'], 'md');
    expect(find.textContaining('Nothing was sent'), findsOneWidget);
  });
}
