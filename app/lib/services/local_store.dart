import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// Thin wrapper over [SharedPreferences] for persisting local-only state
/// (saved places, settings, AI sessions, API keys). Works on Android + Windows.
class LocalStore {
  LocalStore._(this._prefs);

  final SharedPreferences _prefs;

  static Future<LocalStore> create() async {
    final prefs = await SharedPreferences.getInstance();
    return LocalStore._(prefs);
  }

  // Generic JSON list helpers
  List<Map<String, dynamic>> readJsonList(String key) {
    final raw = _prefs.getString(key);
    if (raw == null || raw.isEmpty) return [];
    try {
      final decoded = jsonDecode(raw) as List<dynamic>;
      return decoded.map((e) => Map<String, dynamic>.from(e)).toList();
    } catch (_) {
      return [];
    }
  }

  Future<void> writeJsonList(String key, List<Map<String, dynamic>> value) {
    return _prefs.setString(key, jsonEncode(value));
  }

  // Primitives
  bool getBool(String key, {bool fallback = false}) =>
      _prefs.getBool(key) ?? fallback;
  Future<void> setBool(String key, bool value) => _prefs.setBool(key, value);

  String getString(String key, {String fallback = ''}) =>
      _prefs.getString(key) ?? fallback;
  Future<void> setString(String key, String value) =>
      _prefs.setString(key, value);

  double getDouble(String key, {double fallback = 0}) =>
      _prefs.getDouble(key) ?? fallback;
  Future<void> setDouble(String key, double value) =>
      _prefs.setDouble(key, value);
}
