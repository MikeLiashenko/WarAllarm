import 'dart:async';

import 'package:flutter/foundation.dart';

import '../domain/situation.dart';
import '../models/map_target.dart';
import '../services/alarm_service.dart';
import 'map_data_controller.dart';
import 'settings_controller.dart';

/// Decides *when* to raise the alarm and drives [AlarmService].
///
/// It watches the live threat data ([MapDataController]) and the user's settings
/// and reacts on its own — no screen has to be open for the logic to run. The
/// severity is measured by distance to the user's **selected** places (via
/// [riskOf]), so it works wherever the user actually is (Kharkiv *or* Kyiv), not
/// only relative to the fixed home the monitor was started with.
class AlarmController extends ChangeNotifier {
  AlarmController(this._map, this._settings, {AlarmService? service})
      : _svc = service ?? AlarmService() {
    _map.addListener(_reevaluate);
    _settings.addListener(_reevaluate);
    _timer = Timer.periodic(const Duration(seconds: 3), (_) => _reevaluate());
    _reevaluate();
  }

  final MapDataController _map;
  final SettingsController _settings;
  final AlarmService _svc;
  Timer? _timer;

  // --- public state (for the UI) ---------------------------------------------

  bool _ringing = false;
  bool get ringing => _ringing;

  bool _impactRinging = false; // зараз грає сильний сигнал «приліт над домом»
  bool get impactAlarm => _impactRinging;

  bool _nuclearRinging = false; // ☢️ активна радіаційна/ядерна небезпека
  bool get nuclearAlarm => _nuclearRinging;
  bool _nuclearAcked = false; // «Стоп» під час радіаційної небезпеки — тиша до її кінця

  /// 'calm' | 'warn' | 'alarm'
  String _level = 'calm';
  String get level => _level;

  String _title = 'Спокійно';
  String get title => _title;

  String _detail = '';
  String get detail => _detail;

  /// «Я в укритті — стоп» silences the whole alarm episode: no auto-repeat.
  /// It re-arms only once the danger is over (calm for [_calmToClear]), so the
  /// next, separate attack rings normally.
  bool _silenced = false;
  bool get silenced => _silenced;
  DateTime? _calmSince; // when the level last turned calm (episode end)
  static const _calmToClear = Duration(seconds: 60);

  // --- user actions -----------------------------------------------------------

  /// "Я в укритті / Стоп" — the siren stops and STAYS off for this episode
  /// (no repeat). A new attack after the all-clear rings again.
  void hush() {
    _silenced = true;
    // Радіація — окреме підтвердження: тиша, доки сигнал не зникне.
    if (_nuclearRinging) _nuclearAcked = true;
    _svc.stopAll();
    _ringing = false;
    _impactRinging = false;
    _nuclearRinging = false;
    notifyListeners();
    _reevaluate();
  }

  void resume() {
    _silenced = false;
    notifyListeners();
    _reevaluate();
  }

  /// Play the siren for a few seconds so the user can check sound + vibration
  /// work on their device — without waiting for a real raid. No-op while a real
  /// alarm is already ringing.
  Future<void> testSiren() async {
    if (_ringing) return;
    await _svc.startSiren(
        sound: true,
        vibrate: _settings.vibration,
        volume: _settings.alarmVolume,
        cat: 'shahed');
    await Future<void>.delayed(const Duration(seconds: 5));
    if (!_ringing) await _svc.stopAll();
  }

  // --- core logic -------------------------------------------------------------

  void _reevaluate() {
    // Never alarm on sample/demo data — it must never be mistaken for real —
    // nor on a stale snapshot (monitor down: those targets are history).
    final demo = _map.isDemo || _map.stale;

    // ☢️ Найвищий пріоритет — радіаційна/ядерна небезпека (окремий екран і звук).
    if (_map.nuclear) {
      final acked = _nuclearAcked;
      _title = '☢️ РАДІАЦІЙНА НЕБЕЗПЕКА';
      _detail = _map.nuclearMsg.isNotEmpty
          ? _map.nuclearMsg
          : 'Зайдіть у приміщення, зачиніть вікна й двері.';
      _level = 'alarm';
      if (!acked && !_nuclearRinging) {
        _nuclearRinging = true;
        _impactRinging = false;
        _ringing = true;
        _svc.stopAll();
        _svc.startSiren(
          sound: _settings.soundAlerts,
          vibrate: _settings.vibration,
          volume: 1.0,
          cat: 'nuclear',
          impact: true,
        );
      }
      notifyListeners();
      return;
    }
    if (_nuclearRinging) {
      _nuclearRinging = false;
      _nuclearAcked = false;
      _ringing = false;
      _svc.stopAll();
    }

    // Найсильніший випадок: приліт/над районом за ≤7 км від обраного місця.
    // Після «Стоп» не повторюємо — користувач уже в укритті (епізод заглушено).
    if (!demo && _impactNearHome()) {
      final acked = _silenced;
      _calmSince = null; // a strike nearby is never "calm"
      _title = '💥 ПРИЛІТ ПОРУЧ';
      _detail = _impactDetail();
      _level = 'alarm';
      if (!acked && !_impactRinging) {
        _impactRinging = true;
        _ringing = true;
        _svc.stopAll();
        _svc.startSiren(
          sound: _settings.soundAlerts,
          vibrate: _settings.vibration,
          volume: _settings.alarmVolume.clamp(0.7, 1.0),
          cat: 'impact',
          impact: true,
        );
      }
      notifyListeners();
      return;
    }
    // Прилёт минув → повертаємось до звичайної логіки (нижче вирішить рівень).
    if (_impactRinging) {
      _impactRinging = false;
      _ringing = false;
      _svc.stopAll();
    }

    final worst = demo ? null : _worstRisk();

    final score = worst?.score ?? 0;
    final nearestKm = demo ? double.infinity : _nearestKm();
    final radius = _settings.alarmRadiusKm;
    // Alarm if the situation is severe OR a target is within the user's chosen
    // alarm radius of a selected place.
    final newLevel = (score >= 55 || nearestKm <= radius)
        ? 'alarm'
        : (score >= 35 || nearestKm <= radius * 1.7)
            ? 'warn'
            : 'calm';

    _title = switch (newLevel) {
      'alarm' => 'ТРИВОГА',
      'warn' => 'Загроза поруч',
      _ => 'Спокійно',
    };
    _detail = worst == null ? '' : _describe(worst);

    final sound = _settings.soundAlerts;
    final vibrate = _settings.vibration;
    _svc.setVolume(_settings.alarmVolume); // keep loudness in sync live

    // «Я в укритті — стоп» holds for the whole episode — no repeats.
    final snoozing = _silenced;

    if (newLevel == 'alarm' && !snoozing) {
      if (!_ringing) {
        _ringing = true;
        _svc.startSiren(
            sound: sound,
            vibrate: vibrate,
            volume: _settings.alarmVolume,
            cat: worst?.top?.cat);
      }
    } else {
      if (_ringing) {
        _ringing = false;
        _svc.stopAll();
      }
      // No "approaching" pings — only the full siren on a real alarm.
    }

    // Episode over → re-arm for the next attack. Only after it has stayed calm
    // for a while: a single empty/partial update must not end the episode.
    if (newLevel == 'calm') {
      _calmSince ??= DateTime.now();
      if (DateTime.now().difference(_calmSince!) >= _calmToClear) {
        _silenced = false;
      }
    } else {
      _calmSince = null;
    }

    if (newLevel != _level) {
      _level = newLevel;
    }
    notifyListeners();
  }

  /// Приліт/над районом (стадія Impact/Overhead) за ≤7 км від обраного місця.
  bool _impactNearHome() {
    for (final pl in _map.selectedPlaces) {
      for (final t in placeThreats(pl, _map.alarmTargets)) {
        final d = t.pdist ?? double.infinity;
        if (d <= 7 && (t.stage == 'Impact' || t.stage == 'Overhead')) return true;
      }
    }
    return false;
  }

  /// Рядок опису для сильного сигналу «приліт поруч».
  String _impactDetail() {
    for (final pl in _map.selectedPlaces) {
      for (final t in placeThreats(pl, _map.alarmTargets)) {
        final d = t.pdist ?? double.infinity;
        if (d <= 7 && (t.stage == 'Impact' || t.stage == 'Overhead')) {
          final where = t.place.isNotEmpty ? ' · ${t.place}' : '';
          return '${t.label} · ${d.round()} км від «${pl.name}»$where';
        }
      }
    }
    return 'Вибух поруч. В укриття, подалі від вікон.';
  }

  /// Nearest target distance (km) to any selected place, or infinity.
  double _nearestKm() {
    var best = double.infinity;
    for (final pl in _map.selectedPlaces) {
      for (final t in placeThreats(pl, _map.alarmTargets)) {
        final d = t.pdist ?? double.infinity;
        if (d < best) best = d;
      }
    }
    return best;
  }

  /// Worst per-place risk across the currently selected places.
  RiskResult? _worstRisk() {
    RiskResult? worst;
    for (final pl in _map.selectedPlaces) {
      final r = riskOf(pl, _map.alarmTargets);
      if (worst == null || r.score > worst.score) worst = r;
    }
    return (worst == null || worst.score <= 0) ? null : worst;
  }

  String _describe(RiskResult r) {
    final MapTarget? t = r.top;
    if (t == null) return r.advice;
    final bits = <String>[
      '${t.label}${t.amount > 1 ? ' ×${t.amount}' : ''}',
      if (t.pdist != null) '${t.pdist!.round()} км від «${r.place.name}»',
      fmtEta(etaNow(t, _map.stateTs)),
      if (t.place.isNotEmpty) t.place,
    ];
    return bits.join(' · ');
  }

  @override
  void dispose() {
    _timer?.cancel();
    _map.removeListener(_reevaluate);
    _settings.removeListener(_reevaluate);
    _svc.dispose();
    super.dispose();
  }
}
