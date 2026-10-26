import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';

import '../domain/motion.dart';
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
    // Old builds remembered the place by its POSITION ('u0', 'u1'…): adding or
    // removing a place made the selection jump to a neighbour (to a muted one).
    // Convert that to the place's own id once.
    final old = RegExp(r'^u(\d+)$').firstMatch(_selectedId);
    if (old != null) {
      final saved = _store.readJsonList('saved_places');
      final i = int.parse(old.group(1)!);
      _selectedId = i < saved.length && saved[i]['id'] != null ? saved[i]['id'].toString() : 'all';
      _store.setString('sel_place', _selectedId);
    }
    // Demo is OFF by default: an alarm app must never show sample threats that
    // could be mistaken for real ones. It can be turned on in Settings.
    _showDemo = _store.getBool('show_demo', fallback: false);
  }

  final RtdbService _rtdb;
  final LocalStore _store;
  StreamSubscription<dynamic>? _sub;

  static const homeKharkiv = (lat: 49.9935, lon: 36.2304);

  ({double lat, double lon}) _home = homeKharkiv;
  ({double lat, double lon}) get home => _home;

  String _homeName = 'Харків · Салтівка';
  String get homeName => _homeName;

  List<MapTarget> _targets = const [];
  List<MapTarget> get targets => _targets;

  /// 🚀 Групи пуску в польоті (прогноз типовим маршрутом) — лише для карти.
  List<MapTarget> _launches = const [];
  List<MapTarget> get launches => _launches;

  List<AnalyticsRow> _rows = const [];
  List<AnalyticsRow> get rows => _rows;

  String _aiText = '';
  String get aiText => _aiText;
  DateTime? _aiAt;
  DateTime? get aiAt => _aiAt;

  int _stateTs = DateTime.now().millisecondsSinceEpoch;
  int get stateTs => _stateTs;

  // ☢️ Радіаційна/ядерна небезпека (з боту). Найвищий пріоритет у тривозі.
  bool _nuclear = false;
  bool get nuclear => _nuclear && fresh; // тільки на живих даних, ніколи в демо
  String _nuclearMsg = '';
  String get nuclearMsg => _nuclearMsg;

  // 🔌 Світло (відключення/відновлення) з каналу про світло.
  String _powerStatus = ''; // 'off' | 'on' | ''
  String _powerArea = '';
  String _powerMsg = '';
  int _powerTs = 0;
  String get powerArea => _powerArea;
  String get powerMsg => _powerMsg;
  List<int> _powerGroupsOff = const [];
  String _powerAt = '';
  /// Черги Києва, які зараз без світла (з постів «єСвітло»).
  List<int> get powerGroupsOff => powerStatus.isEmpty ? const [] : _powerGroupsOff;
  String get powerAt => _powerAt;
  /// 'off' | 'on' | '' — показуємо, лише поки подія свіжа (≤12 год).
  String get powerStatus =>
      (_powerTs > 0 &&
              DateTime.now().millisecondsSinceEpoch - _powerTs < 12 * 3600 * 1000)
          ? _powerStatus
          : '';

  bool _live = false;
  bool get live => _live;

  /// The Reader re-publishes `state` at least every ~20 s while it runs (the
  /// official-alerts loop). A snapshot older than this means the monitor is
  /// down: its targets are history, not a live threat, and must never sound
  /// the siren (a 20-hour-old drone "5 km away" was ringing as a real alarm).
  static const staleAfter = Duration(minutes: 3);

  /// Connected, but the monitor stopped updating.
  bool get stale =>
      _live &&
      DateTime.now().millisecondsSinceEpoch - _stateTs >
          staleAfter.inMilliseconds;

  /// Connected AND the data is current — the only state worth alarming on.
  bool get fresh => _live && !stale;

  /// How old the last snapshot is.
  Duration get dataAge => Duration(
      milliseconds:
          math.max(0, DateTime.now().millisecondsSinceEpoch - _stateTs));

  Timer? _staleTick;
  bool _wasStale = false;

  bool _showDemo = true;
  bool get showDemo => _showDemo;

  /// True when what's on screen is sample data, not a live feed — so the UI can
  /// label it clearly and it is never mistaken for real threats.
  bool get isDemo => !_live && _showDemo && _targets.isNotEmpty;

  String _selectedId = 'all';
  String get selectedId => _selectedId;

  void start() {
    if (_showDemo) {
      _targets = _demoTargets();
      _rows = _demoRows();
    }
    _sub = _rtdb.stream('state').listen(_onData, onError: (_) {});
    // Data can go stale without any new event arriving — re-check on a timer.
    _staleTick = Timer.periodic(const Duration(seconds: 10), (_) {
      if (stale != _wasStale) {
        _wasStale = stale;
        notifyListeners();
      }
    });
  }

  void setShowDemo(bool value) {
    _showDemo = value;
    _store.setBool('show_demo', value);
    if (!_live) {
      _targets = value ? _demoTargets() : const [];
      _rows = value ? _demoRows() : const [];
    }
    notifyListeners();
  }

  void select(String id) {
    _selectedId = id;
    _store.setString('sel_place', id);
    notifyListeners();
  }

  // --- Places (home + Kyiv guest + user-saved) --------------------------------

  /// Only the user's own places (from "Мої місця"). The selector shows
  /// "Всі місця" + each of these separately — no built-in home/Kyiv chips.
  List<MonitoredPlace> get places {
    final list = <MonitoredPlace>[];
    final saved = _store.readJsonList('saved_places');
    for (var i = 0; i < saved.length; i++) {
      final p = saved[i];
      list.add(MonitoredPlace(
        id: (p['id'] ?? 'u$i').toString(), // the place's own id — stable when the list changes
        icon: '📍',
        name: (p['name'] ?? 'Місце').toString(),
        sub: (p['region'] ?? '').toString(),
        lat: (p['lat'] as num).toDouble(),
        lon: (p['lng'] ?? p['lon'] as num).toDouble(),
        notify: p['notify'] != false,
      ));
    }
    return list;
  }

  List<MonitoredPlace> get selectedPlaces {
    final all = places;
    if (_selectedId == 'all') return all;
    final picked = all.where((p) => p.id == _selectedId).toList();
    // A stale selection (e.g. an old 'home'/'kyiv' id) falls back to all.
    return picked.isEmpty ? all : picked;
  }

  /// Places whose bell is on — only these may sound the siren, whichever
  /// place is being viewed (the selector is a view filter, the bell is sound).
  List<MonitoredPlace> get alarmPlaces => places.where((p) => p.notify).toList();

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
    if (stale) return false;
    for (final p in selectedPlaces) {
      for (final t in alarmTargets) {
        final relevant = _km(t, p) <= 40 || (p.zone != null && t.zone == p.zone);
        if (relevant && (_rank[t.stage] ?? 0) >= 3) return true;
      }
    }
    return false;
  }

  /// Цілі для ТРИВОГИ: без приблизних «центр області» (інакше будь-який дрон на
  /// Київщині ставав «над Києвом» і тривога заїдала).
  List<MapTarget> get alarmTargets => _targets.where((t) => !t.approx).toList();

  int get activeCount => _targets.length;

  /// Скільки бортів у повітрі ВСЬОГО: сума ×N по цілях на карті або свіжа (≤45 хв)
  /// оцінка моніторів («На зараз у повітрі +- 60») — що більше.
  int get airborne {
    var n = 0;
    for (final t in _targets) {
      n += t.amount;
    }
    final fresh = _airTs > 0 &&
        DateTime.now().millisecondsSinceEpoch - _airTs < 45 * 60 * 1000;
    return fresh && _airReported > n ? _airReported : n;
  }

  // 🚀 Пуски / оцінка «у повітрі» від моніторів (@ukraine_pyxx через бота).
  int _airReported = 0;
  int _airTs = 0;
  int _launched = 0;
  int _launchedTs = 0;
  String _launchedAt = '';
  String _launchOrigins = '';
  /// Остання хвиля пусків (показуємо 2 год).
  bool get hasRecentLaunch =>
      _launched > 0 &&
      DateTime.now().millisecondsSinceEpoch - _launchedTs < 2 * 3600 * 1000;
  int get launched => _launched;

  // 📡 Джерела моніторингу (state.sources від Reader).
  Map<String, dynamic> _sources = const {};
  int get sourcesCatalog => (_sources['catalog'] as num?)?.toInt() ?? 0;
  int get sourcesActive => (_sources['active'] as num?)?.toInt() ?? 0;
  int get sourcesPending => (_sources['pending'] as num?)?.toInt() ?? 0;
  /// Топ каналів: (username, рівень, цілей, підтверджено, першим).
  List<({String user, String tier, int threats, int confirmed, int first})> get sourcesTop =>
      [
        for (final e in (_sources['top'] as List<dynamic>? ?? const []))
          if (e is Map)
            (
              user: (e['u'] ?? '').toString(),
              tier: (e['tier'] ?? '').toString(),
              threats: (e['threats'] as num?)?.toInt() ?? 0,
              confirmed: (e['confirmed'] as num?)?.toInt() ?? 0,
              first: (e['first'] as num?)?.toInt() ?? 0,
            ),
      ];

  // 🌙 Підсумок ночі (бот збирає з історії каналів).
  Map<String, dynamic> _night = const {};
  bool get hasNight {
    final ts = (_night['ts'] as num?)?.toInt() ?? 0;
    return ts > 0 && DateTime.now().millisecondsSinceEpoch - ts < 14 * 3600 * 1000;
  }
  String get nightPeriod => (_night['period'] ?? '').toString();
  int get nightLaunched => (_night['launched'] as num?)?.toInt() ?? 0;
  int get nightWaves => (_night['waves'] as num?)?.toInt() ?? 0;
  int get nightMaxAir => (_night['maxAir'] as num?)?.toInt() ?? 0;
  String get nightTop => (_night['top'] ?? '').toString();
  int get nightImpacts => (_night['impacts'] as num?)?.toInt() ?? 0;
  String get nightImpactAt => (_night['impactAt'] ?? '').toString();
  String get launchedAt => _launchedAt;
  String get launchOrigins => _launchOrigins;

  /// state.launches → forecast markers that fly the typical route from the launch site.
  static List<MapTarget> _parseLaunches(dynamic raw) {
    if (raw is! List) return const [];
    final now = DateTime.now().millisecondsSinceEpoch;
    final out = <MapTarget>[];
    for (final g in raw.whereType<Map>()) {
      final route = [
        for (final p in (g['route'] as List? ?? const []))
          if (p is List && p.length == 2) [(p[0] as num).toDouble(), (p[1] as num).toDouble()],
      ];
      if (route.length < 2) continue;
      final kmh = (g['kmh'] as num?)?.toDouble() ?? 157;
      final t0 = (g['t0'] as num?)?.toInt() ?? now;
      // Already at the end of the route — nothing left to show.
      if (kmh * (now - t0) / 3600000 >= routeLengthKm(route)) continue;
      final reactive = g['reactive'] == true;
      final origin = (g['origin'] ?? '').toString();
      out.add(MapTarget(
        id: 'launch:${g['id']}',
        label: 'Пуск з $origin${reactive ? ' (реакт.)' : ''}',
        cat: 'shahed',
        lat: route[0][0],
        lon: route[0][1],
        amount: (g['n'] as num?)?.toInt() ?? 1,
        kmh: kmh / 0.85, // motion flies routes at 85 % of the weapon speed
        seenMs: t0,
        route: route,
        forecast: true,
        place: 'прогноз',
        stageLabel: 'прогноз маршруту',
        fromName: origin,
      ));
    }
    return out;
  }

  // --- RTDB ingest ------------------------------------------------------------

  void _onData(dynamic data) {
    if (data is! Map) return;
    _live = true;
    _stateTs = (data['ts'] as num?)?.toInt() ?? _stateTs;
    _wasStale = stale;
    _homeName = (data['homeName'] ?? _homeName).toString();
    final home = data['home'];
    if (home is Map && home['lat'] != null && home['lon'] != null) {
      _home = (
        lat: (home['lat'] as num).toDouble(),
        lon: (home['lon'] as num).toDouble(),
      );
    }
    // A live snapshot is authoritative — replace demo entirely, even when the
    // live state is an empty "all clear".
    final rawT = data['threats'];
    final t = <MapTarget>[];
    if (rawT is List) {
      for (final e in rawT) {
        if (e is Map) t.add(MapTarget.fromMap(e));
      }
    } else if (rawT is Map) {
      rawT.forEach((_, v) {
        if (v is Map) t.add(MapTarget.fromMap(v));
      });
    }
    _targets = t;
    _launches = _parseLaunches(data['launches']);

    final rawR = data['rows'];
    _rows = rawR is List
        ? rawR.whereType<Map>().map(AnalyticsRow.fromMap).toList()
        : const [];

    _aiText = (data['aiText'] ?? '').toString();
    final aiAt = data['aiAt'];
    if (aiAt is num) _aiAt = DateTime.fromMillisecondsSinceEpoch(aiAt.toInt());

    final nuc = data['nuclear'];
    if (nuc is Map) {
      _nuclear = nuc['active'] == true;
      _nuclearMsg = (nuc['msg'] ?? '').toString();
    } else {
      _nuclear = false;
      _nuclearMsg = '';
    }

    // 📡 Хаб джерел (Reader): скільки каналів моніторингу, хто що дав.
    final srcs = data['sources'];
    if (srcs is Map) _sources = Map<String, dynamic>.from(srcs);

    final night = data['night'];
    if (night is Map) _night = Map<String, dynamic>.from(night);

    final air = data['air'];
    if (air is Map) {
      _airReported = (air['reported'] as num?)?.toInt() ?? 0;
      _airTs = (air['ts'] as num?)?.toInt() ?? 0;
      _launched = (air['launched'] as num?)?.toInt() ?? 0;
      _launchedTs = (air['launchedTs'] as num?)?.toInt() ?? 0;
      _launchedAt = (air['launchedAt'] ?? '').toString();
      _launchOrigins = (air['origins'] ?? '').toString();
    }

    final pw = data['power'];
    if (pw is Map) {
      _powerStatus = (pw['status'] ?? '').toString();
      _powerArea = (pw['area'] ?? '').toString();
      _powerMsg = (pw['msg'] ?? '').toString();
      _powerTs = (pw['ts'] as num?)?.toInt() ?? 0;
      final g = pw['groupsOff'];
      _powerGroupsOff = g is List ? g.whereType<num>().map((e) => e.toInt()).toList() : const [];
      _powerAt = (pw['at'] ?? '').toString();
    }

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
    _staleTick?.cancel();
    super.dispose();
  }
}
