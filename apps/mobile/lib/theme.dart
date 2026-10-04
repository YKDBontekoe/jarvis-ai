import 'package:flutter/cupertino.dart' show CupertinoPageTransitionsBuilder;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'ui/motion.dart';
import 'ui/phosphor_icons.dart';

/// Semantic Jarvis palette. Read it from the ambient [Theme] with [JarvisColors.of]
/// so screens follow light, dark, and device appearance.
@immutable
class JarvisColors extends ThemeExtension<JarvisColors> {
  const JarvisColors({
    required this.brightness,
    required this.canvas,
    required this.surface,
    required this.surfaceMuted,
    required this.surfaceRaised,
    required this.outline,
    required this.outlineStrong,
    required this.ink,
    required this.inkSoft,
    required this.muted,
    required this.onInk,
    required this.accent,
    required this.accentDeep,
    required this.accentSoft,
    required this.violet,
    required this.sky,
    required this.rose,
    required this.success,
    required this.successSoft,
    required this.warning,
    required this.warningSoft,
    required this.danger,
    required this.dangerSoft,
    required this.info,
    required this.infoSoft,
  });

  final Brightness brightness;
  final Color canvas;
  Color get background => canvas;
  final Color surface;
  final Color surfaceMuted;
  final Color surfaceRaised;
  final Color outline;
  final Color outlineStrong;
  final Color ink;
  final Color inkSoft;
  final Color muted;
  final Color onInk;
  final Color accent;
  final Color accentDeep;
  final Color accentSoft;
  final Color violet;
  final Color sky;
  final Color rose;
  final Color success;
  final Color successSoft;
  final Color warning;
  final Color warningSoft;
  final Color danger;
  final Color dangerSoft;
  final Color info;
  final Color infoSoft;

  bool get isDark => brightness == Brightness.dark;

  /// Soft indigo-to-violet wash for the primary send action and highlights.
  LinearGradient get accentGradient => LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [accent, violet],
  );

  /// Dims the screen behind sheets and dialogs without going black.
  Color get scrim => (isDark ? const Color(0xff000000) : ink).withValues(
    alpha: isDark ? .5 : .28,
  );

  static JarvisColors of(BuildContext context) =>
      Theme.of(context).extension<JarvisColors>() ?? light;

  /// Iris: cool greys plus one indigo accent. Semantic colours stay quiet.
  static const light = JarvisColors(
    brightness: Brightness.light,
    canvas: Color(0xfff7f7fa),
    surface: Color(0xffffffff),
    surfaceMuted: Color(0xffefeff4),
    surfaceRaised: Color(0xffe5e5ee),
    outline: Color(0xffe6e6ec),
    outlineStrong: Color(0xffd3d3de),
    ink: Color(0xff16161d),
    inkSoft: Color(0xff5a5b68),
    muted: Color(0xff8a8b9a),
    onInk: Color(0xffffffff),
    accent: Color(0xff5b4ef0),
    accentDeep: Color(0xff4338ca),
    accentSoft: Color(0xffeeedfe),
    violet: Color(0xff7c5cf0),
    sky: Color(0xff3b82f6),
    rose: Color(0xffb8a9f5),
    success: Color(0xff5b4ef0),
    successSoft: Color(0xffeeedfe),
    warning: Color(0xffb45309),
    warningSoft: Color(0xfffcf3e6),
    danger: Color(0xffdc2626),
    dangerSoft: Color(0xfffcebeb),
    info: Color(0xff2563eb),
    infoSoft: Color(0xffe7eefd),
  );

  static const dark = JarvisColors(
    brightness: Brightness.dark,
    canvas: Color(0xff0f0f14),
    surface: Color(0xff18181f),
    surfaceMuted: Color(0xff202029),
    surfaceRaised: Color(0xff2a2a35),
    outline: Color(0xff26262f),
    outlineStrong: Color(0xff363642),
    ink: Color(0xfff2f2f7),
    inkSoft: Color(0xffa9aab8),
    muted: Color(0xff7c7d8d),
    onInk: Color(0xff0f0f14),
    accent: Color(0xff8f86ff),
    accentDeep: Color(0xffc4c0ff),
    accentSoft: Color(0xff221f45),
    violet: Color(0xffa78bfa),
    sky: Color(0xff7aa2ff),
    rose: Color(0xffc9bdfb),
    success: Color(0xff8f86ff),
    successSoft: Color(0xff221f45),
    warning: Color(0xfffbbf24),
    warningSoft: Color(0xff3a2a12),
    danger: Color(0xfff87171),
    dangerSoft: Color(0xff3a1c1c),
    info: Color(0xff7aa2ff),
    infoSoft: Color(0xff1a2347),
  );

  @override
  JarvisColors copyWith({
    Brightness? brightness,
    Color? canvas,
    Color? surface,
    Color? surfaceMuted,
    Color? surfaceRaised,
    Color? outline,
    Color? outlineStrong,
    Color? ink,
    Color? inkSoft,
    Color? muted,
    Color? onInk,
    Color? accent,
    Color? accentDeep,
    Color? accentSoft,
    Color? violet,
    Color? sky,
    Color? rose,
    Color? success,
    Color? successSoft,
    Color? warning,
    Color? warningSoft,
    Color? danger,
    Color? dangerSoft,
    Color? info,
    Color? infoSoft,
  }) => JarvisColors(
    brightness: brightness ?? this.brightness,
    canvas: canvas ?? this.canvas,
    surface: surface ?? this.surface,
    surfaceMuted: surfaceMuted ?? this.surfaceMuted,
    surfaceRaised: surfaceRaised ?? this.surfaceRaised,
    outline: outline ?? this.outline,
    outlineStrong: outlineStrong ?? this.outlineStrong,
    ink: ink ?? this.ink,
    inkSoft: inkSoft ?? this.inkSoft,
    muted: muted ?? this.muted,
    onInk: onInk ?? this.onInk,
    accent: accent ?? this.accent,
    accentDeep: accentDeep ?? this.accentDeep,
    accentSoft: accentSoft ?? this.accentSoft,
    violet: violet ?? this.violet,
    sky: sky ?? this.sky,
    rose: rose ?? this.rose,
    success: success ?? this.success,
    successSoft: successSoft ?? this.successSoft,
    warning: warning ?? this.warning,
    warningSoft: warningSoft ?? this.warningSoft,
    danger: danger ?? this.danger,
    dangerSoft: dangerSoft ?? this.dangerSoft,
    info: info ?? this.info,
    infoSoft: infoSoft ?? this.infoSoft,
  );

  @override
  JarvisColors lerp(ThemeExtension<JarvisColors>? other, double t) {
    if (other is! JarvisColors) return this;
    Color mix(Color a, Color b) => Color.lerp(a, b, t)!;
    return JarvisColors(
      brightness: t < .5 ? brightness : other.brightness,
      canvas: mix(canvas, other.canvas),
      surface: mix(surface, other.surface),
      surfaceMuted: mix(surfaceMuted, other.surfaceMuted),
      surfaceRaised: mix(surfaceRaised, other.surfaceRaised),
      outline: mix(outline, other.outline),
      outlineStrong: mix(outlineStrong, other.outlineStrong),
      ink: mix(ink, other.ink),
      inkSoft: mix(inkSoft, other.inkSoft),
      muted: mix(muted, other.muted),
      onInk: mix(onInk, other.onInk),
      accent: mix(accent, other.accent),
      accentDeep: mix(accentDeep, other.accentDeep),
      accentSoft: mix(accentSoft, other.accentSoft),
      violet: mix(violet, other.violet),
      sky: mix(sky, other.sky),
      rose: mix(rose, other.rose),
      success: mix(success, other.success),
      successSoft: mix(successSoft, other.successSoft),
      warning: mix(warning, other.warning),
      warningSoft: mix(warningSoft, other.warningSoft),
      danger: mix(danger, other.danger),
      dangerSoft: mix(dangerSoft, other.dangerSoft),
      info: mix(info, other.info),
      infoSoft: mix(infoSoft, other.infoSoft),
    );
  }
}

class JarvisRadii {
  static const sm = 8.0;
  static const md = 14.0;
  static const lg = 20.0;
  static const xl = 26.0;
}

/// Geist for everything. [display] is the tight, semibold style used for page
/// titles and headlines; [clock] is the thin large numeral on Home.
class JarvisType {
  static TextStyle display([Color color = const Color(0xff16161d)]) =>
      TextStyle(
        fontFamily: 'Geist',
        color: color,
        fontWeight: FontWeight.w600,
        letterSpacing: -.9,
        height: 1.1,
      );

  static TextStyle displayOf(BuildContext context) =>
      display(JarvisColors.of(context).ink);

  static TextStyle clock(Color color) => TextStyle(
    fontFamily: 'Geist',
    color: color,
    fontWeight: FontWeight.w300,
    fontSize: 64,
    letterSpacing: -3.6,
    height: 1,
    fontFeatures: const [FontFeature.tabularFigures()],
  );
}

class JarvisShadows {
  static List<BoxShadow> soft([Brightness brightness = Brightness.light]) =>
      brightness == Brightness.dark
      ? const [
          BoxShadow(
            color: Color(0x66000000),
            blurRadius: 2,
            offset: Offset(0, 1),
          ),
          BoxShadow(
            color: Color(0x3d000000),
            blurRadius: 16,
            offset: Offset(0, 6),
          ),
        ]
      : const [
          BoxShadow(
            color: Color(0x08111113),
            blurRadius: 2,
            offset: Offset(0, 1),
          ),
          BoxShadow(
            color: Color(0x0a111113),
            blurRadius: 12,
            offset: Offset(0, 4),
          ),
        ];

  /// Barely-there lift for small controls on the canvas.
  static List<BoxShadow> hairline([Brightness brightness = Brightness.light]) =>
      brightness == Brightness.dark
      ? const [
          BoxShadow(
            color: Color(0x40000000),
            blurRadius: 3,
            offset: Offset(0, 1),
          ),
        ]
      : const [
          BoxShadow(
            color: Color(0x0c111113),
            blurRadius: 3,
            offset: Offset(0, 1),
          ),
          BoxShadow(
            color: Color(0x06111113),
            blurRadius: 10,
            offset: Offset(0, 4),
          ),
        ];

  static List<BoxShadow> floating([Brightness brightness = Brightness.light]) =>
      brightness == Brightness.dark
      ? const [
          BoxShadow(
            color: Color(0x73000000),
            blurRadius: 2,
            offset: Offset(0, 1),
          ),
          BoxShadow(
            color: Color(0x52000000),
            blurRadius: 28,
            offset: Offset(0, 10),
          ),
        ]
      : const [
          BoxShadow(
            color: Color(0x0a111113),
            blurRadius: 2,
            offset: Offset(0, 1),
          ),
          BoxShadow(
            color: Color(0x10111113),
            blurRadius: 24,
            offset: Offset(0, 8),
          ),
        ];
}

const _fontFamily = 'Geist';

TextTheme _textTheme(JarvisColors colors) {
  final base = TextStyle(
    fontFamily: _fontFamily,
    color: colors.ink,
    letterSpacing: -.1,
  );
  return TextTheme(
    displaySmall: base.copyWith(
      fontSize: 34,
      fontWeight: FontWeight.w600,
      letterSpacing: -1.2,
      height: 1.12,
    ),
    headlineMedium: base.copyWith(
      fontSize: 28,
      fontWeight: FontWeight.w600,
      letterSpacing: -.9,
      height: 1.15,
    ),
    headlineSmall: base.copyWith(
      fontSize: 22,
      fontWeight: FontWeight.w600,
      letterSpacing: -.6,
      height: 1.2,
    ),
    titleLarge: base.copyWith(
      fontSize: 18,
      fontWeight: FontWeight.w600,
      letterSpacing: -.4,
    ),
    titleMedium: base.copyWith(
      fontSize: 15.5,
      fontWeight: FontWeight.w600,
      letterSpacing: -.25,
    ),
    titleSmall: base.copyWith(
      fontSize: 14.5,
      fontWeight: FontWeight.w500,
      letterSpacing: -.15,
    ),
    bodyLarge: base.copyWith(fontSize: 16, height: 1.5),
    bodyMedium: base.copyWith(fontSize: 14.5, height: 1.45),
    bodySmall: base.copyWith(
      fontSize: 12.5,
      height: 1.4,
      color: colors.inkSoft,
    ),
    labelLarge: base.copyWith(fontSize: 14, fontWeight: FontWeight.w500),
    labelMedium: base.copyWith(fontSize: 12.5, fontWeight: FontWeight.w600),
    labelSmall: base.copyWith(
      fontSize: 11,
      fontWeight: FontWeight.w500,
      letterSpacing: .4,
      color: colors.muted,
    ),
  );
}

ThemeData buildJarvisTheme({Brightness brightness = Brightness.light}) {
  final colors = brightness == Brightness.dark
      ? JarvisColors.dark
      : JarvisColors.light;
  final scheme =
      ColorScheme.fromSeed(
        seedColor: colors.accent,
        brightness: brightness,
      ).copyWith(
        primary: colors.accent,
        onPrimary: Colors.white,
        primaryContainer: colors.accentSoft,
        onPrimaryContainer: colors.accentDeep,
        secondary: colors.violet,
        surface: colors.surface,
        onSurface: colors.ink,
        onSurfaceVariant: colors.inkSoft,
        surfaceContainerLowest: colors.surface,
        surfaceContainerLow: colors.canvas,
        surfaceContainer: colors.surfaceMuted,
        surfaceContainerHigh: colors.surfaceMuted,
        surfaceContainerHighest: colors.surfaceRaised,
        error: colors.danger,
        onError: Colors.white,
        outline: colors.outlineStrong,
        outlineVariant: colors.outline,
        surfaceTint: Colors.transparent,
      );
  final text = _textTheme(colors);
  final buttonShape = RoundedRectangleBorder(
    borderRadius: BorderRadius.circular(JarvisRadii.md),
  );
  const buttonText = TextStyle(
    fontFamily: _fontFamily,
    fontSize: 14,
    fontWeight: FontWeight.w500,
    letterSpacing: -.1,
  );
  final fieldBorder = OutlineInputBorder(
    borderRadius: BorderRadius.circular(JarvisRadii.md),
    borderSide: BorderSide(color: colors.outline),
  );
  final overlay = colors.isDark
      ? SystemUiOverlayStyle.light.copyWith(statusBarColor: Colors.transparent)
      : SystemUiOverlayStyle.dark.copyWith(statusBarColor: Colors.transparent);

  return ThemeData(
    brightness: brightness,
    useMaterial3: true,
    fontFamily: _fontFamily,
    colorScheme: scheme,
    textTheme: text,
    scaffoldBackgroundColor: colors.canvas,
    splashFactory: InkRipple.splashFactory,
    splashColor: colors.ink.withValues(alpha: .04),
    hoverColor: colors.ink.withValues(alpha: .03),
    highlightColor: colors.ink.withValues(alpha: .04),
    extensions: [colors],
    actionIconTheme: ActionIconThemeData(
      backButtonIconBuilder: (_) =>
          const _RoundIcon(PhosphorIconsRegular.arrowLeft),
      closeButtonIconBuilder: (_) =>
          const Icon(PhosphorIconsRegular.x, size: 22),
    ),
    visualDensity: VisualDensity.standard,
    pageTransitionsTheme: const PageTransitionsTheme(
      builders: {
        TargetPlatform.android: JarvisPageTransitionsBuilder(),
        // iOS keeps the native slide so the edge swipe back still works.
        TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
        TargetPlatform.macOS: JarvisPageTransitionsBuilder(),
        TargetPlatform.linux: JarvisPageTransitionsBuilder(),
        TargetPlatform.windows: JarvisPageTransitionsBuilder(),
        TargetPlatform.fuchsia: JarvisPageTransitionsBuilder(),
      },
    ),
    appBarTheme: AppBarTheme(
      backgroundColor: colors.canvas,
      foregroundColor: colors.ink,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleSpacing: 20,
      systemOverlayStyle: overlay,
      iconTheme: IconThemeData(color: colors.ink, size: 20),
      actionsIconTheme: IconThemeData(color: colors.inkSoft, size: 20),
      // Page titles use the tight display style so every screen carries the
      // same voice.
      titleTextStyle: JarvisType.display(colors.ink).copyWith(fontSize: 26),
    ),
    iconTheme: IconThemeData(color: colors.inkSoft, size: 20),
    iconButtonTheme: IconButtonThemeData(
      style: IconButton.styleFrom(
        iconSize: 20,
        foregroundColor: colors.inkSoft,
        highlightColor: colors.ink.withValues(alpha: .05),
      ),
    ),
    cardTheme: CardThemeData(
      color: colors.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      margin: const EdgeInsets.only(bottom: 10),
      clipBehavior: Clip.antiAlias,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.lg),
        side: BorderSide(color: colors.outline),
      ),
    ),
    listTileTheme: ListTileThemeData(
      iconColor: colors.inkSoft,
      textColor: colors.ink,
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 2),
      minVerticalPadding: 12,
      horizontalTitleGap: 14,
      titleTextStyle: text.titleSmall?.copyWith(fontSize: 15),
      subtitleTextStyle: text.bodySmall?.copyWith(fontSize: 13),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.lg),
      ),
      selectedColor: colors.ink,
      selectedTileColor: colors.surfaceMuted,
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: colors.ink,
        foregroundColor: colors.onInk,
        disabledBackgroundColor: colors.surfaceRaised,
        disabledForegroundColor: colors.muted,
        minimumSize: const Size(64, 44),
        padding: const EdgeInsets.symmetric(horizontal: 18),
        shape: buttonShape,
        textStyle: buttonText,
        elevation: 0,
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: colors.ink,
        backgroundColor: colors.surface,
        minimumSize: const Size(64, 44),
        padding: const EdgeInsets.symmetric(horizontal: 16),
        side: BorderSide(color: colors.outline),
        shape: buttonShape,
        textStyle: buttonText,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: colors.ink,
        textStyle: buttonText,
        shape: buttonShape,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      ),
    ),
    floatingActionButtonTheme: FloatingActionButtonThemeData(
      backgroundColor: colors.ink,
      foregroundColor: colors.onInk,
      elevation: 0,
      focusElevation: 0,
      hoverElevation: 2,
      highlightElevation: 0,
      extendedTextStyle: buttonText,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.lg),
      ),
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: colors.surface,
      surfaceTintColor: Colors.transparent,
      indicatorColor: colors.accentSoft,
      labelTextStyle: WidgetStateProperty.resolveWith(
        (states) => TextStyle(
          fontFamily: _fontFamily,
          fontSize: 12,
          fontWeight: states.contains(WidgetState.selected)
              ? FontWeight.w600
              : FontWeight.w500,
          color: states.contains(WidgetState.selected)
              ? colors.ink
              : colors.muted,
        ),
      ),
    ),
    navigationRailTheme: NavigationRailThemeData(
      backgroundColor: colors.surface,
      indicatorColor: colors.surfaceMuted,
      indicatorShape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
      ),
      selectedIconTheme: IconThemeData(color: colors.ink),
      unselectedIconTheme: IconThemeData(color: colors.muted),
      selectedLabelTextStyle: text.labelMedium?.copyWith(color: colors.ink),
      unselectedLabelTextStyle: text.labelMedium?.copyWith(
        color: colors.muted,
        fontWeight: FontWeight.w500,
      ),
    ),
    tabBarTheme: TabBarThemeData(
      labelColor: colors.ink,
      unselectedLabelColor: colors.muted,
      labelStyle: text.labelLarge,
      unselectedLabelStyle: text.labelLarge?.copyWith(
        fontWeight: FontWeight.w500,
      ),
      indicatorSize: TabBarIndicatorSize.tab,
      dividerColor: Colors.transparent,
      indicator: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(JarvisRadii.sm),
        boxShadow: JarvisShadows.soft(brightness),
      ),
      overlayColor: WidgetStateProperty.all(Colors.transparent),
    ),
    chipTheme: ChipThemeData(
      backgroundColor: colors.surface,
      selectedColor: colors.surfaceRaised,
      secondarySelectedColor: colors.surfaceRaised,
      checkmarkColor: colors.ink,
      side: BorderSide(color: colors.outline),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(40)),
      labelStyle: TextStyle(
        fontFamily: _fontFamily,
        fontSize: 13,
        fontWeight: FontWeight.w500,
        color: colors.inkSoft,
      ),
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 6),
      showCheckmark: false,
    ),
    dividerTheme: DividerThemeData(
      color: colors.outline,
      thickness: 1,
      space: 1,
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(
      color: colors.ink,
      linearTrackColor: colors.surfaceRaised,
      circularTrackColor: Colors.transparent,
      borderRadius: const BorderRadius.all(Radius.circular(4)),
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.all(colors.surface),
      trackColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? colors.ink
            : colors.outlineStrong,
      ),
      trackOutlineColor: WidgetStateProperty.all(Colors.transparent),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: colors.surface,
      barrierColor: colors.scrim,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.xl),
      ),
      titleTextStyle: JarvisType.display(colors.ink).copyWith(fontSize: 26),
      contentTextStyle: text.bodyMedium?.copyWith(color: colors.inkSoft),
      actionsPadding: const EdgeInsets.fromLTRB(20, 4, 20, 20),
    ),
    bottomSheetTheme: BottomSheetThemeData(
      backgroundColor: colors.surface,
      surfaceTintColor: Colors.transparent,
      showDragHandle: true,
      dragHandleColor: colors.outlineStrong,
      modalBarrierColor: colors.scrim,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(JarvisRadii.xl),
        ),
      ),
    ),
    popupMenuTheme: PopupMenuThemeData(
      color: colors.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 8,
      shadowColor: colors.isDark
          ? const Color(0x66000000)
          : const Color(0x22111113),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
        side: BorderSide(color: colors.outline),
      ),
      textStyle: text.bodyMedium,
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: colors.ink,
      contentTextStyle: text.bodyMedium?.copyWith(color: colors.onInk),
      actionTextColor: colors.onInk,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
      ),
    ),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: colors.ink,
        borderRadius: BorderRadius.circular(8),
      ),
      textStyle: TextStyle(
        fontFamily: _fontFamily,
        color: colors.onInk,
        fontSize: 12,
        fontWeight: FontWeight.w500,
      ),
      waitDuration: const Duration(milliseconds: 400),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: colors.surface,
      hintStyle: text.bodyMedium?.copyWith(color: colors.muted),
      labelStyle: text.bodyMedium?.copyWith(color: colors.inkSoft),
      floatingLabelStyle: WidgetStateTextStyle.resolveWith(
        (states) => (text.bodyMedium ?? const TextStyle()).copyWith(
          color: states.contains(WidgetState.error)
              ? colors.danger
              : states.contains(WidgetState.focused)
              ? colors.accent
              : colors.inkSoft,
          fontWeight: FontWeight.w500,
        ),
      ),
      helperStyle: text.bodySmall,
      prefixIconColor: colors.muted,
      suffixIconColor: colors.muted,
      border: fieldBorder,
      enabledBorder: fieldBorder,
      disabledBorder: fieldBorder,
      // Focus reads as the accent, like the chat composer, not a heavy black.
      focusedBorder: fieldBorder.copyWith(
        borderSide: BorderSide(color: colors.accent, width: 1.5),
      ),
      errorBorder: fieldBorder.copyWith(
        borderSide: BorderSide(color: colors.danger),
      ),
      focusedErrorBorder: fieldBorder.copyWith(
        borderSide: BorderSide(color: colors.danger, width: 1.5),
      ),
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
    ),
    textSelectionTheme: TextSelectionThemeData(
      cursorColor: colors.accent,
      selectionColor: colors.accent.withValues(alpha: .18),
      selectionHandleColor: colors.accent,
    ),
  );
}

/// Back/close glyph drawn on a soft disc, matching the shell's round
/// top bar buttons.
class _RoundIcon extends StatelessWidget {
  const _RoundIcon(this.icon);

  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      width: 36,
      height: 36,
      decoration: BoxDecoration(
        color: colors.surface,
        shape: BoxShape.circle,
        border: Border.all(color: colors.outline),
        boxShadow: JarvisShadows.soft(colors.brightness),
      ),
      child: Icon(icon, size: 18, color: colors.ink),
    );
  }
}
