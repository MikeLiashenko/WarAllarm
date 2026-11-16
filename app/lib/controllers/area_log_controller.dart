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

  Future<void> _load(String obl) async {
    _loading.add(obl);
    try {
      final v = await _rtdb.getOnce('areaLog/${Uri.encodeComponent(obl)}');
      _nodes[obl] = v is Map ? v : const {};
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

  /// Записи аналітики всіх [places] (zone = назва місця).
  List<AnalyticsRow> rowsFor(List<MonitoredPlace> places) {
    final rows = <AnalyticsRow>[];
    for (final p in places) {
      final obl = OblastLookup.instance.nameAt(p.lat, p.lon);
      if (obl == null) continue;
      rows.addAll(placeRows(p, _nodes[obl]));
    }
    return rows;
  }
}
