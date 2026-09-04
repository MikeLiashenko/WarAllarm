import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../models/api_key.dart';
import '../services/local_store.dart';

/// Manages generated project API keys, persisted locally.
class ApiKeysController extends ChangeNotifier {
  ApiKeysController(this._store) {
    _load();
  }

  static const _key = 'api_keys';
  final LocalStore _store;
  final _uuid = const Uuid();
  final _rng = Random.secure();

  List<ApiKey> _keys = [];
  List<ApiKey> get keys => List.unmodifiable(_keys);

  void _load() {
    _keys = _store.readJsonList(_key).map(ApiKey.fromJson).toList();
    notifyListeners();
  }

  ApiKey generate(String label) {
    final key = ApiKey(
      id: _uuid.v4(),
      label: label.trim().isEmpty ? 'Untitled key' : label.trim(),
      key: _randomKey(),
      createdAt: DateTime.now(),
    );
    _keys.insert(0, key);
    _persist();
    notifyListeners();
    return key;
  }

  Future<void> revoke(String id) async {
    final i = _keys.indexWhere((k) => k.id == id);
    if (i == -1) return;
    _keys[i].active = !_keys[i].active;
    await _persist();
    notifyListeners();
  }

  Future<void> remove(String id) async {
    _keys.removeWhere((k) => k.id == id);
    await _persist();
    notifyListeners();
  }

  String _randomKey() {
    const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
    final body =
        List.generate(32, (_) => chars[_rng.nextInt(chars.length)]).join();
    return 'wa_live_$body';
  }

  Future<void> _persist() =>
      _store.writeJsonList(_key, _keys.map((k) => k.toJson()).toList());
}
