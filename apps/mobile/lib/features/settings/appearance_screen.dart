import 'package:flutter/material.dart';

import '../../appearance.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

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
    icon: PhosphorIconsRegular.sun,
  ),
  _AppearanceOption(
    preference: AppearancePreference.dark,
    title: 'Dark',
    subtitle: 'Low-light surfaces and light ink',
    icon: PhosphorIconsRegular.moon,
  ),
];

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
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return ListTile(
      key: Key('appearance-${option.preference.name}'),
      leading: IconBadge(icon: option.icon, size: 34),
      title: Text(option.title),
      subtitle: Text(option.subtitle),
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
