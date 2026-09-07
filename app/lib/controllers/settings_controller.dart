import 'package:flutter/foundation.dart';

import '../services/local_store.dart';

/// App preferences, persisted locally. Independent of the website design.
class SettingsController extends ChangeNotifier {
  SettingsController(this._store,
      {Map<String, String> externalSecrets = const {}}) {
    _pushNotifications = _store.getBool('set_push', fallback: true);
    _soundAlerts = _store.getBool('set_sound', fallback: true);
    _threatOnly = _store.getBool('set_threat_only', fallback: false);
    _vibration = _store.getBool('set_vibration', fallback: true);
    _alarmVolume = _store.getDouble('set_volume', fallback: 0.5).clamp(0.0, 1.0);
    _alarmRadiusKm = _store.getDouble('set_alarm_radius', fallback: 25).clamp(5, 100);
    _audibilityKm = _store.getDouble('set_audibility_km', fallback: 15).clamp(3, 60);
    _showAudibility = _store.getBool('set_audibility_show', fallback: true);
    _snoozeSeconds = _store.getDouble('set_snooze_sec', fallback: 90).round();
    _keepScreenOn = _store.getBool('set_keep_screen', fallback: false);
    _compactFeed = _store.getBool('set_compact_feed', fallback: false);
    _displayName = _store.getString('set_name', fallback: 'Guest');
    // Prefer an in-app override; otherwise reuse the Reader's secrets.local.txt.
    _aiApiKey = _store.getString('ai_api_key');
    if (_aiApiKey.isEmpty) _aiApiKey = externalSecrets['ai_api_key'] ?? '';
    _aiModel = _store.getString('ai_model');
    if (_aiModel.isEmpty) _aiModel = externalSecrets['ai_model'] ?? '';
    _keyFromSecrets = _store.getString('ai_api_key').isEmpty && _aiApiKey.isNotEmpty;
  }

  /// True when the active key came from the Reader's secrets file (not typed in).
  bool _keyFromSecrets = false;
  bool get keyFromSecrets => _keyFromSecrets;

  final LocalStore _store;

  bool _pushNotifications = true;
  bool _soundAlerts = true;
  bool _threatOnly = false;
  bool _vibration = true;
  double _alarmVolume = 0.5;
  double _alarmRadiusKm = 25;
  double _audibilityKm = 15;
  bool _showAudibility = true;
  int _snoozeSeconds = 90;
  bool _keepScreenOn = false;
  bool _compactFeed = false;
  String _displayName = 'Guest';
  String _aiApiKey = '';
  String _aiModel = '';

  bool get pushNotifications => _pushNotifications;
  bool get soundAlerts => _soundAlerts;
  bool get threatOnly => _threatOnly;
  bool get vibration => _vibration;
  double get alarmVolume => _alarmVolume;
  double get alarmRadiusKm => _alarmRadiusKm;
  double get audibilityKm => _audibilityKm;
  bool get showAudibility => _showAudibility;
  int get snoozeSeconds => _snoozeSeconds;
  bool get keepScreenOn => _keepScreenOn;
  bool get compactFeed => _compactFeed;
  String get displayName => _displayName;
  String get aiApiKey => _aiApiKey;
  String get aiModel => _aiModel;
  bool get aiConfigured => _aiApiKey.trim().isNotEmpty;

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

  void setAlarmVolume(double v) {
    _alarmVolume = v.clamp(0.0, 1.0);
    _store.setDouble('set_volume', _alarmVolume);
    notifyListeners();
  }

  void setAlarmRadiusKm(double v) {
    _alarmRadiusKm = v.clamp(5, 100);
    _store.setDouble('set_alarm_radius', _alarmRadiusKm);
    notifyListeners();
  }

  void setAudibilityKm(double v) {
    _audibilityKm = v.clamp(3, 60);
    _store.setDouble('set_audibility_km', _audibilityKm);
    notifyListeners();
  }

  void setShowAudibility(bool v) =>
      _setBool('set_audibility_show', v, (x) => _showAudibility = x);

  void setSnoozeSeconds(int v) {
    _snoozeSeconds = v;
    _store.setDouble('set_snooze_sec', v.toDouble());
    notifyListeners();
  }
  void setKeepScreenOn(bool v) =>
      _setBool('set_keep_screen', v, (x) => _keepScreenOn = x);
  void setCompactFeed(bool v) =>
      _setBool('set_compact_feed', v, (x) => _compactFeed = x);

  void setDisplayName(String value) {
    _displayName = value.trim().isEmpty ? 'Guest' : value.trim();
    _store.setString('set_name', _displayName);
    notifyListeners();
  }

  void setAiApiKey(String value) {
    _aiApiKey = value.trim();
    _store.setString('ai_api_key', _aiApiKey);
    notifyListeners();
  }

  void setAiModel(String value) {
    _aiModel = value.trim();
    _store.setString('ai_model', _aiModel);
    notifyListeners();
  }
}
