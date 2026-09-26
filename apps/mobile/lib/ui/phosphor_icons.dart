import 'package:flutter/widgets.dart';

// Phosphor Icons (MIT, https://phosphoricons.com), bundled as fonts under
// assets/fonts. Only the glyphs the app uses are declared.

abstract final class PhosphorIconsRegular {
  static const alarm = IconData(0xe006, fontFamily: 'PhosphorRegular');
  static const arrowLeft = IconData(0xe058, fontFamily: 'PhosphorRegular');
  static const arrowSquareOut = IconData(0xe5de, fontFamily: 'PhosphorRegular');
  static const arrowUpRight = IconData(0xe092, fontFamily: 'PhosphorRegular');
  static const arrowsClockwise = IconData(
    0xe094,
    fontFamily: 'PhosphorRegular',
  );
  static const bell = IconData(0xe0ce, fontFamily: 'PhosphorRegular');
  static const bellSlash = IconData(0xe0d4, fontFamily: 'PhosphorRegular');
  static const bookmarkSimple = IconData(0xe0ea, fontFamily: 'PhosphorRegular');
  static const bracketsCurly = IconData(0xe860, fontFamily: 'PhosphorRegular');
  static const brain = IconData(0xe74e, fontFamily: 'PhosphorRegular');
  static const calendarBlank = IconData(0xe10a, fontFamily: 'PhosphorRegular');
  static const caretDown = IconData(0xe136, fontFamily: 'PhosphorRegular');
  static const caretRight = IconData(0xe13a, fontFamily: 'PhosphorRegular');
  static const chartLine = IconData(0xe154, fontFamily: 'PhosphorRegular');
  static const chatCircle = IconData(0xe168, fontFamily: 'PhosphorRegular');
  static const chatsCircle = IconData(0xe17e, fontFamily: 'PhosphorRegular');
  static const check = IconData(0xe182, fontFamily: 'PhosphorRegular');
  static const checkCircle = IconData(0xe184, fontFamily: 'PhosphorRegular');
  static const circle = IconData(0xe18a, fontFamily: 'PhosphorRegular');
  static const clock = IconData(0xe19a, fontFamily: 'PhosphorRegular');
  static const clockCounterClockwise = IconData(
    0xe1a0,
    fontFamily: 'PhosphorRegular',
  );
  static const cloudSlash = IconData(0xe1b6, fontFamily: 'PhosphorRegular');
  static const code = IconData(0xe1bc, fontFamily: 'PhosphorRegular');
  static const copy = IconData(0xe1ca, fontFamily: 'PhosphorRegular');
  static const dotsThree = IconData(0xe1fe, fontFamily: 'PhosphorRegular');
  static const eye = IconData(0xe220, fontFamily: 'PhosphorRegular');
  static const eyeSlash = IconData(0xe224, fontFamily: 'PhosphorRegular');
  static const fileImage = IconData(0xea24, fontFamily: 'PhosphorRegular');
  static const filePdf = IconData(0xe702, fontFamily: 'PhosphorRegular');
  static const fileText = IconData(0xe23a, fontFamily: 'PhosphorRegular');
  static const folderOpen = IconData(0xe256, fontFamily: 'PhosphorRegular');
  static const folderSimple = IconData(0xe25a, fontFamily: 'PhosphorRegular');
  static const gavel = IconData(0xea32, fontFamily: 'PhosphorRegular');
  static const gearSix = IconData(0xe272, fontFamily: 'PhosphorRegular');
  static const globeSimple = IconData(0xe28e, fontFamily: 'PhosphorRegular');
  static const heart = IconData(0xe2a8, fontFamily: 'PhosphorRegular');
  static const hourglassMedium = IconData(
    0xe2b8,
    fontFamily: 'PhosphorRegular',
  );
  static const house = IconData(0xe2c2, fontFamily: 'PhosphorRegular');
  static const info = IconData(0xe2ce, fontFamily: 'PhosphorRegular');
  static const key = IconData(0xe2d6, fontFamily: 'PhosphorRegular');
  static const lightbulb = IconData(0xe2dc, fontFamily: 'PhosphorRegular');
  static const linkBreak = IconData(0xe2e4, fontFamily: 'PhosphorRegular');
  static const linkSimple = IconData(0xe2e6, fontFamily: 'PhosphorRegular');
  static const list = IconData(0xe2f0, fontFamily: 'PhosphorRegular');
  static const listChecks = IconData(0xeadc, fontFamily: 'PhosphorRegular');
  static const lockSimple = IconData(0xe308, fontFamily: 'PhosphorRegular');
  static const magnifyingGlass = IconData(
    0xe30c,
    fontFamily: 'PhosphorRegular',
  );
  static const microphone = IconData(0xe326, fontFamily: 'PhosphorRegular');
  static const minusCircle = IconData(0xe32c, fontFamily: 'PhosphorRegular');
  static const notePencil = IconData(0xe34c, fontFamily: 'PhosphorRegular');
  static const notebook = IconData(0xe34e, fontFamily: 'PhosphorRegular');
  static const notepad = IconData(0xe63e, fontFamily: 'PhosphorRegular');
  static const paperclip = IconData(0xe39a, fontFamily: 'PhosphorRegular');
  static const pauseCircle = IconData(0xe3a0, fontFamily: 'PhosphorRegular');
  static const pencilSimple = IconData(0xe3b4, fontFamily: 'PhosphorRegular');
  static const plugsConnected = IconData(0xeb5a, fontFamily: 'PhosphorRegular');
  static const plus = IconData(0xe3d4, fontFamily: 'PhosphorRegular');
  static const prohibit = IconData(0xe3de, fontFamily: 'PhosphorRegular');
  static const pulse = IconData(0xe000, fontFamily: 'PhosphorRegular');
  static const pushPin = IconData(0xe3e2, fontFamily: 'PhosphorRegular');
  static const puzzlePiece = IconData(0xe596, fontFamily: 'PhosphorRegular');
  static const repeat = IconData(0xe3f6, fontFamily: 'PhosphorRegular');
  static const rocketLaunch = IconData(0xe3fe, fontFamily: 'PhosphorRegular');
  static const shieldCheck = IconData(0xe40c, fontFamily: 'PhosphorRegular');
  static const shieldWarning = IconData(0xe412, fontFamily: 'PhosphorRegular');
  static const signIn = IconData(0xe428, fontFamily: 'PhosphorRegular');
  static const signOut = IconData(0xe42a, fontFamily: 'PhosphorRegular');
  static const sparkle = IconData(0xe6a2, fontFamily: 'PhosphorRegular');
  static const stop = IconData(0xe46c, fontFamily: 'PhosphorRegular');
  static const stopCircle = IconData(0xe46e, fontFamily: 'PhosphorRegular');
  static const sunHorizon = IconData(0xe5b6, fontFamily: 'PhosphorRegular');
  static const table = IconData(0xe476, fontFamily: 'PhosphorRegular');
  static const timer = IconData(0xe492, fontFamily: 'PhosphorRegular');
  static const trash = IconData(0xe4a6, fontFamily: 'PhosphorRegular');
  static const uploadSimple = IconData(0xe4c0, fontFamily: 'PhosphorRegular');
  static const user = IconData(0xe4c2, fontFamily: 'PhosphorRegular');
  static const users = IconData(0xe4d6, fontFamily: 'PhosphorRegular');
  static const warningCircle = IconData(0xe4e2, fontFamily: 'PhosphorRegular');
  static const waveform = IconData(0xe802, fontFamily: 'PhosphorRegular');
  static const wifiSlash = IconData(0xe4f2, fontFamily: 'PhosphorRegular');
  static const x = IconData(0xe4f6, fontFamily: 'PhosphorRegular');
  static const xCircle = IconData(0xe4f8, fontFamily: 'PhosphorRegular');
}

abstract final class PhosphorIconsFill {
  static const chatCircle = IconData(0xe168, fontFamily: 'PhosphorFill');
  static const checkCircle = IconData(0xe184, fontFamily: 'PhosphorFill');
  static const pushPin = IconData(0xe3e2, fontFamily: 'PhosphorFill');
  static const shieldCheck = IconData(0xe40c, fontFamily: 'PhosphorFill');
}

abstract final class PhosphorIconsBold {
  static const arrowUp = IconData(0xe08e, fontFamily: 'PhosphorBold');
  static const waveform = IconData(0xe802, fontFamily: 'PhosphorBold');
}
