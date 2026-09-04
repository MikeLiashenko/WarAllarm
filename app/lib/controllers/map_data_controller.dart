import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';

import '../models/analytics_row.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import '../services/local_store.dart';
import '../services/rtdb_service.dart';

/// Central app state, mirroring the Reader's `map.html` state object `S`:
/// home, threats, analytics rows, the real-AI text, config and places.
///
/// Streams from the RTDB `state` node (same JSON the Reader builds in
/// `MapWriter`). Until that node is populated + readable, a rich demo scene is
/// shown so every screen is functional.
class MapDataController extends ChangeNotifier {
  MapDataController(this._rtdb, this._store) {
    _selectedId = _store.getString('sel_place', fallback: 'all');
  }

  final RtdbService _rtdb;
  final LocalStore _store;
  StreamSubscription<dynamic>? _sub;

  static const homeKharkiv = (lat: 49.9935, lon: 36.2304);

  ({double lat, double lon}) _home = homeKharkiv;
  ({double lat, double lon}) get home => _home;

  String _homeName = 'Харків · Салтівка';
  String get homeName => _homeName;

  String _district = 'Салтівський';
  String _city = 'Харків';
  bool _watchKyiv = true;

  List<MapTarget> _targets = const [];
  List<MapTarget> get targets => _targets;

  List<AnalyticsRow> _rows = const [];
  List<AnalyticsRow> get rows => _rows;

  String _aiText = '';
  String get aiText => _aiText;
  DateTime? _aiAt;
  DateTime? get aiAt => _aiAt;

  int _stateTs = DateTime.now().millisecondsSinceEpoch;
  int get stateTs => _stateTs;

  bool _live = false;
  bool get live => _live;

  String _selectedId = 'all';
  String get selectedId => _selectedId;

  void start() {
    _targets = _demoTargets();
    _rows = _demoRows();
    _sub = _rtdb.stream('state').listen(_onData, onError: (_) {});
  }

  void select(String id) {
    _selectedId = id;
    _store.setString('sel_place', id);
    notifyListeners();
  }

  // --- Places (home + Kyiv guest + user-saved) --------------------------------

  List<MonitoredPlace> get places {
    final list = <MonitoredPlace>[
      MonitoredPlace(
        id: 'home',
        icon: '🏠',
        name: _district,
        sub: _city,
        lat: _home.lat,
        lon: _home.lon,
        zone: 'Харків',
      ),
      if (_watchKyiv)
        const MonitoredPlace(
          id: 'kyiv',
          icon: '🏙',
          name: 'Київ',
          sub: 'гість',
          lat: 50.45,
          lon: 30.52,
          zone: 'Київ',
        ),
    ];
    final saved = _store.readJsonList('saved_places');
    for (var i = 0; i < saved.length; i++) {
      final p = saved[i];
      list.add(MonitoredPlace(
        id: 'u$i',
        icon: '📍',
        name: (p['name'] ?? 'Місце').toString(),
        sub: (p['region'] ?? '').toString(),
        lat: (p['lat'] as num).toDouble(),
        lon: (p['lng'] ?? p['lon'] as num).toDouble(),
      ));
    }
    return list;
  }

  List<MonitoredPlace> get selectedPlaces {
    final all = places;
    if (_selectedId == 'all') return all;
    return all.where((p) => p.id == _selectedId).toList();
  }

  static const _rank = {
    'Impact': 5, 'Overhead': 4, 'Imminent': 3, 'Approaching': 2, 'Passed': 1,
  };

  /// Distance target→place (km), haversine.
  static double _km(MapTarget t, MonitoredPlace p) {
    const r = 6371.0, rad = math.pi / 180;
    final dLa = (p.lat - t.lat) * rad, dLo = (p.lon - t.lon) * rad;
    final h = math.sin(dLa / 2) * math.sin(dLa / 2) +
        math.cos(t.lat * rad) * math.cos(p.lat * rad) *
            math.sin(dLo / 2) * math.sin(dLo / 2);
    return r * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h));
  }

  /// Worst stage-rank among targets relevant (≤40 km or same zone) to any
  /// currently selected place → drives the threat indicator.
  bool get threatActive {
    for (final p in selectedPlaces) {
      for (final t in _targets) {
        final relevant = _km(t, p) <= 40 || (p.zone != null && t.zone == p.zone);
        if (relevant && (_rank[t.stage] ?? 0) >= 3) return true;
      }
    }
    return false;
  }

  int get activeCount => _targets.length;

  // --- RTDB ingest ------------------------------------------------------------

  void _onData(dynamic data) {
    if (data is! Map) return;
    _live = true;
    _stateTs = (data['ts'] as num?)?.toInt() ?? _stateTs;
    _homeName = (data['homeName'] ?? _homeName).toString();
    final home = data['home'];
    if (home is Map && home['lat'] != null && home['lon'] != null) {
      _home = (
        lat: (home['lat'] as num).toDouble(),
        lon: (home['lon'] as num).toDouble(),
      );
    }
    final cfg = data['cfg'];
    if (cfg is Map) {
      _district = (cfg['district'] ?? _district).toString();
      _city = (cfg['city'] ?? _city).toString();
      _watchKyiv = cfg['watchKyiv'] == true;
    }

    final rawT = data['threats'];
    final t = <MapTarget>[];
    if (rawT is List) {
      for (final e in rawT) {
        if (e is Map) t.add(MapTarget.fromMap(e));
      }
    }
    if (t.isNotEmpty) _targets = t;

    final rawR = data['rows'];
    if (rawR is List) {
      _rows = rawR
          .whereType<Map>()
          .map(AnalyticsRow.fromMap)
          .toList();
    }

    _aiText = (data['aiText'] ?? '').toString();
    final aiAt = data['aiAt'];
    if (aiAt is num) _aiAt = DateTime.fromMillisecondsSinceEpoch(aiAt.toInt());

    notifyListeners();
  }

  // --- Demo data --------------------------------------------------------------

  List<MapTarget> _demoTargets() {
    final now = DateTime.now().millisecondsSinceEpoch;
    return [
      MapTarget(
        label: 'Shahed-136', cat: 'shahed', zone: 'Харків', stage: 'Approaching',
        lat: 50.55, lon: 37.60, heading: 232, amount: 4, place: "Куп'янськ",
        dir: 'на Пд-Зх', stageLabel: 'наближається', icon: '🛩', distKm: 96,
        etaMin: 28, seenMs: now,
      ),
      MapTarget(
        label: 'Крилата ракета', cat: 'missile', zone: 'Харків', stage: 'Approaching',
        lat: 48.90, lon: 38.20, heading: 300, place: 'Лисичанськ', dir: 'на Зх',
        stageLabel: 'наближається', icon: '🚀', distKm: 145, etaMin: 11, seenMs: now,
      ),
      MapTarget(
        label: 'Балістика', cat: 'balist', zone: 'Дніпро', stage: 'Imminent',
        lat: 48.00, lon: 37.80, heading: 335, place: 'Донецьк', dir: 'на Пн-Зх',
        stageLabel: 'імовірно', icon: '☄', distKm: 233, etaMin: 3, seenMs: now,
      ),
      MapTarget(
        label: 'Шахед реактивний', cat: 'shahed', zone: 'Харків', stage: 'Imminent',
        lat: 50.02, lon: 36.55, heading: 260, place: 'Циркуни', dir: 'на Зх',
        stageLabel: 'підліт', icon: '🛩', distKm: 18, etaMin: 4, seenMs: now,
      ),
      MapTarget(
        label: 'КАБ', cat: 'kab', zone: 'Харків', stage: 'Approaching',
        lat: 50.05, lon: 37.20, heading: 250, place: 'Вовчанськ', dir: 'на Зх',
        stageLabel: 'наближається', icon: '💣', distKm: 70, etaMin: 8, seenMs: now,
      ),
      MapTarget(
        label: 'Авіація (МіГ-31К)', cat: 'avia', zone: 'UA', stage: 'Approaching',
        lat: 51.20, lon: 35.00, heading: 150, place: 'Курськ', dir: 'на Пд',
        stageLabel: 'патрулює', icon: '✈', distKm: 160, seenMs: now,
      ),
    ];
  }

  /// A plausible 45-day alarm journal so the analytics dashboard is populated.
  List<AnalyticsRow> _demoRows() {
    final rng = math.Random(42);
    const kinds = ['Drone', 'Missile', 'Ballistic', 'Kab', 'Aviation'];
    const zones = ['Харків', 'Київ', 'Дніпро'];
    // Night hours are heavier (typical shelling pattern).
    const hourWeights = [
      5, 6, 6, 5, 4, 3, 2, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5, 6, 6, 5
    ];
    final rows = <AnalyticsRow>[];
    for (var d = 44; d >= 0; d--) {
      final date =
          DateTime.now().subtract(Duration(days: d)).toIso8601String().substring(0, 10);
      final count = rng.nextInt(6); // 0-5 alarms that day
      for (var i = 0; i < count; i++) {
        // Weighted random hour.
        var pick = rng.nextInt(hourWeights.reduce((a, b) => a + b));
        var hour = 0;
        for (var h = 0; h < 24; h++) {
          pick -= hourWeights[h];
          if (pick < 0) {
            hour = h;
            break;
          }
        }
        rows.add(AnalyticsRow(
          date: date,
          hour: hour,
          zone: zones[rng.nextInt(zones.length)],
          kind: kinds[rng.nextInt(kinds.length)],
          tag: rng.nextInt(5) == 0 ? 'impact' : 'alarm',
        ));
      }
    }
    return rows;
  }

  @override
  void dispose() {
    _sub?.cancel();
    super.dispose();
  }
}
