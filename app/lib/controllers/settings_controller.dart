import 'package:flutter/foundation.dart';

import '../services/local_store.dart';

/// App preferences, persisted locally. Independent of the website design.
class SettingsController extends ChangeNotifier {
  SettingsController(this._store) {
    _pushNotifications = _store.getBool('set_push', fallback: true);
    _soundAlerts = _store.getBool('set_sound', fallback: true);
    _threatOnly = _store.getBool('set_threat_only', fallback: false);
    _vibration = _store.getBool('set_vibration', fallback: true);
    _keepScreenOn = _store.getBool('set_keep_screen', fallback: false);
    _compactFeed = _store.getBool('set_compact_feed', fallback: false);
    _displayName = _store.getString('set_name', fallback: 'Guest');
  }

  final LocalStore _store;

  bool _pushNotifications = true;
  bool _soundAlerts = true;
  bool _threatOnly = false;
  bool _vibration = true;
  bool _keepScreenOn = false;
  bool _compactFeed = false;
  String _displayName = 'Guest';

  bool get pushNotifications => _pushNotifications;
  bool get soundAlerts => _soundAlerts;
  bool get threatOnly => _threatOnly;
  bool get vibration => _vibration;
  bool get keepScreenOn => _keepScreenOn;
  bool get compactFeed => _compactFeed;
  String get displayName => _displayName;

  void _setBool(String key, bool value, void Function(bool) apply) {
    apply(value);
    _store.setBool(key, value);
    notifyListeners();
  }

  void setPush(bool v) => _setBool('set_push', v, (x) => _pushNotifications = x);
  void setSound(bool v) => _setBool('set_sound', v, (x) => _soundAlerts = x);
  void setThreatOnly(bool v) =>
      _setBool('set_threat_only', v, (x) => _threatOnly = x);
  void setVibration(bool v) =>
      _setBool('set_vibration', v, (x) => _vibration = x);
  void setKeepScreenOn(bool v) =>
      _setBool('set_keep_screen', v, (x) => _keepScreenOn = x);
  void setCompactFeed(bool v) =>
      _setBool('set_compact_feed', v, (x) => _compactFeed = x);

  void setDisplayName(String value) {
    _displayName = value.trim().isEmpty ? 'Guest' : value.trim();
    _store.setString('set_name', _displayName);
    notifyListeners();
  }
}
