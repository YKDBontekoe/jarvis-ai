import 'package:flutter/cupertino.dart' show CupertinoPageTransitionsBuilder;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

class JarvisColors {
  static const canvas = Color(0xfff6f7fb);
  static const background = canvas;
  static const surface = Color(0xffffffff);
  static const surfaceMuted = Color(0xfff1f2f7);
  static const surfaceRaised = Color(0xffeceef5);
  static const outline = Color(0xffe6e8f0);
  static const outlineStrong = Color(0xffd4d7e2);

  static const ink = Color(0xff0e1020);
  static const inkSoft = Color(0xff545a73);
  static const muted = Color(0xff8a90a6);

  static const accent = Color(0xff5b50f0);
  static const accentDeep = Color(0xff4338ca);
  static const accentSoft = Color(0xffeeedff);
  static const violet = Color(0xff9d7bff);
  static const sky = Color(0xff38bdf8);
  static const rose = Color(0xfff472b6);

  static const success = Color(0xff0fa968);
  static const successSoft = Color(0xffe4f6ed);
  static const warning = Color(0xffd98200);
  static const warningSoft = Color(0xfffff3da);
  static const danger = Color(0xffe5484d);
  static const dangerSoft = Color(0xfffdecec);
  static const info = Color(0xff2f7bf5);
  static const infoSoft = Color(0xffe8f1ff);

  static const userBubbleGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xff6a5cff), Color(0xff4f46e5)],
  );

  static const brandGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xff6a5cff), Color(0xff9d7bff), Color(0xff38bdf8)],
  );
}

class JarvisRadii {
  static const sm = 10.0;
  static const md = 14.0;
  static const lg = 20.0;
  static const xl = 28.0;
}

class JarvisShadows {
  static const soft = [
    BoxShadow(color: Color(0x0a0e1020), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(color: Color(0x0f0e1020), blurRadius: 24, offset: Offset(0, 8)),
  ];

  static const floating = [
    BoxShadow(color: Color(0x0d0e1020), blurRadius: 3, offset: Offset(0, 1)),
    BoxShadow(color: Color(0x1a1c1d4a), blurRadius: 40, offset: Offset(0, 16)),
  ];

  static List<BoxShadow> glow(Color color, {double strength = .35}) => [
    BoxShadow(
      color: color.withValues(alpha: strength),
      blurRadius: 24,
      offset: const Offset(0, 10),
    ),
  ];
}

const _fontFamily = 'Inter';

TextTheme _textTheme() {
  const base = TextStyle(
    fontFamily: _fontFamily,
    color: JarvisColors.ink,
    letterSpacing: -.1,
  );
  return TextTheme(
    displaySmall: base.copyWith(
      fontSize: 34,
      fontWeight: FontWeight.w700,
      letterSpacing: -1.1,
      height: 1.12,
    ),
    headlineMedium: base.copyWith(
      fontSize: 30,
      fontWeight: FontWeight.w700,
      letterSpacing: -.9,
      height: 1.15,
    ),
    headlineSmall: base.copyWith(
      fontSize: 24,
      fontWeight: FontWeight.w700,
      letterSpacing: -.6,
      height: 1.2,
    ),
    titleLarge: base.copyWith(
      fontSize: 20,
      fontWeight: FontWeight.w700,
      letterSpacing: -.4,
    ),
    titleMedium: base.copyWith(
      fontSize: 16,
      fontWeight: FontWeight.w600,
      letterSpacing: -.2,
    ),
    titleSmall: base.copyWith(fontSize: 14, fontWeight: FontWeight.w600),
    bodyLarge: base.copyWith(fontSize: 16, height: 1.5),
    bodyMedium: base.copyWith(fontSize: 14.5, height: 1.45),
    bodySmall: base.copyWith(
      fontSize: 12.5,
      height: 1.4,
      color: JarvisColors.inkSoft,
    ),
    labelLarge: base.copyWith(fontSize: 14.5, fontWeight: FontWeight.w600),
    labelMedium: base.copyWith(fontSize: 12.5, fontWeight: FontWeight.w600),
    labelSmall: base.copyWith(
      fontSize: 11,
      fontWeight: FontWeight.w600,
      letterSpacing: .6,
    ),
  );
}

ThemeData buildJarvisTheme() {
  final scheme =
      ColorScheme.fromSeed(
        seedColor: JarvisColors.accent,
        brightness: Brightness.light,
      ).copyWith(
        primary: JarvisColors.accent,
        onPrimary: Colors.white,
        primaryContainer: JarvisColors.accentSoft,
        onPrimaryContainer: JarvisColors.accentDeep,
        secondary: JarvisColors.violet,
        surface: JarvisColors.surface,
        onSurface: JarvisColors.ink,
        onSurfaceVariant: JarvisColors.inkSoft,
        surfaceContainerLowest: JarvisColors.surface,
        surfaceContainerLow: JarvisColors.canvas,
        surfaceContainer: JarvisColors.surfaceMuted,
        surfaceContainerHigh: JarvisColors.surfaceMuted,
        surfaceContainerHighest: JarvisColors.surfaceRaised,
        error: JarvisColors.danger,
        outline: JarvisColors.outlineStrong,
        outlineVariant: JarvisColors.outline,
        surfaceTint: Colors.transparent,
      );
  final text = _textTheme();
  final buttonShape = RoundedRectangleBorder(
    borderRadius: BorderRadius.circular(JarvisRadii.md),
  );
  const buttonText = TextStyle(
    fontFamily: _fontFamily,
    fontSize: 14.5,
    fontWeight: FontWeight.w600,
    letterSpacing: -.1,
  );
  // Underline borders keep floating labels inside the filled, rounded field.
  final fieldBorder = UnderlineInputBorder(
    borderRadius: BorderRadius.circular(JarvisRadii.md),
    borderSide: BorderSide.none,
  );

  return ThemeData(
    brightness: Brightness.light,
    useMaterial3: true,
    fontFamily: _fontFamily,
    colorScheme: scheme,
    textTheme: text,
    scaffoldBackgroundColor: JarvisColors.canvas,
    splashFactory: InkSparkle.splashFactory,
    hoverColor: JarvisColors.accent.withValues(alpha: .04),
    highlightColor: JarvisColors.accent.withValues(alpha: .06),
    visualDensity: VisualDensity.standard,
    pageTransitionsTheme: const PageTransitionsTheme(
      builders: {
        TargetPlatform.android: FadeForwardsPageTransitionsBuilder(),
        TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
        TargetPlatform.macOS: CupertinoPageTransitionsBuilder(),
        TargetPlatform.linux: FadeForwardsPageTransitionsBuilder(),
        TargetPlatform.windows: FadeForwardsPageTransitionsBuilder(),
      },
    ),
    appBarTheme: AppBarTheme(
      backgroundColor: JarvisColors.canvas,
      foregroundColor: JarvisColors.ink,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleSpacing: 20,
      systemOverlayStyle: SystemUiOverlayStyle.dark.copyWith(
        statusBarColor: Colors.transparent,
      ),
      iconTheme: const IconThemeData(color: JarvisColors.ink, size: 22),
      actionsIconTheme: const IconThemeData(
        color: JarvisColors.inkSoft,
        size: 22,
      ),
      titleTextStyle: text.titleLarge,
    ),
    iconTheme: const IconThemeData(color: JarvisColors.inkSoft),
    iconButtonTheme: IconButtonThemeData(
      style: IconButton.styleFrom(
        foregroundColor: JarvisColors.inkSoft,
        highlightColor: JarvisColors.accent.withValues(alpha: .08),
      ),
    ),
    cardTheme: CardThemeData(
      color: JarvisColors.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      margin: const EdgeInsets.only(bottom: 10),
      clipBehavior: Clip.antiAlias,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.lg),
        side: const BorderSide(color: JarvisColors.outline),
      ),
    ),
    listTileTheme: ListTileThemeData(
      iconColor: JarvisColors.inkSoft,
      textColor: JarvisColors.ink,
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 2),
      minVerticalPadding: 12,
      horizontalTitleGap: 14,
      titleTextStyle: text.titleSmall?.copyWith(fontSize: 15),
      subtitleTextStyle: text.bodySmall?.copyWith(fontSize: 13),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.lg),
      ),
      selectedColor: JarvisColors.accent,
      selectedTileColor: JarvisColors.accentSoft,
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: JarvisColors.accent,
        foregroundColor: Colors.white,
        disabledBackgroundColor: JarvisColors.surfaceRaised,
        disabledForegroundColor: JarvisColors.muted,
        minimumSize: const Size(64, 48),
        padding: const EdgeInsets.symmetric(horizontal: 22),
        shape: buttonShape,
        textStyle: buttonText,
        elevation: 0,
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: JarvisColors.ink,
        minimumSize: const Size(64, 46),
        padding: const EdgeInsets.symmetric(horizontal: 20),
        side: const BorderSide(color: JarvisColors.outlineStrong),
        shape: buttonShape,
        textStyle: buttonText,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: JarvisColors.accent,
        textStyle: buttonText,
        shape: buttonShape,
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      ),
    ),
    floatingActionButtonTheme: FloatingActionButtonThemeData(
      backgroundColor: JarvisColors.ink,
      foregroundColor: Colors.white,
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
      backgroundColor: JarvisColors.surface,
      surfaceTintColor: Colors.transparent,
      indicatorColor: JarvisColors.accentSoft,
      labelTextStyle: WidgetStateProperty.resolveWith(
        (states) => TextStyle(
          fontFamily: _fontFamily,
          fontSize: 12,
          fontWeight: states.contains(WidgetState.selected)
              ? FontWeight.w600
              : FontWeight.w500,
          color: states.contains(WidgetState.selected)
              ? JarvisColors.ink
              : JarvisColors.muted,
        ),
      ),
    ),
    navigationRailTheme: NavigationRailThemeData(
      backgroundColor: JarvisColors.surface,
      indicatorColor: JarvisColors.accentSoft,
      indicatorShape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
      ),
      selectedIconTheme: const IconThemeData(color: JarvisColors.accent),
      unselectedIconTheme: const IconThemeData(color: JarvisColors.muted),
      selectedLabelTextStyle: text.labelMedium?.copyWith(
        color: JarvisColors.ink,
      ),
      unselectedLabelTextStyle: text.labelMedium?.copyWith(
        color: JarvisColors.muted,
        fontWeight: FontWeight.w500,
      ),
    ),
    tabBarTheme: TabBarThemeData(
      labelColor: JarvisColors.ink,
      unselectedLabelColor: JarvisColors.muted,
      labelStyle: text.labelLarge,
      unselectedLabelStyle: text.labelLarge?.copyWith(
        fontWeight: FontWeight.w500,
      ),
      indicatorSize: TabBarIndicatorSize.tab,
      dividerColor: Colors.transparent,
      indicator: BoxDecoration(
        color: JarvisColors.surface,
        borderRadius: BorderRadius.circular(JarvisRadii.sm),
        boxShadow: JarvisShadows.soft,
      ),
      overlayColor: WidgetStateProperty.all(Colors.transparent),
    ),
    chipTheme: ChipThemeData(
      backgroundColor: JarvisColors.surface,
      selectedColor: JarvisColors.accentSoft,
      secondarySelectedColor: JarvisColors.accentSoft,
      checkmarkColor: JarvisColors.accent,
      side: const BorderSide(color: JarvisColors.outline),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(40)),
      labelStyle: const TextStyle(
        fontFamily: _fontFamily,
        fontSize: 13,
        fontWeight: FontWeight.w500,
        color: JarvisColors.inkSoft,
      ),
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 6),
      showCheckmark: false,
    ),
    dividerTheme: const DividerThemeData(
      color: JarvisColors.outline,
      thickness: 1,
      space: 1,
    ),
    progressIndicatorTheme: const ProgressIndicatorThemeData(
      color: JarvisColors.accent,
      linearTrackColor: JarvisColors.accentSoft,
      circularTrackColor: Colors.transparent,
      borderRadius: BorderRadius.all(Radius.circular(4)),
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.all(Colors.white),
      trackColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? JarvisColors.accent
            : JarvisColors.outlineStrong,
      ),
      trackOutlineColor: WidgetStateProperty.all(Colors.transparent),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: JarvisColors.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.xl),
      ),
      titleTextStyle: text.titleLarge,
      contentTextStyle: text.bodyMedium?.copyWith(color: JarvisColors.inkSoft),
      actionsPadding: const EdgeInsets.fromLTRB(20, 4, 20, 20),
    ),
    bottomSheetTheme: const BottomSheetThemeData(
      backgroundColor: JarvisColors.surface,
      surfaceTintColor: Colors.transparent,
      showDragHandle: true,
      dragHandleColor: JarvisColors.outlineStrong,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(28)),
      ),
    ),
    popupMenuTheme: PopupMenuThemeData(
      color: JarvisColors.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 8,
      shadowColor: const Color(0x331c1d4a),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
        side: const BorderSide(color: JarvisColors.outline),
      ),
      textStyle: text.bodyMedium,
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: JarvisColors.ink,
      contentTextStyle: text.bodyMedium?.copyWith(color: Colors.white),
      actionTextColor: const Color(0xffb9b2ff),
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
      ),
    ),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: JarvisColors.ink,
        borderRadius: BorderRadius.circular(8),
      ),
      textStyle: const TextStyle(
        fontFamily: _fontFamily,
        color: Colors.white,
        fontSize: 12,
        fontWeight: FontWeight.w500,
      ),
      waitDuration: const Duration(milliseconds: 400),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: JarvisColors.surfaceMuted,
      hintStyle: text.bodyMedium?.copyWith(color: JarvisColors.muted),
      labelStyle: text.bodyMedium?.copyWith(color: JarvisColors.inkSoft),
      floatingLabelStyle: text.bodyMedium?.copyWith(
        color: JarvisColors.accent,
        fontWeight: FontWeight.w600,
      ),
      helperStyle: text.bodySmall,
      prefixIconColor: JarvisColors.muted,
      suffixIconColor: JarvisColors.muted,
      border: fieldBorder,
      enabledBorder: fieldBorder,
      disabledBorder: fieldBorder,
      focusedBorder: fieldBorder.copyWith(
        borderSide: const BorderSide(color: JarvisColors.accent, width: 1.5),
      ),
      errorBorder: fieldBorder.copyWith(
        borderSide: const BorderSide(color: JarvisColors.danger),
      ),
      focusedErrorBorder: fieldBorder.copyWith(
        borderSide: const BorderSide(color: JarvisColors.danger, width: 1.5),
      ),
      contentPadding: const EdgeInsets.fromLTRB(16, 14, 16, 12),
    ),
    textSelectionTheme: TextSelectionThemeData(
      cursorColor: JarvisColors.accent,
      selectionColor: JarvisColors.accent.withValues(alpha: .18),
      selectionHandleColor: JarvisColors.accent,
    ),
  );
}
