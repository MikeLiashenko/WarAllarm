import 'dart:math' as math;

import '../data/oblast_lookup.dart';
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
/// * **cruise** (drones / cruise missiles): the Reader sends a typical `route`
///   (north-east → Kyiv, south/east → west via Умань–Вінниця–Хмельницький, or
///   along a real course). The marker flies it for [kRouteMin] minutes after a
///   post and never into an oblast without an alarm; every new post puts it
///   where the channel says and it flies on from there. When it has stopped,
///   [uncertKm] — a ring of where it may be — grows until the next post.
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
    this.uncertKm = 0,
    this.quietMin = 0,
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

  /// Cruise target: radius (km) of where it may be by now — grows with silence.
  final double uncertKm;

  /// Minutes since the last report of this target.
  final double quietMin;

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

/// How long a target keeps flying its typical route after a post (then it stops
/// and the uncertainty ring grows until the next post).
const double kRouteMin = 20;

/// Oblasts (map polygon names) with an official alarm now; null = unknown.
/// Set by the alerts controller. A real drone over an oblast means an alarm there,
/// so the predicted flight stops at the edge of an all-clear oblast.
Set<String>? motionAlarmOblasts;
int _zonesVersion = 0;

/// Raion-level split of [motionAlarmOblasts]: oblasts alarmed as a WHOLE, and the
/// alarmed raions ('X район') of the rest. Null/empty → oblast level only.
Set<String>? _alarmWhole;
Set<String>? _alarmRaions;

void setMotionAlarmOblasts(Set<String>? zones, {Set<String>? whole, Set<String>? raions}) {
  motionAlarmOblasts = zones;
  _alarmWhole = whole;
  _alarmRaions = raions?.map(_napo).toSet();
  _zonesVersion++;
  _limitCache.clear();
}

String _napo(String s) => s.replaceAll('’', "'").replaceAll('ʼ', "'").replaceAll('`', "'");

/// Is this point (inside oblast [obl]) under an official alarm right now? An oblast
/// with only some raions alarmed counts only in those raions — a drone must not fly
/// on into the all-clear part. Unknown alert data → true (nothing to stop at).
bool _inAlarm(String obl, double lat, double lon) {
  final zones = motionAlarmOblasts;
  if (zones == null || zones.isEmpty) return true;
  if (!zones.contains(obl)) return false;
  final whole = _alarmWhole, raions = _alarmRaions;
  if (whole == null || raions == null || raions.isEmpty || whole.contains(obl)) return true;
  final r = OblastLookup.raions.nameAt(lat, lon);
  return r == null || raions.contains(_napo(r));
}

/// Would a target at [lat],[lon] still be somewhere it may fly: inside Ukraine and
/// under alarm (or it was already outside — launched from Russia / over the sea).
bool motionAllowed(double fromLat, double fromLon, double lat, double lon) {
  final lookup = OblastLookup.instance;
  final to = lookup.nameAt(lat, lon);
  if (to == null) return lookup.nameAt(fromLat, fromLon) == null;
  return _inAlarm(to, lat, lon) || !_inAlarmAt(fromLat, fromLon);
}

/// How far along [heading] a course line may reach without crossing the all-clear
/// border or leaving Ukraine (≤ [maxKm], 3-km steps).
double allowedLeadKm(double lat, double lon, double heading, double maxKm) {
  var ok = 0.0;
  for (var km = 3.0; km <= maxKm; km += 3) {
    final p = moveKm(lat, lon, heading, km);
    if (!motionAllowed(lat, lon, p.lat, p.lon)) break;
    ok = km;
  }
  return ok;
}

bool _inAlarmAt(double lat, double lon) {
  final o = OblastLookup.instance.nameAt(lat, lon);
  return o != null && _inAlarm(o, lat, lon);
}

final Map<String, double> _limitCache = {};

/// How far along [route] the target may fly before it would leave the alarm zone —
/// or Ukraine: a drone over Odesa oblast flying "west" must not drift on into
/// Moldova and Romania. Short excursions (≤15 km) are allowed because the
/// polygons are simplified along the border and the coast.
double _alarmLimitKm(MapTarget t, List<List<double>> route) {
  final zones = motionAlarmOblasts;
  final key = '${t.id}|${t.seenMs}|${t.lat},${t.lon}|$_zonesVersion';
  final hit = _limitCache[key];
  if (hit != null) return hit;
  if (_limitCache.length > 400) _limitCache.clear();
  final total = routeLengthKm(route);
  var lim = total;
  var wasIn = false;
  double? leftAt;
  for (var d = 0.0; d <= total; d += 4) {
    final p = alongRoute(route, d);
    final name = OblastLookup.instance.nameAt(p.lat, p.lon);
    if (name == null) {
      if (!wasIn) continue; // still on its way in (launched from Russia / the sea)
      leftAt ??= d;
      if (d - leftAt > 15) {
        lim = math.max(0, leftAt - 4);
        break;
      }
      continue;
    }
    wasIn = true;
    leftAt = null;
    // Stop at the border of the all-clear zone — an oblast without alarm, or the
    // all-clear raions of a partially alarmed one.
    if (zones != null && zones.isNotEmpty && !_inAlarm(name, p.lat, p.lon)) {
      lim = math.max(0, d - 4);
      break;
    }
  }
  if (leftAt != null && lim == total) lim = math.max(0, leftAt - 4);
  return _limitCache[key] = lim;
}

double routeLengthKm(List<List<double>> r) {
  var s = 0.0;
  for (var i = 1; i < r.length; i++) {
    s += kmBetween(r[i - 1][0], r[i - 1][1], r[i][0], r[i][1]);
  }
  return s;
}

/// Point [km] along a polyline route ([[lat, lon], …]) and the heading there.
({double lat, double lon, double heading}) alongRoute(List<List<double>> r, double km) {
  for (var i = 1; i < r.length; i++) {
    final a = r[i - 1], b = r[i];
    final seg = kmBetween(a[0], a[1], b[0], b[1]);
    if (km <= seg && seg > 0) {
      final f = km / seg;
      return (lat: a[0] + (b[0] - a[0]) * f, lon: a[1] + (b[1] - a[1]) * f, heading: bearingDeg(a[0], a[1], b[0], b[1]));
    }
    km -= seg;
  }
  final a = r[r.length - 2], b = r.last;
  return (lat: b[0], lon: b[1], heading: bearingDeg(a[0], a[1], b[0], b[1]));
}

/// How long a cruise target keeps moving after a post before it stops and only
/// the uncertainty ring grows.
const double kCreepMin = 4;

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
      // Ballistic «where it could reach» — huge after a few minutes and meaningless:
      // cap it (the Reader drops the ring once a target is named).
      reachKm: frozen ? 0 : math.min(t.cat == 'balist' ? 450 : 1200, spd * mins),
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

  // Aviation zone: the plane circles ~15 km around the point (one lap ≈ 2 min).
  if (t.areaKm > 0) {
    final ang = frozen ? 0.0 : (nowMs / 333) % 360;
    final p = moveKm(t.lat, t.lon, ang, 15);
    return TargetMotion(lat: p.lat, lon: p.lon, heading: (ang + 90) % 360);
  }

  var lat = t.lat, lon = t.lon;
  final quiet = math.max(0, nowMs - (t.seenMs == 0 ? nowMs : t.seenMs)) / 60000;
  final route = t.route;
  if (t.forecast && route.length >= 2) {
    // Launch group (forecast): flies its whole typical route from the launch time.
    final p = alongRoute(route, frozen ? 0 : spd * 0.85 * quiet);
    return TargetMotion(lat: p.lat, lon: p.lon, heading: p.heading, quietMin: quiet.toDouble());
  }
  if (route.length >= 2 && !t.down && !frozen) {
    // Typical route (to Kyiv / west, or along the real course): fly it for up to
    // [kRouteMin] minutes after the post at ~85 % speed, never past the edge of an
    // oblast without an alarm. A new post resets the start (the marker jumps to
    // where the channel says it is and flies on from there).
    final want = spd * 0.85 * math.min(quiet, kRouteMin);
    final lim = _alarmLimitKm(t, route);
    final d = math.min(want, lim);
    final p = alongRoute(route, d);
    final stopped = quiet >= kRouteMin || lim < want;
    final uncert = !stopped || quiet < 1.5 ? 0.0 : math.min(80.0, spd * 0.4 * quiet);
    return TargetMotion(lat: p.lat, lon: p.lon, heading: p.heading, uncertKm: uncert, quietMin: quiet.toDouble());
  }
  if (t.heading != null && t.headingKnown && !t.down && !frozen) {
    // Real course: creep along it for at most [kCreepMin] minutes, at ~70 % of the
    // speed (turns and zig-zags make the straight-line progress shorter).
    // Never creep out of Ukraine (Odesa oblast → Moldova / Romania) or over the
    // all-clear border: stop at the last point that is still allowed.
    for (var km = math.min(30.0, spd * 0.7 * math.min(quiet, kCreepMin)); km > 0; km -= 2) {
      final m = moveKm(t.lat, t.lon, t.heading!, km);
      if (motionAllowed(t.lat, t.lon, m.lat, m.lon)) {
        lat = m.lat;
        lon = m.lon;
        break;
      }
    }
  }
  // After that it could be anywhere within this much — shown as a ring.
  final uncert = frozen || t.down || quiet < 1.5 ? 0.0 : math.min(80.0, spd * 0.5 * quiet);
  return TargetMotion(lat: lat, lon: lon, heading: t.heading ?? 0, uncertKm: uncert, quietMin: quiet.toDouble());
}

/// Short altitude from the channels for map labels: «~150 м ↓», «низько», «падає».
String altitudeShort(MapTarget t) {
  final m = t.altM;
  final num = m == null ? '' : (m >= 1000 ? '~${(m / 1000).toStringAsFixed(1).replaceAll('.0', '')} км' : '~${m.round()} м');
  final arrow = switch (t.altNote) {
    'знижується' => ' ↓',
    'набирає висоту' => ' ↑',
    _ => '',
  };
  if (num.isNotEmpty) return '$num$arrow';
  return t.altNote;
}

/// The rest of [route] from the current point ([lat], [lon]) — for drawing where a
/// launch group / selected target is expected to go next.
List<List<double>> routeAhead(List<List<double>> route, double lat, double lon) {
  final from0 = kmBetween(route[0][0], route[0][1], lat, lon);
  return [
    [lat, lon],
    for (final p in route.skip(1))
      if (kmBetween(route[0][0], route[0][1], p[0], p[1]) > from0 + 1) p,
  ];
}
