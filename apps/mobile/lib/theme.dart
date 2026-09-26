import 'package:flutter/cupertino.dart' show CupertinoPageTransitionsBuilder;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'ui/phosphor_icons.dart';

class JarvisColors {
  static const canvas = Color(0xfff8f8f9);
  static const background = canvas;
  static const surface = Color(0xffffffff);
  static const surfaceMuted = Color(0xfff3f3f5);
  static const surfaceRaised = Color(0xffececef);
  static const outline = Color(0xffe8e8eb);
  static const outlineStrong = Color(0xffd8d8dd);

  static const ink = Color(0xff111113);
  static const inkSoft = Color(0xff55555e);
  static const muted = Color(0xff8e8e98);

  static const accent = Color(0xff4f46e5);
  static const accentDeep = Color(0xff3730a3);
  static const accentSoft = Color(0xffeef0ff);
  static const violet = Color(0xff8b7cf6);
  static const sky = Color(0xff38bdf8);
  static const rose = Color(0xfff0a6c8);

  static const success = Color(0xff16a34a);
  static const successSoft = Color(0xffeaf6ee);
  static const warning = Color(0xffd97706);
  static const warningSoft = Color(0xfffcf4e6);
  static const danger = Color(0xffdc2626);
  static const dangerSoft = Color(0xfffcebeb);
  static const info = Color(0xff2563eb);
  static const infoSoft = Color(0xffecf2fe);
}

class JarvisRadii {
  static const sm = 8.0;
  static const md = 12.0;
  static const lg = 16.0;
  static const xl = 22.0;
}

class JarvisShadows {
  static const soft = [
    BoxShadow(color: Color(0x08111113), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(color: Color(0x0a111113), blurRadius: 12, offset: Offset(0, 4)),
  ];

  static const floating = [
    BoxShadow(color: Color(0x0a111113), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(color: Color(0x10111113), blurRadius: 24, offset: Offset(0, 8)),
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
      color: JarvisColors.inkSoft,
    ),
    labelLarge: base.copyWith(fontSize: 14, fontWeight: FontWeight.w500),
    labelMedium: base.copyWith(fontSize: 12.5, fontWeight: FontWeight.w600),
    labelSmall: base.copyWith(
      fontSize: 11,
      fontWeight: FontWeight.w500,
      letterSpacing: .4,
      color: JarvisColors.muted,
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
    fontSize: 14,
    fontWeight: FontWeight.w500,
    letterSpacing: -.1,
  );
  final fieldBorder = OutlineInputBorder(
    borderRadius: BorderRadius.circular(JarvisRadii.md),
    borderSide: const BorderSide(color: JarvisColors.outline),
  );

  return ThemeData(
    brightness: Brightness.light,
    useMaterial3: true,
    fontFamily: _fontFamily,
    colorScheme: scheme,
    textTheme: text,
    scaffoldBackgroundColor: JarvisColors.canvas,
    splashFactory: InkRipple.splashFactory,
    splashColor: JarvisColors.ink.withValues(alpha: .04),
    hoverColor: JarvisColors.ink.withValues(alpha: .03),
    highlightColor: JarvisColors.ink.withValues(alpha: .04),
    actionIconTheme: ActionIconThemeData(
      backButtonIconBuilder: (_) =>
          const Icon(PhosphorIconsRegular.arrowLeft, size: 22),
      closeButtonIconBuilder: (_) =>
          const Icon(PhosphorIconsRegular.x, size: 22),
    ),
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
      iconTheme: const IconThemeData(color: JarvisColors.ink, size: 20),
      actionsIconTheme: const IconThemeData(
        color: JarvisColors.inkSoft,
        size: 20,
      ),
      titleTextStyle: text.titleLarge,
    ),
    iconTheme: const IconThemeData(color: JarvisColors.inkSoft, size: 20),
    iconButtonTheme: IconButtonThemeData(
      style: IconButton.styleFrom(
        iconSize: 20,
        foregroundColor: JarvisColors.inkSoft,
        highlightColor: JarvisColors.ink.withValues(alpha: .05),
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
      selectedColor: JarvisColors.ink,
      selectedTileColor: JarvisColors.surfaceMuted,
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: JarvisColors.ink,
        foregroundColor: Colors.white,
        disabledBackgroundColor: JarvisColors.surfaceRaised,
        disabledForegroundColor: JarvisColors.muted,
        minimumSize: const Size(64, 44),
        padding: const EdgeInsets.symmetric(horizontal: 18),
        shape: buttonShape,
        textStyle: buttonText,
        elevation: 0,
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: JarvisColors.ink,
        backgroundColor: JarvisColors.surface,
        minimumSize: const Size(64, 44),
        padding: const EdgeInsets.symmetric(horizontal: 16),
        side: const BorderSide(color: JarvisColors.outline),
        shape: buttonShape,
        textStyle: buttonText,
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: JarvisColors.ink,
        textStyle: buttonText,
        shape: buttonShape,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
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
      indicatorColor: JarvisColors.surfaceMuted,
      indicatorShape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(JarvisRadii.md),
      ),
      selectedIconTheme: const IconThemeData(color: JarvisColors.ink),
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
      selectedColor: JarvisColors.surfaceRaised,
      secondarySelectedColor: JarvisColors.surfaceRaised,
      checkmarkColor: JarvisColors.ink,
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
      color: JarvisColors.ink,
      linearTrackColor: JarvisColors.surfaceRaised,
      circularTrackColor: Colors.transparent,
      borderRadius: BorderRadius.all(Radius.circular(4)),
    ),
    switchTheme: SwitchThemeData(
      thumbColor: WidgetStateProperty.all(Colors.white),
      trackColor: WidgetStateProperty.resolveWith(
        (states) => states.contains(WidgetState.selected)
            ? JarvisColors.ink
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
        borderRadius: BorderRadius.vertical(top: Radius.circular(22)),
      ),
    ),
    popupMenuTheme: PopupMenuThemeData(
      color: JarvisColors.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 8,
      shadowColor: const Color(0x22111113),
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
      actionTextColor: Colors.white,
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
      fillColor: JarvisColors.surface,
      hintStyle: text.bodyMedium?.copyWith(color: JarvisColors.muted),
      labelStyle: text.bodyMedium?.copyWith(color: JarvisColors.inkSoft),
      floatingLabelStyle: text.bodyMedium?.copyWith(
        color: JarvisColors.ink,
        fontWeight: FontWeight.w500,
      ),
      helperStyle: text.bodySmall,
      prefixIconColor: JarvisColors.muted,
      suffixIconColor: JarvisColors.muted,
      border: fieldBorder,
      enabledBorder: fieldBorder,
      disabledBorder: fieldBorder,
      focusedBorder: fieldBorder.copyWith(
        borderSide: const BorderSide(color: JarvisColors.ink, width: 1.2),
      ),
      errorBorder: fieldBorder.copyWith(
        borderSide: const BorderSide(color: JarvisColors.danger),
      ),
      focusedErrorBorder: fieldBorder.copyWith(
        borderSide: const BorderSide(color: JarvisColors.danger, width: 1.5),
      ),
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
    ),
    textSelectionTheme: TextSelectionThemeData(
      cursorColor: JarvisColors.accent,
      selectionColor: JarvisColors.accent.withValues(alpha: .18),
      selectionHandleColor: JarvisColors.accent,
    ),
  );
}
