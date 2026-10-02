import 'dart:async';

import 'package:flutter/material.dart';

import '../../app_lock.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Turns the Face ID lock on or off and picks how soon it locks again.
class AppLockScreen extends StatefulWidget {
  const AppLockScreen({super.key});

  @override
  State<AppLockScreen> createState() => _AppLockScreenState();
}

class _AppLockScreenState extends State<AppLockScreen> {
  bool? _available;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final controller = AppLockScope.maybeOf(context);
    if (_available == null && controller != null) {
      unawaited(
        controller.isAvailable().then((value) {
          if (mounted) setState(() => _available = value);
        }),
      );
    }
  }

  Future<void> _toggle(AppLockController controller, bool value) async {
    final changed = await controller.setEnabled(value);
    if (!mounted || changed) return;
    ScaffoldMessenger.maybeOf(context)?.showSnackBar(
      SnackBar(
        content: Text(
          _available == false
              ? 'This device has no Face ID, Touch ID, or passcode set up.'
              : 'The lock was not changed because Face ID did not confirm it.',
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final controller = AppLockScope.maybeOf(context);
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Face ID lock')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
          children: [
            ContentWidth(
              child: controller == null
                  ? const InlineNotice(
                      message: 'The lock is not available here.',
                    )
                  : Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        SurfaceCard(
                          padding: const EdgeInsets.symmetric(vertical: 6),
                          child: SwitchListTile(
                            key: const Key('app-lock-switch'),
                            secondary: const IconBadge(
                              icon: PhosphorIconsRegular.lockSimple,
                              size: 34,
                            ),
                            title: const Text('Lock Jarvis'),
                            subtitle: const Text(
                              'Ask for Face ID or your passcode when you come back',
                            ),
                            value: controller.enabled,
                            onChanged:
                                controller.authenticating || _available == false
                                ? null
                                : (value) =>
                                      unawaited(_toggle(controller, value)),
                          ),
                        ),
                        if (_available == false) ...[
                          const SizedBox(height: 12),
                          const InlineNotice(
                            message:
                                'Set up Face ID, Touch ID, or a passcode on this device to use the lock.',
                          ),
                        ],
                        if (controller.enabled) ...[
                          const SizedBox(height: 22),
                          const SectionHeader('Lock again'),
                          SurfaceCard(
                            padding: const EdgeInsets.symmetric(vertical: 6),
                            child: Column(
                              children: [
                                for (final (index, delay)
                                    in AppLockDelay.values.indexed) ...[
                                  if (index > 0) const Divider(indent: 16),
                                  ListTile(
                                    key: Key('app-lock-${delay.name}'),
                                    title: Text(delay.label),
                                    trailing: Icon(
                                      controller.delay == delay
                                          ? PhosphorIconsRegular.checkCircle
                                          : PhosphorIconsRegular.circle,
                                      size: 22,
                                      color: controller.delay == delay
                                          ? colors.ink
                                          : colors.muted,
                                    ),
                                    onTap: () =>
                                        unawaited(controller.setDelay(delay)),
                                  ),
                                ],
                              ],
                            ),
                          ),
                        ],
                        const SizedBox(height: 16),
                        Text(
                          'While Jarvis is in the app switcher its screen is blurred, so nobody can read your chats over your shoulder. Reminders and notifications still arrive as usual.',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
            ),
          ],
        ),
      ),
    );
  }
}
