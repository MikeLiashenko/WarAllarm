import 'dart:async';

import 'package:flutter/foundation.dart';

import '../models/map_target.dart';
import '../services/rtdb_service.dart';

/// Streams live map state (home + targets) from the RTDB `map` node.
///
/// The JSON shape matches the website's map state:
/// `{ "home": {"lat":..,"lon":..}, "homeName": "..", "threats": [ {..} ] }`.
/// When the node is empty, a rich demo scene over eastern Ukraine is shown so
/// the map always looks alive.
class MapDataController extends ChangeNotifier {
  MapDataController(this._rtdb);

  final RtdbService _rtdb;
  StreamSubscription<dynamic>? _sub;

  static const homeKharkiv = (lat: 49.9935, lon: 36.2304);

  ({double lat, double lon}) _home = homeKharkiv;
  ({double lat, double lon}) get home => _home;

  String _homeName = 'Харків';
  String get homeName => _homeName;

  List<MapTarget> _targets = const [];
  List<MapTarget> get targets => _targets;

  bool _live = false;
  bool get live => _live;

  void start() {
    _targets = _demo();
    _sub = _rtdb.stream('map').listen(_onData, onError: (_) {});
  }

  void _onData(dynamic data) {
    if (data is! Map) return;
    final home = data['home'];
    if (home is Map && home['lat'] != null && home['lon'] != null) {
      _home = (
        lat: (home['lat'] as num).toDouble(),
        lon: (home['lon'] as num).toDouble()
      );
    }
    _homeName = (data['homeName'] ?? _homeName).toString();

    final raw = data['threats'];
    final list = <MapTarget>[];
    if (raw is List) {
      for (final e in raw) {
        if (e is Map) list.add(MapTarget.fromMap(e));
      }
    } else if (raw is Map) {
      raw.forEach((_, v) {
        if (v is Map) list.add(MapTarget.fromMap(v));
      });
    }
    _live = true;
    _targets = list; // may be empty → map shows a clear country
    notifyListeners();
  }

  /// Demo scene: a Shahed swarm, a cruise missile, ballistic, a KAB and a jet
  /// converging on the Kharkiv area — headings point toward home.
  List<MapTarget> _demo() {
    final now = DateTime.now().millisecondsSinceEpoch;
    return [
      MapTarget(
        label: 'Shahed-136',
        cat: 'shahed',
        lat: 50.55,
        lon: 37.60,
        heading: 232,
        amount: 4,
        place: 'Куп\'янськ',
        dir: 'на Пд-Зх',
        stageLabel: 'наближається',
        distKm: 96,
        etaMin: 28,
        seenMs: now,
      ),
      MapTarget(
        label: 'Крилата ракета',
        cat: 'missile',
        lat: 48.90,
        lon: 38.20,
        heading: 300,
        place: 'Лисичанськ',
        dir: 'на Зх',
        stageLabel: 'наближається',
        distKm: 145,
        etaMin: 11,
        seenMs: now,
      ),
      MapTarget(
        label: 'Балістика',
        cat: 'balist',
        lat: 48.00,
        lon: 37.80,
        heading: 335,
        place: 'Донецьк',
        dir: 'на Пн-Зх',
        stageLabel: 'імовірно',
        distKm: 233,
        etaMin: 3,
        seenMs: now,
      ),
      MapTarget(
        label: 'КАБ',
        cat: 'kab',
        lat: 50.05,
        lon: 37.20,
        heading: 250,
        place: 'Вовчанськ',
        dir: 'на Зх',
        stageLabel: 'наближається',
        distKm: 70,
        etaMin: 8,
        seenMs: now,
      ),
      MapTarget(
        label: 'Авіація (МіГ-31К)',
        cat: 'avia',
        lat: 51.20,
        lon: 35.00,
        heading: 150,
        place: 'Курськ',
        dir: 'на Пд',
        stageLabel: 'патрулює',
        distKm: 160,
        seenMs: now,
      ),
    ];
  }

  @override
  void dispose() {
    _sub?.cancel();
    super.dispose();
  }
}
