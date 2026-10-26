import 'dart:async';

import 'package:flutter/foundation.dart';

import '../domain/motion.dart';
import '../services/official_alerts_service.dart';

/// Polls the official per-oblast air-raid status every ~25 s and exposes it as
/// a map of oblast polygon name -> level ('red' | 'yellow'). No token needed.
class AlertsController extends ChangeNotifier {
  AlertsController({OfficialAlertsService? service})
      : _svc = service ?? OfficialAlertsService() {
    _poll();
    _timer = Timer.periodic(const Duration(seconds: 25), (_) => _poll());
  }

  final OfficialAlertsService _svc;
  Timer? _timer;

  Map<String, String> _levels = const {};
  Map<String, String> get levels => _levels;

  // Raion-level split (like the official map): "X район" -> red/yellow, and the
  // oblasts that are under alarm as a WHOLE (filled entirely).
  Map<String, String> _raions = const {};
  Map<String, String> get raions => _raions;
  Map<String, String> _oblastWhole = const {};
  Map<String, String> get oblastWhole => _oblastWhole;

  bool _ok = false;
  bool get ok => _ok;

  DateTime? _at;
  DateTime? get updatedAt => _at;

  int get redCount => _levels.values.where((v) => v == 'red').length;
  int get yellowCount => _levels.values.where((v) => v == 'yellow').length;

  Future<void> _poll() async {
    try {
      final snap = await _svc.fetchFull();
      _levels = snap.oblasts;
      _raions = snap.raions;
      _oblastWhole = snap.oblastWhole;
      _ok = true;
      _at = DateTime.now();
      setMotionAlarmOblasts(_levels.keys.toSet()); // прогноз польоту не заходить у зони відбою
    } catch (_) {
      _ok = false;
      setMotionAlarmOblasts(null);
    }
    notifyListeners();
  }

  Future<void> refresh() => _poll();

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }
}
