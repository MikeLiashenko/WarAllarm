import 'package:flutter/material.dart';

/// The primary destinations. «Мапа» is the home screen — the live map with
/// the place card and details floating over it.
enum AppTab { now, myPlaces, chat, ai, settings, apiKeys }

extension AppTabInfo on AppTab {
  String get label => switch (this) {
        AppTab.now => 'Мапа',
        AppTab.myPlaces => 'Мої місця',
        AppTab.settings => 'Налаштування',
        AppTab.chat => 'Чат',
        AppTab.ai => 'AI-помічник',
        AppTab.apiKeys => 'API · Дані',
      };

  IconData get icon => switch (this) {
        AppTab.now => Icons.radar_rounded,
        AppTab.myPlaces => Icons.place_outlined,
        AppTab.settings => Icons.tune_rounded,
        AppTab.chat => Icons.forum_outlined,
        AppTab.ai => Icons.auto_awesome_outlined,
        AppTab.apiKeys => Icons.data_object_rounded,
      };

  IconData get activeIcon => switch (this) {
        AppTab.now => Icons.radar_rounded,
        AppTab.myPlaces => Icons.place_rounded,
        AppTab.settings => Icons.tune_rounded,
        AppTab.chat => Icons.forum_rounded,
        AppTab.ai => Icons.auto_awesome_rounded,
        AppTab.apiKeys => Icons.data_object_rounded,
      };
}

/// Tracks the active tab.
class NavController extends ChangeNotifier {
  AppTab _tab = AppTab.now;
  AppTab get tab => _tab;

  void select(AppTab tab) {
    if (_tab == tab) return;
    _tab = tab;
    notifyListeners();
  }
}
