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
  DateTime? _impactAckUntil; // користувач натиснув «Стоп» ПІД ЧАС прильоту — тиша на вікно

  /// 'calm' | 'warn' | 'alarm'
  String _level = 'calm';
  String get level => _level;

  String _title = 'Спокійно';
  String get title => _title;

  String _detail = '';
  String get detail => _detail;

  DateTime? _snoozeUntil;
  bool get snoozed =>
      _snoozeUntil != null && DateTime.now().isBefore(_snoozeUntil!);
  int get snoozeLeftSec => _snoozeUntil == null
      ? 0
      : _snoozeUntil!.difference(DateTime.now()).inSeconds.clamp(0, 3600);

  // --- user actions -----------------------------------------------------------

  /// "Я в укритті / Стоп" — silence the current alarm for 90 s. It re-arms
  /// automatically afterwards (safety: never silence forever), and immediately
  /// if the danger gets worse (a strike / overhead).
  void hush() {
    _snoozeUntil =
        DateTime.now().add(Duration(seconds: _settings.snoozeSeconds));
    // «Стоп» під час прильоту — свідоме підтвердження: гасимо сильний сигнал на вікно.
    if (_impactRinging) {
      _impactAckUntil =
          DateTime.now().add(Duration(seconds: _settings.snoozeSeconds));
    }
    _svc.stopAll();
    _ringing = false;
    _impactRinging = false;
    notifyListeners();
    _reevaluate();
  }

  void resume() {
    _snoozeUntil = null;
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
    // Never alarm on sample/demo data — it must never be mistaken for real.
    final demo = _map.isDemo;

    // Найсильніший випадок: приліт/над районом за ≤7 км від обраного місця.
    // Перебиває звичайний snooze — прилёт поруч не можна пропустити. Але свідомий
    // «Стоп» під час прильоту (_impactAckUntil) поважаємо на вікно snooze.
    if (!demo && _impactNearHome()) {
      final acked = _impactAckUntil != null &&
          DateTime.now().isBefore(_impactAckUntil!);
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
    _impactAckUntil = null;
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

    // Respect the user's "Я в укритті — стоп" for the full snooze window — even
    // for a close/critical target. It auto-re-arms after snoozeSeconds, so the
    // alarm is never silenced for long, but the STOP button always works now.
    final snoozing = snoozed;

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

    // Danger gone → drop any lingering snooze so the next threat rings clean.
    if (newLevel == 'calm') _snoozeUntil = null;

    if (newLevel != _level) {
      _level = newLevel;
    }
    notifyListeners();
  }

  /// Приліт/над районом (стадія Impact/Overhead) за ≤7 км від обраного місця.
  bool _impactNearHome() {
    for (final pl in _map.selectedPlaces) {
      for (final t in placeThreats(pl, _map.targets)) {
        final d = t.pdist ?? double.infinity;
        if (d <= 7 && (t.stage == 'Impact' || t.stage == 'Overhead')) return true;
      }
    }
    return false;
  }

  /// Рядок опису для сильного сигналу «приліт поруч».
  String _impactDetail() {
    for (final pl in _map.selectedPlaces) {
      for (final t in placeThreats(pl, _map.targets)) {
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
      for (final t in placeThreats(pl, _map.targets)) {
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
      final r = riskOf(pl, _map.targets);
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
