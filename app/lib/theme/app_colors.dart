import 'package:flutter/material.dart';

/// WarAlarm design-system palette.
///
/// Fields are **mutable** so the app can switch between light and dark at
/// runtime: [applyDark]/[applyLight] swap every token, and because widgets read
/// these (non-const) values on each build, a rebuild picks up the new theme.
/// Red stays reserved **strictly** for active threats/alerts in both themes.
class AppColors {
  const AppColors._();

  static bool isDark = false;

  // Backgrounds
  static Color background = _lightBackground;
  static Color surface = _lightSurface;
  static Color scaffold = _lightScaffold;

  // Light-blue primary scheme
  static Color primary = _lightPrimary;
  static Color primaryDark = _lightPrimaryDark;
  static Color primarySoft = _lightPrimarySoft;
  static Color primaryTint = _lightPrimaryTint;
  static Color accent = _lightAccent;

  // Threat (red) — active alerts only
  static Color threat = _lightThreat;
  static Color threatDark = _lightThreatDark;
  static Color threatSoft = _lightThreatSoft;

  // Safe / calm
  static Color safe = _lightSafe;
  static Color safeSoft = _lightSafeSoft;

  // Text
  static Color textPrimary = _lightTextPrimary;
  static Color textSecondary = _lightTextSecondary;
  static Color textMuted = _lightTextMuted;

  // Lines / borders
  static Color border = _lightBorder;
  static Color divider = _lightDivider;

  // Glass overlays
  static Color glassFill = _lightGlassFill;
  static Color glassStroke = _lightGlassStroke;
  static Color glassShadow = _lightGlassShadow;

  // Sidebar
  static Color sidebarBg = _lightSidebarBg;
  static Color sidebarActive = _lightPrimarySoft;

  // Map
  static Color mapSea = _lightMapSea;
  static Color mapLand = _lightMapLand;

  static List<Color> chartGradient = _lightChartGradient;

  // --- Light values -----------------------------------------------------------
  static const Color _lightBackground = Color(0xFFF7FAFF);
  static const Color _lightSurface = Color(0xFFFFFFFF);
  static const Color _lightScaffold = Color(0xFFFFFFFF);
  static const Color _lightPrimary = Color(0xFF3B9EFF);
  static const Color _lightPrimaryDark = Color(0xFF1E7BE0);
  static const Color _lightPrimarySoft = Color(0xFFE6F2FF);
  static const Color _lightPrimaryTint = Color(0xFFCFE7FF);
  static const Color _lightAccent = Color(0xFF5AC8FA);
  static const Color _lightThreat = Color(0xFFF4433D);
  static const Color _lightThreatDark = Color(0xFFD32F2A);
  static const Color _lightThreatSoft = Color(0xFFFFE9E8);
  static const Color _lightSafe = Color(0xFF34C759);
  static const Color _lightSafeSoft = Color(0xFFE7F9EC);
  static const Color _lightTextPrimary = Color(0xFF0F2035);
  static const Color _lightTextSecondary = Color(0xFF5B6B7F);
  static const Color _lightTextMuted = Color(0xFF93A2B4);
  static const Color _lightBorder = Color(0xFFE4ECF5);
  static const Color _lightDivider = Color(0xFFEDF2F8);
  static const Color _lightGlassFill = Color(0x99FFFFFF);
  static const Color _lightGlassStroke = Color(0x66FFFFFF);
  static const Color _lightGlassShadow = Color(0x14123A6B);
  static const Color _lightSidebarBg = Color(0xFFFFFFFF);
  static const Color _lightMapSea = Color(0xFFEAF3FF);
  static const Color _lightMapLand = Color(0xFFFFFFFF);
  static const List<Color> _lightChartGradient = [
    Color(0xFF5AC8FA),
    Color(0xFF3B9EFF),
  ];

  // --- Dark values ------------------------------------------------------------
  static const Color _darkBackground = Color(0xFF0E1626);
  static const Color _darkSurface = Color(0xFF17223B);
  static const Color _darkScaffold = Color(0xFF0B1220);
  static const Color _darkPrimary = Color(0xFF4FA6FF);
  static const Color _darkPrimaryDark = Color(0xFF8CC4FF);
  static const Color _darkPrimarySoft = Color(0xFF1B2A46);
  static const Color _darkPrimaryTint = Color(0xFF25406B);
  static const Color _darkAccent = Color(0xFF5AC8FA);
  static const Color _darkThreat = Color(0xFFFF5A54);
  static const Color _darkThreatDark = Color(0xFFFF8A85);
  static const Color _darkThreatSoft = Color(0xFF3A1D20);
  static const Color _darkSafe = Color(0xFF3DDC6B);
  static const Color _darkSafeSoft = Color(0xFF14301E);
  static const Color _darkTextPrimary = Color(0xFFEAF1FB);
  static const Color _darkTextSecondary = Color(0xFFA7B4C6);
  static const Color _darkTextMuted = Color(0xFF6E7D92);
  static const Color _darkBorder = Color(0xFF25324C);
  static const Color _darkDivider = Color(0xFF1C2740);
  static const Color _darkGlassFill = Color(0x6617223B);
  static const Color _darkGlassStroke = Color(0x33FFFFFF);
  static const Color _darkGlassShadow = Color(0x33000000);
  static const Color _darkSidebarBg = Color(0xFF111B30);
  static const Color _darkMapSea = Color(0xFF0C1526);
  static const Color _darkMapLand = Color(0xFF1B2740);
  static const List<Color> _darkChartGradient = [
    Color(0xFF5AC8FA),
    Color(0xFF4FA6FF),
  ];

  static void applyLight() {
    isDark = false;
    background = _lightBackground;
    surface = _lightSurface;
    scaffold = _lightScaffold;
    primary = _lightPrimary;
    primaryDark = _lightPrimaryDark;
    primarySoft = _lightPrimarySoft;
    primaryTint = _lightPrimaryTint;
    accent = _lightAccent;
    threat = _lightThreat;
    threatDark = _lightThreatDark;
    threatSoft = _lightThreatSoft;
    safe = _lightSafe;
    safeSoft = _lightSafeSoft;
    textPrimary = _lightTextPrimary;
    textSecondary = _lightTextSecondary;
    textMuted = _lightTextMuted;
    border = _lightBorder;
    divider = _lightDivider;
    glassFill = _lightGlassFill;
    glassStroke = _lightGlassStroke;
    glassShadow = _lightGlassShadow;
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
    scaffold = _darkScaffold;
    primary = _darkPrimary;
    primaryDark = _darkPrimaryDark;
    primarySoft = _darkPrimarySoft;
    primaryTint = _darkPrimaryTint;
    accent = _darkAccent;
    threat = _darkThreat;
    threatDark = _darkThreatDark;
    threatSoft = _darkThreatSoft;
    safe = _darkSafe;
    safeSoft = _darkSafeSoft;
    textPrimary = _darkTextPrimary;
    textSecondary = _darkTextSecondary;
    textMuted = _darkTextMuted;
    border = _darkBorder;
    divider = _darkDivider;
    glassFill = _darkGlassFill;
    glassStroke = _darkGlassStroke;
    glassShadow = _darkGlassShadow;
    sidebarBg = _darkSidebarBg;
    sidebarActive = _darkPrimarySoft;
    mapSea = _darkMapSea;
    mapLand = _darkMapLand;
    chartGradient = _darkChartGradient;
  }
}
