import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/foundation.dart';
import 'package:vibration/vibration.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

/// Low-level alarm output: the actual siren sound, vibration and keeping the
/// screen awake. Everything is guarded so it never throws on a platform that
/// lacks a capability (e.g. no vibrator, desktop wakelock, headless tests).
///
/// This is the part the old redesign was missing entirely — the app could only
/// turn a circle red, it could not make any noise.
class AlarmService {
  final AudioPlayer _siren = AudioPlayer(playerId: 'wa_siren');
  final AudioPlayer _ping = AudioPlayer(playerId: 'wa_ping');

  // Alarm loudness, 0..1 (relative to the device volume). User-adjustable.
  double _volume = 0.5;

  // Which threat's siren/ping to play (per-kind, ported from the Reader).
  static const _kinds = {'shahed', 'missile', 'balist', 'kab', 'avia', 'other'};
  // 'impact' — приліт над домом (siren_impact.wav); 'nuclear' — радіаційна небезпека.
  static const _special = {'impact', 'nuclear'};
  static String _norm(String? cat) =>
      cat == 'banderol' ? 'missile' : (_special.contains(cat) || _kinds.contains(cat)) ? cat! : 'other';

  bool _sirenOn = false;
  bool _vibrating = false;
  bool _wakelock = false;

  AlarmService() {
    // Loop the siren; keep playing when the app is briefly in the background.
    _siren.setReleaseMode(ReleaseMode.loop);
    _siren.setPlayerMode(PlayerMode.mediaPlayer);
  }

  /// Live-adjust the alarm loudness (also updates an in-progress siren).
  Future<void> setVolume(double v) async {
    _volume = v.clamp(0.0, 1.0);
    if (_sirenOn && !(_fade?.isActive ?? false)) {
      try {
        await _siren.setVolume(_volume);
      } catch (_) {}
    }
  }

  /// Start the continuous air-raid siren + vibration + keep the screen awake.
  /// [cat] picks the per-kind wail (shahed/missile/balist/kab/avia/other).
  Future<void> startSiren({
    required bool sound,
    required bool vibrate,
    double? volume,
    String? cat,
    bool impact = false, // приліт над домом — окремий звук + жорсткіша вібрація
  }) async {
    if (volume != null) _volume = volume.clamp(0.0, 1.0);
    await _keepAwake(true);
    if (sound && !_sirenOn) {
      _sirenOn = true;
      try {
        // Fade in instead of a full blast out of silence (it frightened Mike half
        // to death over his music): quiet start, full loudness in ~3 s; a strike
        // nearby ramps faster (~1 s) — there every second counts.
        final start = _volume * 0.08;
        await _siren.setVolume(start);
        await _siren.setReleaseMode(ReleaseMode.loop);
        await _siren.play(AssetSource('siren_${_norm(cat)}.wav'), volume: start);
        _fadeIn(Duration(milliseconds: impact ? 1000 : 3000));
      } catch (e) {
        _sirenOn = false;
        debugPrint('siren play failed: $e');
      }
    }
    if (vibrate) await _startVibration(impact: impact);
  }

  Timer? _fade;

  /// Ramp the siren from quiet to [_volume] over [d] (eased, so the first
  /// second stays gentle). [setVolume] during the ramp just changes the goal.
  void _fadeIn(Duration d) {
    _fade?.cancel();
    const step = Duration(milliseconds: 100);
    final n = (d.inMilliseconds / step.inMilliseconds).ceil();
    var i = 0;
    _fade = Timer.periodic(step, (tm) {
      i++;
      if (!_sirenOn) return tm.cancel();
      final p = (i / n).clamp(0.0, 1.0);
      final v = _volume * (0.08 + 0.92 * p * p);
      try {
        _siren.setVolume(v);
      } catch (_) {}
      if (p >= 1) tm.cancel();
    });
  }

  /// One short "approaching" blip (used to pulse while a threat is on the way).
  Future<void> ping({required bool sound, String? cat}) async {
    if (!sound || _sirenOn) return; // no pips over a full siren
    try {
      await _ping.stop();
      await _ping.play(AssetSource('ping_${_norm(cat)}.wav'),
          volume: (_volume * 0.8).clamp(0.0, 1.0));
    } catch (_) {}
  }

  /// Stop everything: siren, vibration, wakelock.
  Future<void> stopAll() async {
    _fade?.cancel();
    if (_sirenOn) {
      _sirenOn = false;
      try {
        await _siren.stop();
      } catch (_) {}
    }
    await _stopVibration();
    await _keepAwake(false);
  }

  // --- vibration --------------------------------------------------------------

  Future<void> _startVibration({bool impact = false}) async {
    if (_vibrating) return;
    try {
      final has = await Vibration.hasVibrator();
      if (has != true) return;
      _vibrating = true;
      // Impact над домом — частый жёсткий стаккато (паника). Иначе — длинный вой/пауза.
      Vibration.vibrate(
        pattern: impact
            ? const [0, 400, 110, 400, 110, 400, 110, 400, 110, 400, 110]
            : const [0, 700, 300, 700, 300, 700, 900],
        intensities: impact
            ? const [0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0]
            : const [0, 255, 0, 255, 0, 255, 0],
        repeat: 0,
      );
    } catch (_) {
      _vibrating = false;
    }
  }

  Future<void> _stopVibration() async {
    if (!_vibrating) return;
    _vibrating = false;
    try {
      await Vibration.cancel();
    } catch (_) {}
  }

  // --- wakelock ---------------------------------------------------------------

  Future<void> _keepAwake(bool on) async {
    if (on == _wakelock) return;
    _wakelock = on;
    try {
      on ? await WakelockPlus.enable() : await WakelockPlus.disable();
    } catch (_) {}
  }

  Future<void> dispose() async {
    await stopAll();
    await _siren.dispose();
    await _ping.dispose();
  }
}
