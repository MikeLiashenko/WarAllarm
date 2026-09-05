import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../models/saved_place.dart';
import '../services/local_store.dart';

/// Manages the user's saved monitoring locations, persisted locally.
class PlacesController extends ChangeNotifier {
  PlacesController(this._store) {
    _load();
  }

  static const _key = 'saved_places';
  final LocalStore _store;
  final _uuid = const Uuid();

  List<SavedPlace> _places = [];
  List<SavedPlace> get places => List.unmodifiable(_places);

  void _load() {
    final rows = _store.readJsonList(_key);
    if (rows.isEmpty) {
      _places = _seed();
      _persist();
    } else {
      _places = rows.map(SavedPlace.fromJson).toList();
    }
    notifyListeners();
  }

  Future<void> add({
    required String name,
    required String region,
    required double lat,
    required double lng,
  }) async {
    _places.add(SavedPlace(
      id: _uuid.v4(),
      name: name,
      region: region,
      lat: lat,
      lng: lng,
    ));
    await _persist();
    notifyListeners();
  }

  Future<void> remove(String id) async {
    _places.removeWhere((p) => p.id == id);
    await _persist();
    notifyListeners();
  }

  Future<void> toggleNotify(String id) async {
    final i = _places.indexWhere((p) => p.id == id);
    if (i == -1) return;
    _places[i] = _places[i].copyWith(notify: !_places[i].notify);
    await _persist();
    notifyListeners();
  }

  Future<void> _persist() =>
      _store.writeJsonList(_key, _places.map((p) => p.toJson()).toList());

  // No default places — home (Kharkiv) and the Kyiv guest place are built in,
  // so seeding "Kharkiv"/"Kyiv" here just created confusing duplicates.
  List<SavedPlace> _seed() => const [];
}
