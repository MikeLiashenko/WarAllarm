import 'package:flutter/foundation.dart';

import '../data/oblast_lookup.dart';
import '../domain/area_history.dart';
import '../models/analytics_row.dart';
import '../models/monitored_place.dart';
import '../services/rtdb_service.dart';

/// 📈 Історія тривог по районах для «моїх місць» (Firebase `areaLog/<область>`).
/// Тягнемо лише області своїх місць, раз на 10 хв (не стрімом — трафік мізерний).
class AreaLogController extends ChangeNotifier {
  AreaLogController(this._rtdb);
  final RtdbService _rtdb;

  static const _ttl = Duration(minutes: 10);
  final Map<String, Map<dynamic, dynamic>> _nodes = {};
  final Map<String, DateTime> _fetched = {};
  final Set<String> _loading = {};
  bool _failed = false;

  /// Хоч одну область не вдалося завантажити (немає мережі / правил доступу).
  bool get failed => _failed;
  bool get loading => _loading.isNotEmpty;

  /// Завантажити (або оновити застарілі) області місць [places].
  void ensure(List<MonitoredPlace> places) {
    final now = DateTime.now();
    for (final p in places) {
      final obl = OblastLookup.instance.nameAt(p.lat, p.lon);
      if (obl == null || _loading.contains(obl)) continue;
      final at = _fetched[obl];
      if (at != null && now.difference(at) < _ttl) continue;
      _load(obl);
    }
  }

  static String _day(DateTime t) =>
      '${t.year.toString().padLeft(4, '0')}-${t.month.toString().padLeft(2, '0')}-${t.day.toString().padLeft(2, '0')}';

  Future<void> _load(String obl) async {
    _loading.add(obl);
    try {
      final path = 'areaLog/${Uri.encodeComponent(obl)}';
      final have = _nodes[obl];
      if (have == null || have.isEmpty) {
        // Перший раз — уся історія області (≤60 днів).
        final v = await _rtdb.getOnce(path);
        _nodes[obl] = v is Map ? Map<dynamic, dynamic>.of(v) : <dynamic, dynamic>{};
      } else {
        // Далі — лише останні 3 дні (тривога триває ≤30 год і пишеться в день початку).
        final now = DateTime.now();
        final upd = Map<dynamic, dynamic>.of(have);
        for (var i = 0; i < 3; i++) {
          final d = _day(now.subtract(Duration(days: i)));
          final v = await _rtdb.getOnce('$path/$d');
          if (v is Map) {
            upd[d] = v;
          } else {
            upd.remove(d);
          }
        }
        // Старше 61 дня сервер видаляє — і ми теж.
        final cut = _day(now.subtract(const Duration(days: 61)));
        upd.removeWhere((k, _) => k.toString().compareTo(cut) < 0);
        _nodes[obl] = upd;
      }
      _version++;
      _fetched[obl] = DateTime.now();
      _failed = false;
    } catch (_) {
      _failed = true;
      _fetched[obl] = DateTime.now().subtract(_ttl - const Duration(minutes: 1)); // повтор за хвилину
    } finally {
      _loading.remove(obl);
      notifyListeners();
    }
  }

  /// Чи є історія хоч для одного з місць.
  bool hasDataFor(List<MonitoredPlace> places) => places.any((p) {
        final obl = OblastLookup.instance.nameAt(p.lat, p.lon);
        return obl != null && (_nodes[obl]?.isNotEmpty ?? false);
      });

  int _version = 0;
  String? _memoKey;
  List<AnalyticsRow> _memo = const [];

  /// Записи аналітики всіх [places] (zone = назва місця). Кеш — до нових даних
  /// чи іншого списку місць (екран перебудовується щосекунди).
  List<AnalyticsRow> rowsFor(List<MonitoredPlace> places) {
    final now = DateTime.now();
    final key = '$_version|${now.hour}:${now.minute}|'
        '${places.map((p) => '${p.name}@${p.lat},${p.lon}').join(';')}';
    if (key == _memoKey) return _memo;
    final rows = <AnalyticsRow>[];
    for (final p in places) {
      final obl = OblastLookup.instance.nameAt(p.lat, p.lon);
      if (obl == null) continue;
      rows.addAll(placeRows(p, _nodes[obl]));
    }
    _memoKey = key;
    _memo = rows;
    return rows;
  }
}
