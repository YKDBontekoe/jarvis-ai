import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/generative_ui.dart';

void main() {
  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(430, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: SingleChildScrollView(child: child)),
      ),
    );
    await tester.pumpAndSettle();
  }

  UiSurfaceEntry surface(
    Map<String, dynamic> schema, {
    String status = 'open',
    UiSurfaceAnswer? answer,
  }) => UiSurfaceEntry(
    id: 'surf',
    title: schema['title'] as String,
    status: status,
    schema: schema,
    answer: answer,
  );

  testWidgets('a multi-select choice sends every picked option', (
    tester,
  ) async {
    String? action;
    Map<String, String>? values;
    await show(
      tester,
      UiSurfaceCard(
        surface: surface({
          'kind': 'choice',
          'title': 'Which days?',
          'selection': 'multiple',
          'items': [
            {'id': 'mon', 'title': 'Monday'},
            {'id': 'wed', 'title': 'Wednesday'},
            {'id': 'fri', 'title': 'Friday'},
          ],
          'actions': [
            {'id': 'done', 'label': 'Done', 'style': 'primary'},
          ],
        }),
        onAction: (id, submitted) async {
          action = id;
          values = submitted;
        },
      ),
    );

    expect(find.text('PICK ANY'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Done'));
    await tester.pump();
    expect(action, isNull);
    expect(find.text('Pick at least one option.'), findsOneWidget);

    await tester.tap(find.text('Monday'));
    await tester.tap(find.text('Friday'));
    await tester.tap(find.text('Wednesday'));
    await tester.tap(find.text('Wednesday'));
    await tester.pump();
    await tester.tap(find.widgetWithText(FilledButton, 'Done'));
    await tester.pumpAndSettle();
    expect(action, 'done');
    expect(values?['choice'], 'mon,fri');
    expect(values?['label'], 'Monday, Friday');
  });

  testWidgets('forms send sliders, chips, toggles and dates', (tester) async {
    Map<String, String>? values;
    await show(
      tester,
      UiSurfaceCard(
        surface: surface({
          'kind': 'form',
          'title': 'Plan a workout',
          'fields': [
            {
              'id': 'energy',
              'label': 'Energy',
              'type': 'slider',
              'min': 1,
              'max': 5,
              'step': 1,
              'unit': 'pts',
            },
            {
              'id': 'kind',
              'label': 'Kind',
              'type': 'choice',
              'options': ['Run', 'Gym', 'Swim'],
            },
            {'id': 'buddy', 'label': 'With a friend', 'type': 'toggle'},
            {'id': 'day', 'label': 'Day', 'type': 'date'},
          ],
          'actions': [
            {'id': 'plan', 'label': 'Plan it', 'style': 'primary'},
          ],
        }),
        onAction: (_, submitted) async => values = submitted,
      ),
    );

    expect(find.text('3 pts'), findsOneWidget);
    expect(find.byType(DropdownButtonFormField<String>), findsNothing);
    await tester.tap(find.text('Gym'));
    await tester.tap(find.text('With a friend'));
    await tester.tap(find.text('Pick a date'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Plan it'));
    await tester.pumpAndSettle();

    final today = DateTime.now();
    expect(values, {
      'energy': '3',
      'kind': 'Gym',
      'buddy': 'true',
      'day':
          '${today.year}-${today.month.toString().padLeft(2, '0')}-'
          '${today.day.toString().padLeft(2, '0')}',
    });
  });

  testWidgets('custom cards draw every block and stay after closing', (
    tester,
  ) async {
    final schema = {
      'kind': 'custom',
      'title': 'Your week',
      'blocks': [
        {'type': 'heading', 'text': 'Training'},
        {
          'type': 'stats',
          'items': [
            {'label': 'Workouts', 'value': '3', 'delta': '+1', 'trend': 'up'},
            {'label': 'Sleep', 'value': '7 h'},
          ],
        },
        {'type': 'progress', 'label': 'Goal', 'value': 0.6, 'detail': '3/5'},
        {
          'type': 'bars',
          'unit': 'km',
          'items': [
            {'label': 'Mon', 'value': 5},
            {'label': 'Thu', 'value': 8.5},
          ],
        },
        {
          'type': 'steps',
          'items': [
            {'title': 'Monday run', 'done': true},
            {'title': 'Friday gym', 'detail': '18:00'},
          ],
        },
        {
          'type': 'columns',
          'children': [
            {
              'type': 'pairs',
              'items': [
                {'label': 'Pace', 'value': '5:20 /km'},
              ],
            },
            {
              'type': 'chips',
              'labels': ['legs', 'cardio'],
            },
          ],
        },
        {'type': 'callout', 'text': 'Rest on Sunday', 'tone': 'success'},
        {'type': 'divider'},
        {'type': 'marquee', 'text': 'ignored'},
      ],
    };
    expect(surfaceAsksSomething(schema), isFalse);
    await show(tester, UiSurfaceCard(surface: surface(schema)));

    for (final text in [
      'Training',
      'Workouts',
      '+1',
      '7 h',
      '60%',
      '8.5 km',
      'Monday run',
      '18:00',
      '5:20 /km',
      'cardio',
      'Rest on Sunday',
    ]) {
      expect(find.text(text), findsOneWidget, reason: text);
    }
    expect(find.text('ignored'), findsNothing);
    expect(tester.takeException(), isNull);

    await show(
      tester,
      UiSurfaceCard(surface: surface(schema, status: 'replaced')),
    );
    expect(find.text('Rest on Sunday'), findsOneWidget);
    expect(find.text('Closed'), findsNothing);
  });

  testWidgets('list items show their value and badge', (tester) async {
    await show(
      tester,
      UiSurfaceCard(
        surface: surface({
          'kind': 'list',
          'title': 'Bills',
          'items': [
            {
              'id': 'rent',
              'title': 'Rent',
              'icon': 'money',
              'value': '€950',
              'badge': 'Due',
            },
          ],
        }),
      ),
    );

    expect(find.text('€950'), findsOneWidget);
    expect(find.text('Due'), findsOneWidget);
  });

  testWidgets('an answered card shows what was picked', (tester) async {
    await show(
      tester,
      UiSurfaceCard(
        surface: surface(
          {
            'kind': 'choice',
            'title': 'Which tone?',
            'items': [
              {'id': 'hard', 'title': 'Hard push'},
            ],
          },
          status: 'completed',
          answer: const UiSurfaceAnswer(
            actionId: 'hard',
            values: {'choice': 'hard', 'label': 'Hard push'},
          ),
        ),
      ),
    );

    expect(find.text('Answered'), findsOneWidget);
    expect(find.text('Hard push'), findsOneWidget);
  });

  test('answers never show a secret', () {
    final schema = {
      'kind': 'form',
      'title': 'Token',
      'fields': [
        {'id': 'token', 'label': 'Token', 'type': 'secret'},
        {'id': 'energy', 'label': 'Energy', 'type': 'slider', 'unit': 'pts'},
      ],
    };
    final values = redactSurfaceValues(schema, {
      'token': 'ghp_secret',
      'energy': '4',
    });
    expect(values['token'], storedSecretMarker);

    final summary = surfaceAnswerSummary(
      UiSurfaceEntry(
        id: 's',
        title: 'Token',
        status: 'completed',
        schema: schema,
        answer: UiSurfaceAnswer(actionId: 'save', values: values),
      ),
    );
    expect(summary, 'Saved securely · 4 pts');
    expect(summary, isNot(contains('ghp_')));
  });

  test('surfaces read the answer from the server', () {
    final entry = UiSurfaceEntry.fromJson({
      'id': 's',
      'title': 'Tone',
      'status': 'completed',
      'schema': {'kind': 'choice', 'title': 'Tone'},
      'answer': {
        'actionId': 'hard',
        'values': {'label': 'Hard push', 'bad': 3},
      },
    });
    expect(entry?.answer?.actionId, 'hard');
    expect(entry?.answer?.values, {'label': 'Hard push'});
  });
}
