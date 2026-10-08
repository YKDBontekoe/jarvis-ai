import 'package:flutter/material.dart';

import '../../appearance.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'motion_gallery_screen.dart';

/// Light, dark, or match the device appearance. The choice is stored on this
/// device and applied to [MaterialApp.themeMode].
class AppearanceScreen extends StatelessWidget {
  const AppearanceScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final controller = AppearanceScope.of(context);
    final preference = controller.preference;
    final platform = MediaQuery.platformBrightnessOf(context);
    final effective = switch (preference) {
      AppearancePreference.light => Brightness.light,
      AppearancePreference.dark => Brightness.dark,
      AppearancePreference.system => platform,
    };

    return Scaffold(
      appBar: AppBar(title: const Text('Appearance')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
          children: [
            ContentWidth(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SurfaceCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          effective == Brightness.dark
                              ? 'Dark is on'
                              : 'Light is on',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 6),
                        Text(
                          preference == AppearancePreference.system
                              ? 'Jarvis is following this device, which is currently ${platform == Brightness.dark ? 'dark' : 'light'}.'
                              : 'Jarvis stays ${preference == AppearancePreference.dark ? 'dark' : 'light'} even if the device changes.',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 16),
                  SurfaceCard(
                    padding: const EdgeInsets.symmetric(vertical: 6),
                    child: Column(
                      children: [
                        for (final (index, option) in _options.indexed) ...[
                          if (index > 0) const Divider(indent: 64),
                          _AppearanceTile(
                            option: option,
                            selected: preference == option.preference,
                            onTap: () =>
                                controller.setPreference(option.preference),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SectionHeader(
                    'Motion',
                    padding: EdgeInsets.fromLTRB(4, 24, 0, 10),
                  ),
                  SurfaceCard(
                    padding: const EdgeInsets.symmetric(vertical: 6),
                    child: Column(
                      children: [
                        for (final (index, option)
                            in _motionOptions.indexed) ...[
                          if (index > 0) const Divider(indent: 64),
                          _ChoiceTile(
                            key: Key('motion-${option.motion.name}'),
                            icon: option.icon,
                            title: option.title,
                            subtitle: option.subtitle,
                            selected: controller.motion == option.motion,
                            onTap: () => controller.setMotion(option.motion),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),
                  SurfaceCard(
                    key: const Key('motion-preview'),
                    onTap: () => Navigator.of(context).push(
                      MaterialPageRoute<void>(
                        builder: (_) => const MotionGalleryScreen(),
                      ),
                    ),
                    child: Row(
                      children: [
                        const IconBadge(
                          icon: PhosphorIconsRegular.sparkle,
                          size: 34,
                        ),
                        const SizedBox(width: 14),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'Preview effects',
                                style: Theme.of(context).textTheme.titleSmall,
                              ),
                              Text(
                                'Try the animations Jarvis uses',
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                            ],
                          ),
                        ),
                        const Icon(PhosphorIconsRegular.caretRight, size: 18),
                      ],
                    ),
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

const _options = <_AppearanceOption>[
  _AppearanceOption(
    preference: AppearancePreference.system,
    title: 'Match device',
    subtitle: 'Follow the phone or computer light and dark setting',
    icon: PhosphorIconsRegular.circleHalf,
  ),
  _AppearanceOption(
    preference: AppearancePreference.light,
    title: 'Light',
    subtitle: 'Warm paper surfaces and dark ink',
    icon: PhosphorIconsRegular.lightbulb,
  ),
  _AppearanceOption(
    preference: AppearancePreference.dark,
    title: 'Dark',
    subtitle: 'Low-light surfaces and light ink',
    icon: PhosphorIconsRegular.moon,
  ),
];

const _motionOptions = <_MotionOption>[
  _MotionOption(
    motion: MotionPreference.system,
    title: 'Use device setting',
    subtitle: 'Follow the Reduce Motion setting on this device',
    icon: PhosphorIconsRegular.circleHalf,
  ),
  _MotionOption(
    motion: MotionPreference.full,
    title: 'Full',
    subtitle: 'Every animation, even if the device reduces motion',
    icon: PhosphorIconsRegular.sparkle,
  ),
  _MotionOption(
    motion: MotionPreference.reduced,
    title: 'Reduced',
    subtitle: 'A calm Jarvis: things appear in place without moving',
    icon: PhosphorIconsRegular.pauseCircle,
  ),
];

class _MotionOption {
  const _MotionOption({
    required this.motion,
    required this.title,
    required this.subtitle,
    required this.icon,
  });

  final MotionPreference motion;
  final String title;
  final String subtitle;
  final IconData icon;
}

class _AppearanceOption {
  const _AppearanceOption({
    required this.preference,
    required this.title,
    required this.subtitle,
    required this.icon,
  });

  final AppearancePreference preference;
  final String title;
  final String subtitle;
  final IconData icon;
}

class _AppearanceTile extends StatelessWidget {
  const _AppearanceTile({
    required this.option,
    required this.selected,
    required this.onTap,
  });

  final _AppearanceOption option;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => _ChoiceTile(
    key: Key('appearance-${option.preference.name}'),
    icon: option.icon,
    title: option.title,
    subtitle: option.subtitle,
    selected: selected,
    onTap: onTap,
  );
}

class _ChoiceTile extends StatelessWidget {
  const _ChoiceTile({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.selected,
    required this.onTap,
    super.key,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return ListTile(
      leading: IconBadge(icon: icon, size: 34),
      title: Text(title),
      subtitle: Text(subtitle),
      trailing: Icon(
        selected
            ? PhosphorIconsRegular.checkCircle
            : PhosphorIconsRegular.circle,
        size: 22,
        color: selected ? colors.ink : colors.muted,
      ),
      onTap: onTap,
    );
  }
}
