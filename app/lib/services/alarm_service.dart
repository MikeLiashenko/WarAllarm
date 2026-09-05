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

  // Loud enough to alert, not painful. 0..1.
  static const double _sirenVolume = 0.5;
  static const double _pingVolume = 0.4;

  bool _sirenOn = false;
  bool _vibrating = false;
  bool _wakelock = false;

  AlarmService() {
    // Loop the siren; keep playing when the app is briefly in the background.
    _siren.setReleaseMode(ReleaseMode.loop);
    _siren.setPlayerMode(PlayerMode.mediaPlayer);
  }

  /// Start the continuous air-raid siren + vibration + keep the screen awake.
  Future<void> startSiren({required bool sound, required bool vibrate}) async {
    await _keepAwake(true);
    if (sound && !_sirenOn) {
      _sirenOn = true;
      try {
        await _siren.setVolume(_sirenVolume);
        await _siren.setReleaseMode(ReleaseMode.loop);
        await _siren.play(AssetSource('siren.wav'), volume: _sirenVolume);
      } catch (e) {
        _sirenOn = false;
        debugPrint('siren play failed: $e');
      }
    }
    if (vibrate) await _startVibration();
  }

  /// One short "approaching" blip (used to pulse while a threat is on the way).
  Future<void> ping({required bool sound}) async {
    if (!sound || _sirenOn) return; // no pips over a full siren
    try {
      await _ping.stop();
      await _ping.play(AssetSource('ping.wav'), volume: _pingVolume);
    } catch (_) {}
  }

  /// Stop everything: siren, vibration, wakelock.
  Future<void> stopAll() async {
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

  Future<void> _startVibration() async {
    if (_vibrating) return;
    try {
      final has = await Vibration.hasVibrator();
      if (has != true) return;
      _vibrating = true;
      // Long buzz / short gap, repeating — an urgent, unmistakable pattern.
      Vibration.vibrate(
        pattern: const [0, 700, 300, 700, 300, 700, 900],
        intensities: const [0, 255, 0, 255, 0, 255, 0],
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
