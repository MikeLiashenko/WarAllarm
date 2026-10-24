import 'package:flutter/material.dart';

/// WarAlarm design-system palette — "night ops": a calm graphite UI where
/// colour carries meaning only. Blue = interactive, green = calm, amber =
/// warning, red = active threat (strictly), violet = night summary.
///
/// Fields are **mutable** so the app can switch between light and dark at
/// runtime: [applyDark]/[applyLight] swap every token, and because widgets read
/// these (non-const) values on each build, a rebuild picks up the new theme.
class AppColors {
  const AppColors._();

  static bool isDark = true;

  // Backgrounds
  static Color background = _darkBackground;
  static Color surface = _darkSurface;
  static Color surface2 = _darkSurface2;
  static Color scaffold = _darkScaffold;

  // Interactive accent
  static Color primary = _darkPrimary;
  static Color primaryDark = _darkPrimaryDark;
  static Color primarySoft = _darkPrimarySoft;
  static Color primaryTint = _darkPrimaryTint;
  static Color accent = _darkAccent;
  static Color onPrimary = Colors.white;

  // Threat (red) — active alerts only
  static Color threat = _darkThreat;
  static Color threatDark = _darkThreatDark;
  static Color threatSoft = _darkThreatSoft;

  // Warning (amber)
  static Color warn = _darkWarn;
  static Color warnSoft = _darkWarnSoft;

  // Safe / calm
  static Color safe = _darkSafe;
  static Color safeSoft = _darkSafeSoft;

  // Night summary (violet)
  static Color night = _darkNight;

  // Text
  static Color textPrimary = _darkTextPrimary;
  static Color textSecondary = _darkTextSecondary;
  static Color textMuted = _darkTextMuted;

  // Lines / borders
  static Color border = _darkBorder;
  static Color divider = _darkDivider;

  // Panels (kept under the old "glass" names so every screen keeps compiling)
  static Color glassFill = _darkSurface;
  static Color glassStroke = _darkBorder;
  static Color glassShadow = _darkShadow;

  // Sidebar
  static Color sidebarBg = _darkSidebarBg;
  static Color sidebarActive = _darkPrimarySoft;

  // Map
  static Color mapSea = _darkMapSea;
  static Color mapLand = _darkMapLand;

  static List<Color> chartGradient = _darkChartGradient;

  /// Tone helper: the colour for a status class ('alarm' | 'warn' | other).
  static Color forCls(String cls) => cls == 'alarm'
      ? threat
      : cls == 'warn'
          ? warn
          : safe;

  // --- Light values -----------------------------------------------------------
  static const Color _lightBackground = Color(0xFFF2F4F7);
  static const Color _lightSurface = Color(0xFFFFFFFF);
  static const Color _lightSurface2 = Color(0xFFF6F7F9);
  static const Color _lightScaffold = Color(0xFFECEFF3);
  static const Color _lightPrimary = Color(0xFF1F6FEB);
  static const Color _lightPrimaryDark = Color(0xFF1758C4);
  static const Color _lightPrimarySoft = Color(0xFFE7EFFD);
  static const Color _lightPrimaryTint = Color(0xFFC6D9FA);
  static const Color _lightAccent = Color(0xFF0EA5C6);
  static const Color _lightThreat = Color(0xFFE0362B);
  static const Color _lightThreatDark = Color(0xFFBB2A20);
  static const Color _lightThreatSoft = Color(0xFFFCEBEA);
  static const Color _lightWarn = Color(0xFFCF8400);
  static const Color _lightWarnSoft = Color(0xFFFFF3DA);
  static const Color _lightSafe = Color(0xFF14984F);
  static const Color _lightSafeSoft = Color(0xFFE5F5EB);
  static const Color _lightNight = Color(0xFF7C4DDB);
  static const Color _lightTextPrimary = Color(0xFF0D1117);
  static const Color _lightTextSecondary = Color(0xFF4F5A69);
  static const Color _lightTextMuted = Color(0xFF8A94A3);
  static const Color _lightBorder = Color(0xFFDFE3E9);
  static const Color _lightDivider = Color(0xFFEBEEF2);
  static const Color _lightShadow = Color(0x140D1117);
  static const Color _lightSidebarBg = Color(0xFFFFFFFF);
  static const Color _lightMapSea = Color(0xFFE6ECF3);
  static const Color _lightMapLand = Color(0xFFFFFFFF);
  static const List<Color> _lightChartGradient = [
    Color(0xFF0EA5C6),
    Color(0xFF1F6FEB),
  ];

  // --- Dark values ------------------------------------------------------------
  static const Color _darkBackground = Color(0xFF0A0D12);
  static const Color _darkSurface = Color(0xFF11161D);
  static const Color _darkSurface2 = Color(0xFF181E27);
  static const Color _darkScaffold = Color(0xFF07090D);
  static const Color _darkPrimary = Color(0xFF4C93FF);
  static const Color _darkPrimaryDark = Color(0xFF8DBBFF);
  static const Color _darkPrimarySoft = Color(0xFF13223A);
  static const Color _darkPrimaryTint = Color(0xFF1D3658);
  static const Color _darkAccent = Color(0xFF3CCBEA);
  static const Color _darkThreat = Color(0xFFFF453A);
  static const Color _darkThreatDark = Color(0xFFFF7D74);
  static const Color _darkThreatSoft = Color(0xFF2B1215);
  static const Color _darkWarn = Color(0xFFFFB224);
  static const Color _darkWarnSoft = Color(0xFF2A1F0A);
  static const Color _darkSafe = Color(0xFF2FD17C);
  static const Color _darkSafeSoft = Color(0xFF0E241A);
  static const Color _darkNight = Color(0xFFA585FF);
  static const Color _darkTextPrimary = Color(0xFFECF0F5);
  static const Color _darkTextSecondary = Color(0xFF9AA5B4);
  static const Color _darkTextMuted = Color(0xFF5F6A7A);
  static const Color _darkBorder = Color(0xFF222A35);
  static const Color _darkDivider = Color(0xFF1A2029);
  static const Color _darkShadow = Color(0x55000000);
  static const Color _darkSidebarBg = Color(0xFF0D1117);
  static const Color _darkMapSea = Color(0xFF080B10);
  static const Color _darkMapLand = Color(0xFF161D27);
  static const List<Color> _darkChartGradient = [
    Color(0xFF3CCBEA),
    Color(0xFF4C93FF),
  ];

  static void applyLight() {
    isDark = false;
    background = _lightBackground;
    surface = _lightSurface;
    surface2 = _lightSurface2;
    scaffold = _lightScaffold;
    primary = _lightPrimary;
    primaryDark = _lightPrimaryDark;
    primarySoft = _lightPrimarySoft;
    primaryTint = _lightPrimaryTint;
    accent = _lightAccent;
    threat = _lightThreat;
    threatDark = _lightThreatDark;
    threatSoft = _lightThreatSoft;
    warn = _lightWarn;
    warnSoft = _lightWarnSoft;
    safe = _lightSafe;
    safeSoft = _lightSafeSoft;
    night = _lightNight;
    textPrimary = _lightTextPrimary;
    textSecondary = _lightTextSecondary;
    textMuted = _lightTextMuted;
    border = _lightBorder;
    divider = _lightDivider;
    glassFill = _lightSurface;
    glassStroke = _lightBorder;
    glassShadow = _lightShadow;
    sidebarBg = _lightSidebarBg;
    sidebarActive = _lightPrimarySoft;
    mapSea = _lightMapSea;
    mapLand = _lightMapLand;
    chartGradient = _lightChartGradient;
  }

  static void applyDark() {
    isDark = true;
    background = _darkBackground;
    surface = _darkSurface;
    surface2 = _darkSurface2;
    scaffold = _darkScaffold;
    primary = _darkPrimary;
    primaryDark = _darkPrimaryDark;
    primarySoft = _darkPrimarySoft;
    primaryTint = _darkPrimaryTint;
    accent = _darkAccent;
    threat = _darkThreat;
    threatDark = _darkThreatDark;
    threatSoft = _darkThreatSoft;
    warn = _darkWarn;
    warnSoft = _darkWarnSoft;
    safe = _darkSafe;
    safeSoft = _darkSafeSoft;
    night = _darkNight;
    textPrimary = _darkTextPrimary;
    textSecondary = _darkTextSecondary;
    textMuted = _darkTextMuted;
    border = _darkBorder;
    divider = _darkDivider;
    glassFill = _darkSurface;
    glassStroke = _darkBorder;
    glassShadow = _darkShadow;
    sidebarBg = _darkSidebarBg;
    sidebarActive = _darkPrimarySoft;
    mapSea = _darkMapSea;
    mapLand = _darkMapLand;
    chartGradient = _darkChartGradient;
  }
}
