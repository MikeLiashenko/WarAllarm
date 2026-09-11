import 'package:flutter/foundation.dart';

import '../services/local_store.dart';
import 'alarm_controller.dart';
import 'map_data_controller.dart';

/// A pending "Чи було чути вибух?" question, raised once right after a real
/// alarm episode ends (the siren stopped and the situation dropped below alarm).
class BoomPrompt {
  final DateTime endedAt;
  final String place;
  const BoomPrompt(this.endedAt, this.place);
}

/// Crowd feedback after a raid: when an alarm episode ends we ask the user, in a
/// small window, whether they actually heard an explosion. The answer is stored
/// locally only (never uploaded) — a light, honest record of what really
/// happened near them, mirroring the bot's optional "чи було чути вибух?".
class BoomController extends ChangeNotifier {
  BoomController(this._alarm, this._map, this._store) {
    _wasAlarm = _alarm.level == 'alarm';
    _alarm.addListener(_onAlarm);
  }

  static const _kLog = 'boom_feedback';

  final AlarmController _alarm;
  final MapDataController _map;
  final LocalStore _store;

  bool _wasAlarm = false;
  String _episodePlace = '';

  BoomPrompt? _pending;
  BoomPrompt? get pending => _pending;

  int get logged => _store.readJsonList(_kLog).length;

  void _onAlarm() {
    final isAlarm = _alarm.level == 'alarm';
    // Never on demo data — it must never look like a real event happened.
    if (isAlarm && !_map.isDemo) {
      _wasAlarm = true;
      _episodePlace = _map.selectedPlaces.isNotEmpty
          ? _map.selectedPlaces.first.name
          : _map.homeName;
    } else if (_wasAlarm && !isAlarm) {
      // Alarm just ended → ask once (don't stack prompts).
      _wasAlarm = false;
      _pending ??= BoomPrompt(DateTime.now(), _episodePlace);
      notifyListeners();
    }
  }

  /// User answered the "was there an explosion?" question.
  Future<void> answer(bool heard) async {
    final p = _pending;
    _pending = null;
    notifyListeners();
    if (p == null) return;
    final log = _store.readJsonList(_kLog)
      ..add({
        't': p.endedAt.millisecondsSinceEpoch,
        'heard': heard,
        'place': p.place,
      });
    if (log.length > 200) log.removeRange(0, log.length - 200);
    await _store.writeJsonList(_kLog, log);
  }

  /// User skipped — the question is optional.
  void dismiss() {
    if (_pending == null) return;
    _pending = null;
    notifyListeners();
  }

  @override
  void dispose() {
    _alarm.removeListener(_onAlarm);
    super.dispose();
  }
}
