import 'dart:async';

import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../home/home_widget_sync.dart';

/// What the home screen widgets are allowed to show. A widget can be seen by
/// anyone who picks up the phone, so details can be hidden: it then shows only
/// how many things need you.
class WidgetPrivacyScreen extends StatefulWidget {
  const WidgetPrivacyScreen({this.sync, super.key});

  /// Replaces the widget bridge in tests.
  final HomeWidgetSync? sync;

  @override
  State<WidgetPrivacyScreen> createState() => _WidgetPrivacyScreenState();
}

class _WidgetPrivacyScreenState extends State<WidgetPrivacyScreen> {
  bool? _hide;

  HomeWidgetSync get _sync => widget.sync ?? HomeWidgetSync.instance;

  @override
  void initState() {
    super.initState();
    unawaited(
      WidgetPrivacy.hideDetails().then((value) {
        if (mounted) setState(() => _hide = value);
      }),
    );
  }

  Future<void> _set(bool value) async {
    setState(() => _hide = value);
    await WidgetPrivacy.setHideDetails(value);
    // Hiding takes effect at once; showing again waits for Home's next refresh.
    if (value) await _sync.clear();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Widgets')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SurfaceCard(
                  child: SwitchListTile.adaptive(
                    key: const Key('widget-hide-details'),
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Hide widget details'),
                    subtitle: const Text(
                      'Widgets show only how many things need you, not their titles or names.',
                    ),
                    value: _hide ?? false,
                    onChanged: _hide == null ? null : (v) => unawaited(_set(v)),
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  'Widgets also say when they were last refreshed, and they are '
                  'emptied when you sign out. Turning details back on shows them '
                  'the next time Home refreshes.',
                  key: const Key('widget-privacy-note'),
                  style: TextStyle(color: colors.inkSoft, height: 1.4),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
