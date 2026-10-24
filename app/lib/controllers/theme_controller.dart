import 'package:flutter/foundation.dart';

import '../services/local_store.dart';
import '../theme/app_colors.dart';

/// Owns the light/dark choice. Applies the palette to [AppColors] and notifies,
/// which rebuilds the app so every widget picks up the new (non-const) colors.
class ThemeController extends ChangeNotifier {
  ThemeController(this._store) {
    // The 2026-10 redesign is dark-first and uses a fresh key, so every
    // install starts in the dark "night ops" theme (light is one tap away).
    _dark = _store.getBool(_key, fallback: true);
    _apply();
  }

  static const _key = 'ui_dark';

  final LocalStore _store;
  bool _dark = true;
  bool get dark => _dark;

  void _apply() => _dark ? AppColors.applyDark() : AppColors.applyLight();

  /// Call once before runApp so the very first frame is already themed.
  void applyNow() => _apply();

  void setDark(bool value) {
    if (value == _dark) return;
    _dark = value;
    _store.setBool(_key, value);
    _apply();
    notifyListeners();
  }

  void toggle() => setDark(!_dark);
}
