import 'dart:async';

import 'package:flutter/foundation.dart';

import '../domain/accuracy.dart';
import '../domain/alarm_decision.dart';
import '../domain/probability.dart';
import '../models/monitored_place.dart';
import '../services/local_store.dart';
import 'map_data_controller.dart';
import 'settings_controller.dart';

/// Tracks how good our attack-probability forecast actually is.
///
/// Every few minutes (only on live, non-demo data) it records the probability
/// we predict for home. If a real alarm materialises for home within the next
/// 30 minutes, that sample counts as a "hit"; otherwise a "miss". The resolved
/// log feeds [computeAccuracy] to show a hit rate + calibration over time.
class AccuracyController extends ChangeNotifier {
  AccuracyController(this._map, this._settings, this._store) {
    _load();
    _timer = Timer.periodic(const Duration(seconds: 60), (_) => _tick());
    _tick();
  }

  static const _kResolved = 'acc_resolved';
  static const _kPending = 'acc_pending';
  static const _kLast = 'acc_last_sample';
  static const _sampleEverySec = 300; // 5 min between predictions
  static const _windowSec = 1800; // 30 min to materialise

  final MapDataController _map;
  final SettingsController _settings;
  final LocalStore _store;
  Timer? _timer;

  List<AccSample> _resolved = [];
  List<Map<String, dynamic>> _pending = [];
  int _lastSampleMs = 0;

  AccuracyStats get stats => computeAccuracy(_resolved);
  int get qualityPct => qualityScore(stats.brier);

  void _load() {
    _resolved = _store
        .readJsonList(_kResolved)
        .map((m) => AccSample((m['p'] as num).toInt(), (m['hit'] as bool?) ?? false))
        .toList();
    _pending = _store.readJsonList(_kPending);
    _lastSampleMs = _store.getDouble(_kLast).toInt();
  }

  MonitoredPlace get _home => MonitoredPlace(
        id: 'home',
        icon: '🏠',
        name: _map.homeName,
        lat: _map.home.lat,
        lon: _map.home.lon,
      );

  void _tick() {
    // Only measure against real, live data — never demo or an empty feed.
    if (_map.isDemo || !_map.live) return;

    final now = DateTime.now().millisecondsSinceEpoch;
    final home = _home;
    final radius = _settings.alarmRadiusKm;
    final alarmNow = evaluateAlarm(
          targets: _map.targets,
          places: [home],
          radiusKm: radius,
        ).level ==
        'alarm';

    var changed = false;

    // Resolve pending samples: a hit if an alarm fired within the window.
    final stillPending = <Map<String, dynamic>>[];
    for (final s in _pending) {
      final dl = (s['dl'] as num).toInt();
      if (alarmNow) {
        _resolved.add(AccSample((s['p'] as num).toInt(), true));
        changed = true;
      } else if (now >= dl) {
        _resolved.add(AccSample((s['p'] as num).toInt(), false));
        changed = true;
      } else {
        stillPending.add(s);
      }
    }
    _pending = stillPending;

    // Take a new prediction every few minutes.
    if (now - _lastSampleMs >= _sampleEverySec * 1000) {
      final pred = probabilityFor(home, _map.targets).percent;
      _pending.add({'t': now, 'p': pred, 'dl': now + _windowSec * 1000});
      _lastSampleMs = now;
      changed = true;
    }

    if (changed) {
      // Cap the log so it stays small.
      if (_resolved.length > 400) {
        _resolved = _resolved.sublist(_resolved.length - 400);
      }
      _persist();
      notifyListeners();
    }
  }

  void _persist() {
    _store.writeJsonList(
        _kResolved, [for (final s in _resolved) {'p': s.p, 'hit': s.hit}]);
    _store.writeJsonList(_kPending, _pending);
    _store.setDouble(_kLast, _lastSampleMs.toDouble());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }
}
