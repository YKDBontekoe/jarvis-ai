import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:jarvis_mobile/features/shell/jarvis_tab_bar.dart';
import 'package:jarvis_mobile/main.dart';

import 'fixtures.dart';
import 'harness.dart';

/// Actual shell at iPhone SE, mini, standard and Max widths, plus larger text.
/// flutter test test/screenshots/ui_polish.dart --no-pub
void main() {
  setUpAll(loadAppFonts);
  setUp(mockPlatformChannels);
  tearDown(() => debugJarvisHttpAdapter = null);

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 30; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(tester.takeException(), isNull);
  }

  for (final brightness in [Brightness.light, Brightness.dark]) {
    for (final size in [
      const Size(320, 568),
      const Size(375, 812),
      const Size(393, 852),
      const Size(430, 932),
    ]) {
      for (final scale in [1.0, 2.0]) {
        screenshotTest(
          '${brightness.name} polished shell at ${size.width.toInt()}pt, text $scale',
          (tester) async {
            usePhone(tester, size: size);
            tester.platformDispatcher.platformBrightnessTestValue = brightness;
            tester.platformDispatcher.textScaleFactorTestValue = scale;
            addTearDown(tester.platformDispatcher.clearAllTestValues);
            final routes = fixtureRoutes(withApproval: false);
            final now = DateTime.now();
            final next = now.add(const Duration(hours: 20));
            routes['GET /api/v1/home'] = {
              ...home(),
              'approvals': <Object>[],
              'reminders': <Object>[],
              'calendar': {
                'connected': true,
                'events': [
                  {
                    'title': 'Training',
                    'startAt': next.toUtc().toIso8601String(),
                    'endAt': next
                        .add(const Duration(hours: 1))
                        .toUtc()
                        .toIso8601String(),
                    'location': 'Borst & armen',
                  },
                ],
              },
            };
            routes['GET /api/v1/conversations'] = [
              ...conversations(),
              for (var i = 0; i < 2; i++)
                {
                  'id': 'duplicate-$i',
                  'title': 'Jarvis AI',
                  'profileName': i == 0 ? 'Personal' : 'Work',
                  'createdAt': now
                      .subtract(Duration(days: i + 1))
                      .toUtc()
                      .toIso8601String(),
                  'updatedAt': now
                      .subtract(Duration(minutes: i))
                      .toUtc()
                      .toIso8601String(),
                },
            ];
            routes['GET /api/v1/channels'] = [
              {'id': 'wa1', 'kind': 'whatsapp_linked', 'account': 'Personal'},
              {'id': 'wa2', 'kind': 'whatsapp_linked', 'account': 'Work'},
            ];
            for (final id in ['wa1', 'wa2']) {
              routes['GET /api/v1/channels/$id/chats'] = {
                'chats': [
                  {
                    'chatId': 'rosa',
                    'name': 'rosa',
                    'lastMessageAt': now.toUtc().toIso8601String(),
                    'preview': id == 'wa1' ? 'See you tomorrow' : null,
                    'unreadCount': 0,
                  },
                ],
              };
            }
            debugJarvisHttpAdapter = ScreenshotHttp(routes);
            await tester.pumpWidget(
              RepaintBoundary(
                key: screenshotKey,
                child: const JarvisApp(skipAuthentication: true),
              ),
            );
            await settle(tester);
            expect(find.text('Youri'), findsOneWidget);
            expect(find.byKey(const Key('home-settings')), findsNothing);
            expect(find.byKey(const Key('home-greeting')), findsOneWidget);
            final look = brightness == Brightness.light ? 'day' : 'night';
            final suffix =
                '$look-${size.width.toInt()}-${(scale * 100).toInt()}';
            expect(
              find.descendant(
                of: find.byType(JarvisTabBar),
                matching: find.byType(Text),
              ),
              findsNothing,
            );
            await capture(tester, 'polish-home-$suffix');
            await tester.tap(find.byKey(const Key('tab-chats')));
            await settle(tester);
            expect(find.text('WhatsApp · Personal'), findsOneWidget);
            await capture(tester, 'polish-chats-$suffix');
            await tester.scrollUntilVisible(
              find.text('WhatsApp · Work'),
              100,
              scrollable: find.descendant(
                of: find.byKey(const Key('chats-list')),
                matching: find.byType(Scrollable),
              ),
            );
            await settle(tester);
            expect(find.text('WhatsApp · Work'), findsOneWidget);
            expect(find.text('No messages yet'), findsNothing);
            await tester.tap(find.byKey(const Key('tab-everything')));
            await settle(tester);
            await capture(tester, 'polish-everything-$suffix');
            for (final spec in tileSpecs) {
              final label = find.descendant(
                of: find.byKey(Key('everything-${spec.id}')),
                matching: find.text(spec.name),
              );
              final paragraph = tester.renderObject<RenderParagraph>(label);
              expect(paragraph.didExceedMaxLines, isFalse, reason: spec.name);
              final description = find.descendant(
                of: find.byKey(Key('everything-${spec.id}')),
                matching: find.text(spec.description),
              );
              expect(
                tester
                    .renderObject<RenderParagraph>(description)
                    .didExceedMaxLines,
                isFalse,
                reason: '${spec.name} description',
              );
            }
            final weekly = find.byKey(const Key('everything-weekly-review'));
            await tester.ensureVisible(weekly);
            await settle(tester);
            await capture(tester, 'polish-plan-$suffix');
            await tester.tap(weekly);
            await settle(tester);
            expect(find.byKey(const Key('tile-preview')), findsOneWidget);
            final previewLabel = find.descendant(
              of: find.byKey(const Key('tile-preview')),
              matching: find.text('Weekly review'),
            );
            expect(
              tester
                  .renderObject<RenderParagraph>(previewLabel)
                  .didExceedMaxLines,
              isFalse,
            );
            await capture(tester, 'polish-preview-$suffix');
          },
        );
      }
    }
  }
}
