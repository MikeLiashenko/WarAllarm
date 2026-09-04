import 'package:flutter/material.dart';

/// WarAlarm design system palette.
///
/// Crisp white background, light-blue accents, glassmorphism. Red is reserved
/// **strictly** for active threats/alerts.
class AppColors {
  const AppColors._();

  // Backgrounds
  static const Color background = Color(0xFFF7FAFF);
  static const Color surface = Color(0xFFFFFFFF);
  static const Color scaffold = Color(0xFFFFFFFF);

  // Light-blue primary scheme
  static const Color primary = Color(0xFF3B9EFF);
  static const Color primaryDark = Color(0xFF1E7BE0);
  static const Color primarySoft = Color(0xFFE6F2FF);
  static const Color primaryTint = Color(0xFFCFE7FF);
  static const Color accent = Color(0xFF5AC8FA);

  // Threat (red) — active alerts only
  static const Color threat = Color(0xFFF4433D);
  static const Color threatDark = Color(0xFFD32F2A);
  static const Color threatSoft = Color(0xFFFFE9E8);

  // Safe / calm
  static const Color safe = Color(0xFF34C759);
  static const Color safeSoft = Color(0xFFE7F9EC);

  // Text
  static const Color textPrimary = Color(0xFF0F2035);
  static const Color textSecondary = Color(0xFF5B6B7F);
  static const Color textMuted = Color(0xFF93A2B4);

  // Lines / borders
  static const Color border = Color(0xFFE4ECF5);
  static const Color divider = Color(0xFFEDF2F8);

  // Glass overlays
  static const Color glassFill = Color(0x99FFFFFF); // 60% white
  static const Color glassStroke = Color(0x66FFFFFF); // 40% white
  static const Color glassShadow = Color(0x14123A6B);

  // Sidebar
  static const Color sidebarBg = Color(0xFFFFFFFF);
  static const Color sidebarActive = primarySoft;

  static const List<Color> chartGradient = [
    Color(0xFF5AC8FA),
    Color(0xFF3B9EFF),
  ];
}
