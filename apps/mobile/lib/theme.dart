import 'package:flutter/material.dart';

class JarvisColors {
  static const background = Color(0xff0e0f15);
  static const surface = Color(0xff161822);
  static const surfaceRaised = Color(0xff1d2030);
  static const outline = Color(0xff2a2d3d);
  static const accent = Color(0xffa895ff);
  static const accentDeep = Color(0xff7f6be6);
  static const userBubble = Color(0xff2b2842);
  static const success = Color(0xff68d6a8);
  static const warning = Color(0xffffcb6b);
  static const danger = Color(0xfff28b82);
  static const muted = Color(0xff8b8ea3);
}

ThemeData buildJarvisTheme() {
  final scheme =
      ColorScheme.fromSeed(
        seedColor: JarvisColors.accent,
        brightness: Brightness.dark,
      ).copyWith(
        surface: JarvisColors.surface,
        primary: JarvisColors.accent,
        error: JarvisColors.danger,
        outlineVariant: JarvisColors.outline,
      );
  return ThemeData(
    brightness: Brightness.dark,
    useMaterial3: true,
    colorScheme: scheme,
    scaffoldBackgroundColor: JarvisColors.background,
    splashFactory: InkSparkle.splashFactory,
    appBarTheme: const AppBarTheme(
      backgroundColor: JarvisColors.background,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(
        fontSize: 18,
        fontWeight: FontWeight.w600,
        letterSpacing: -.2,
      ),
    ),
    cardTheme: CardThemeData(
      color: JarvisColors.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: const BorderSide(color: JarvisColors.outline),
      ),
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: JarvisColors.background,
      surfaceTintColor: Colors.transparent,
      indicatorColor: JarvisColors.accent.withValues(alpha: .18),
      labelTextStyle: WidgetStateProperty.all(
        const TextStyle(fontSize: 12, fontWeight: FontWeight.w500),
      ),
    ),
    navigationRailTheme: NavigationRailThemeData(
      backgroundColor: JarvisColors.background,
      indicatorColor: JarvisColors.accent.withValues(alpha: .18),
      selectedIconTheme: const IconThemeData(color: JarvisColors.accent),
    ),
    chipTheme: ChipThemeData(
      backgroundColor: JarvisColors.surface,
      side: const BorderSide(color: JarvisColors.outline),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      labelStyle: const TextStyle(fontSize: 13),
    ),
    dividerTheme: const DividerThemeData(color: JarvisColors.outline),
    snackBarTheme: const SnackBarThemeData(behavior: SnackBarBehavior.floating),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: JarvisColors.surfaceRaised,
        borderRadius: BorderRadius.circular(8),
      ),
      textStyle: const TextStyle(color: Colors.white, fontSize: 12),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: JarvisColors.surface,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(26),
        borderSide: BorderSide.none,
      ),
      contentPadding: const EdgeInsets.symmetric(horizontal: 20, vertical: 15),
    ),
  );
}
