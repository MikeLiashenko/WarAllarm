import 'package:flutter/foundation.dart';

/// The seven primary destinations in the fixed sidebar.
enum AppTab { now, map, myPlaces, settings, chat, ai, apiKeys }

extension AppTabInfo on AppTab {
  String get label => switch (this) {
        AppTab.now => 'Now',
        AppTab.map => 'Map',
        AppTab.myPlaces => 'My Places',
        AppTab.settings => 'Settings',
        AppTab.chat => 'Chat',
        AppTab.ai => 'AI',
        AppTab.apiKeys => 'API · Дані',
      };
}

/// Tracks the active sidebar tab.
class NavController extends ChangeNotifier {
  AppTab _tab = AppTab.now;
  AppTab get tab => _tab;

  void select(AppTab tab) {
    if (_tab == tab) return;
    _tab = tab;
    notifyListeners();
  }
}
