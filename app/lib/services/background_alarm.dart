import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// State of the Android background location watcher (native GeoWatchService).
class GeoWatchStatus {
  final bool enabled; // the user turned it on
  final bool running; // the service is alive right now
  final bool location; // location permission granted
  final bool background; // «Allow all the time» — survives reboots, starts by itself
  final bool battery; // battery optimisation is off for WarAlarm
  final double? lat, lon; // last fix
  final int at; // last fix time, ms (0 — none yet)

  const GeoWatchStatus({
    this.enabled = false,
    this.running = false,
    this.location = false,
    this.background = false,
    this.battery = false,
    this.lat,
    this.lon,
    this.at = 0,
  });

  factory GeoWatchStatus.fromMap(Object? m) {
    if (m is! Map) return const GeoWatchStatus();
    double? d(Object? v) => v is num ? v.toDouble() : null;
    return GeoWatchStatus(
      enabled: m['enabled'] == true,
      running: m['running'] == true,
      location: m['location'] == true,
      background: m['background'] == true,
      battery: m['battery'] == true,
      lat: d(m['lat']),
      lon: d(m['lon']),
      at: (m['at'] as num?)?.toInt() ?? 0,
    );
  }

  /// A fix fresh enough to be «where you are now».
  bool get hasFreshFix =>
      lat != null && lon != null && DateTime.now().millisecondsSinceEpoch - at < 30 * 60 * 1000;
}

/// 🛰 Background watcher (Android): the phone's location + alarms with the app closed.
///
/// It is our own Kotlin foreground service (GeoWatchService.kt) — no Flutter engine
/// and no third-party background plugin (flutter_background_service crashed at launch
/// on a Galaxy Tab S10+). Nothing native runs until the user turns it on, and every
/// call here is guarded, so a device that rejects it can never take the app down.
class BackgroundAlarm {
  static const prefEnabled = 'set_bg_alarm';
  static const _ch = MethodChannel('waralarm/geo');

  static bool get supported => !kIsWeb && defaultTargetPlatform == TargetPlatform.android;

  static Future<GeoWatchStatus> _call(String method, [Object? args]) async {
    if (!supported) return const GeoWatchStatus();
    try {
      return GeoWatchStatus.fromMap(await _ch.invokeMethod<Object?>(method, args));
    } catch (_) {
      return const GeoWatchStatus();
    }
  }

  /// At app start: if it was on but got killed (force-stop, update), start it again.
  static Future<void> init() async {
    await _call('status');
  }

  static Future<GeoWatchStatus> status() => _call('status');

  static Future<bool> isRunning() async => (await status()).running;

  /// Asks for location (+ notifications) and starts the watcher. True — it runs.
  static Future<bool> enable({double? radiusKm, double? homeLat, double? homeLon, String? homeName}) async {
    final s = await _call('enable', {
      'radius': radiusKm,
      'homeLat': homeLat,
      'homeLon': homeLon,
      'homeName': homeName,
    });
    return s.enabled && s.location;
  }

  /// «Allow all the time»: needed to come back by itself after a reboot.
  static Future<GeoWatchStatus> requestBackground() => _call('requestBackground');

  static Future<void> disable() async {
    await _call('disable');
  }

  /// Radius / home point (the fallback when the phone has no fix) for the service.
  static Future<void> configure({double? radiusKm, double? homeLat, double? homeLon, String? homeName}) async {
    if (!supported) return;
    try {
      await _ch.invokeMethod('config', {
        'radius': radiusKm,
        'homeLat': homeLat,
        'homeLon': homeLon,
        'homeName': homeName,
      });
    } catch (_) {}
  }

  static Future<void> openBatterySettings() async {
    if (!supported) return;
    try {
      await _ch.invokeMethod('battery');
    } catch (_) {}
  }

  /// The in-app siren handles alerts while we're open (it watches «📍 Я зараз» too),
  /// so the service stays quiet then and doesn't double up.
  static void setAppForeground(bool front) {
    if (!supported) return;
    _ch.invokeMethod('foreground', front).catchError((_) => null);
  }
}
