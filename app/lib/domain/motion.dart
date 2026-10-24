import 'dart:math' as math;

import '../data/ukraine_geo.dart';
import '../models/map_target.dart';

/// Where a target is *right now* and how it moves — shared by the 2D and 3D
/// maps so both show the same flight.
///
/// Three regimes, all at the weapon's real speed (`kmh` from the Reader's
/// weapon catalog):
/// * **flight** (ballistic / КАБ with a launch point and a target): the marker
///   travels From→To since `t0`; [progress] 0..1, [landed] once it arrives.
/// * **launch** (ballistic launch, target unknown): sits at the launch site;
///   [reachKm] — how far it could have got by now (a growing ring).
/// * **cruise** (drones / cruise missiles): dead-reckoning along the heading
///   from the last reported place, capped at 60 km between posts.
class TargetMotion {
  const TargetMotion({
    required this.lat,
    required this.lon,
    required this.heading,
    this.progress,
    this.reachKm = 0,
    this.landed = false,
    this.launch = false,
    this.flightMin,
  });

  final double lat;
  final double lon;
  final double heading;

  /// 0..1 along From→To for a flight, null otherwise.
  final double? progress;
  final double reachKm;
  final bool landed;
  final bool launch;

  /// Total flight time From→To in minutes (flights only).
  final double? flightMin;

  bool get isFlight => progress != null;
}

const double _rad = math.pi / 180;

double kmBetween(double lat1, double lon1, double lat2, double lon2) {
  final dLa = (lat2 - lat1) * _rad, dLo = (lon2 - lon1) * _rad;
  final h = math.sin(dLa / 2) * math.sin(dLa / 2) +
      math.cos(lat1 * _rad) * math.cos(lat2 * _rad) * math.sin(dLo / 2) * math.sin(dLo / 2);
  return 6371 * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h));
}

double bearingDeg(double lat1, double lon1, double lat2, double lon2) {
  final y = math.sin((lon2 - lon1) * _rad) * math.cos(lat2 * _rad);
  final x = math.cos(lat1 * _rad) * math.sin(lat2 * _rad) -
      math.sin(lat1 * _rad) * math.cos(lat2 * _rad) * math.cos((lon2 - lon1) * _rad);
  return (math.atan2(y, x) / _rad + 360) % 360;
}

/// Move [km] from a point along a compass bearing → (lat, lon).
({double lat, double lon}) moveKm(double lat, double lon, double brgDeg, double km) {
  return (
    lat: lat + (km * math.cos(brgDeg * _rad)) / 111,
    lon: lon + (km * math.sin(brgDeg * _rad)) / (111 * math.cos(lat * _rad)),
  );
}

/// Speed in km/min: the weapon's own when the Reader knows it.
double speedKmPerMin(MapTarget t) => t.kmh > 0 ? t.kmh / 60 : speedKmMin(t.cat, t.label);

TargetMotion motionOf(MapTarget t, int nowMs, {bool frozen = false}) {
  final spd = speedKmPerMin(t);
  final from = t.from, to = t.to;

  if (t.launch && from != null) {
    final mins = t.t0 > 0 ? math.max(0, nowMs - t.t0) / 60000 : 0.0;
    return TargetMotion(
      lat: from[0],
      lon: from[1],
      heading: t.heading ?? 0,
      launch: true,
      reachKm: frozen ? 0 : math.min(1200, spd * mins),
    );
  }

  if (from != null && to != null && t.t0 > 0 && (t.cat == 'balist' || t.cat == 'kab')) {
    final dist = math.max(0.1, kmBetween(from[0], from[1], to[0], to[1]));
    final mins = math.max(0, nowMs - t.t0) / 60000;
    final p = frozen ? 1.0 : (spd * mins / dist).clamp(0.0, 1.0);
    return TargetMotion(
      lat: from[0] + (to[0] - from[0]) * p,
      lon: from[1] + (to[1] - from[1]) * p,
      heading: bearingDeg(from[0], from[1], to[0], to[1]),
      progress: p,
      landed: p >= 1,
      flightMin: dist / spd,
    );
  }

  var lat = t.lat, lon = t.lon;
  if (t.heading != null && !frozen) {
    final mins = math.max(0, nowMs - (t.seenMs == 0 ? nowMs : t.seenMs)) / 60000;
    final m = moveKm(t.lat, t.lon, t.heading!, math.min(60.0, spd * mins));
    lat = m.lat;
    lon = m.lon;
  }
  return TargetMotion(lat: lat, lon: lon, heading: t.heading ?? 0);
}
