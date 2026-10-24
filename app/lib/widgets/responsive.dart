import 'package:flutter/widgets.dart';

/// Simple breakpoint helper so screens scale between desktop windows and
/// Android screen ratios.
class Responsive {
  const Responsive._();

  static const double compact = 640; // phones
  static const double medium = 1000; // small windows / tablets

  static bool isCompact(BuildContext c) =>
      MediaQuery.sizeOf(c).width < compact;
  static bool isMedium(BuildContext c) {
    final w = MediaQuery.sizeOf(c).width;
    return w >= compact && w < medium;
  }

  static bool isExpanded(BuildContext c) =>
      MediaQuery.sizeOf(c).width >= medium;

  /// Phones get a bottom navigation bar instead of a side rail.
  static bool useBottomNav(BuildContext c) => isCompact(c);

  /// Whether the sidebar should collapse to icons-only (narrow windows).
  static bool railCollapsed(BuildContext c) =>
      MediaQuery.sizeOf(c).width < 1100;

  /// Max content width so text lines stay comfortable on wide desktop windows.
  static double contentMaxWidth(BuildContext c) => 1240;
}
