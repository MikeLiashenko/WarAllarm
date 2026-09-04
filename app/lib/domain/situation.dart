import 'dart:math' as math;

import '../models/map_target.dart';
import '../models/monitored_place.dart';

/// Threat-situation logic ported 1:1 from the Reader's `map.html` JavaScript
/// (kmTo / placeThreats / risk / forecast / worst / status).

/// Stage severity ranking.
const Map<String, int> kRank = {
  'Impact': 5,
  'Overhead': 4,
  'Imminent': 3,
  'Approaching': 2,
  'Passed': 1,
};

/// Haversine distance target→place, km.
double kmTo(MapTarget t, MonitoredPlace pl) {
  const r = 6371.0, rad = math.pi / 180;
  final dLa = (pl.lat - t.lat) * rad, dLo = (pl.lon - t.lon) * rad;
  final la1 = t.lat * rad, la2 = pl.lat * rad;
  final h = math.sin(dLa / 2) * math.sin(dLa / 2) +
      math.cos(la1) * math.cos(la2) * math.sin(dLo / 2) * math.sin(dLo / 2);
  return r * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h));
}

/// Targets relevant to a place: ≤40 km OR same zone; pdist = distance to it.
List<MapTarget> placeThreats(MonitoredPlace pl, List<MapTarget> threats) {
  final out = <MapTarget>[];
  for (final t in threats) {
    final d = kmTo(t, pl);
    if (d <= 40 || (pl.zone != null && t.zone == pl.zone)) {
      out.add(t.withPdist(d));
    }
  }
  out.sort((a, b) =>
      (a.pdist ?? 1e9).compareTo(b.pdist ?? 1e9));
  return out;
}

MapTarget? worstOf(List<MapTarget> arr) {
  MapTarget? w;
  int wr = -1;
  for (final t in arr) {
    final r = kRank[t.stage] ?? 0;
    if (r > wr) {
      wr = r;
      w = t;
    }
  }
  return w;
}

class PlaceStatus {
  final String cls; // calm | warn | alarm
  final String title;
  const PlaceStatus(this.cls, this.title);
}

PlaceStatus statusOf(MapTarget? worst) {
  final r = worst == null ? 0 : (kRank[worst.stage] ?? 0);
  if (r >= 3) return const PlaceStatus('alarm', 'ТРИВОГА');
  if (worst != null) return const PlaceStatus('warn', 'Загроза поруч');
  return const PlaceStatus('calm', 'Спокійно');
}

/// Live ETA (minutes), decremented from the snapshot time.
double? etaNow(MapTarget t, int stateTs) {
  if (t.etaMin == null) return null;
  final elapsed = (DateTime.now().millisecondsSinceEpoch - stateTs) / 60000;
  return math.max(0, t.etaMin! - elapsed);
}

class RiskResult {
  final MonitoredPlace place;
  final int score; // 0-100
  final String level;
  final String cls; // calm | warn | alarm
  final String advice;
  final MapTarget? top;
  final int count;
  const RiskResult({
    required this.place,
    required this.score,
    required this.level,
    required this.cls,
    required this.advice,
    this.top,
    this.count = 0,
  });
}

/// Per-place risk assessment (score + advice). Ported from `riskOf()`.
RiskResult riskOf(MonitoredPlace pl, List<MapTarget> threats) {
  final arr = placeThreats(pl, threats);
  if (arr.isEmpty) {
    return RiskResult(
      place: pl,
      score: 0,
      level: 'Спокій',
      cls: 'calm',
      advice: 'Загроз поруч немає. Можна займатися справами.',
    );
  }
  final t = arr.first;
  final st = kRank[t.stage] ?? 0;
  final d = t.pdist ?? 999;
  int score;
  if (st >= 5) {
    score = 100;
  } else if (st >= 4) {
    score = 95;
  } else if (st >= 3) {
    score = 88;
  } else if (d <= 8) {
    score = 75;
  } else if (d <= 15) {
    score = 58;
  } else if (d <= 25) {
    score = 42;
  } else if (d <= 40) {
    score = 30;
  } else {
    score = 15;
  }
  score = math.min(100, score + math.min(15, (arr.length - 1) * 4));

  String level, cls, advice;
  if (score >= 85) {
    level = 'Критичний';
    cls = 'alarm';
    advice = 'В укриття НЕГАЙНО. Подалі від вікон, правило двох стін.';
  } else if (score >= 55) {
    level = 'Високий';
    cls = 'alarm';
    advice =
        'Готуйся до укриття: одягнись, візьми телефон і повербанк, будь ближче до укриття.';
  } else if (score >= 35) {
    level = 'Середній';
    cls = 'warn';
    advice = 'Стеж за ситуацією — ціль у сусідньому районі/передмісті.';
  } else {
    level = 'Низький';
    cls = 'warn';
    advice = 'Загроза активна, але далеко. Тримай звук увімкненим.';
  }
  return RiskResult(
    place: pl,
    score: score,
    level: level,
    cls: cls,
    advice: advice,
    top: t,
    count: arr.length,
  );
}

class ClosestApproach {
  final double minKm;
  final bool approaching;
  const ClosestApproach(this.minKm, this.approaching);
}

/// Geometry: closest approach of a target's course to a place. Ported `closest()`.
ClosestApproach closest(MapTarget t, MonitoredPlace pl) {
  final la = pl.lat * math.pi / 180;
  final dN = (t.lat - pl.lat) * 111, dE = (t.lon - pl.lon) * 111 * math.cos(la);
  final th = (t.heading ?? 0) * math.pi / 180;
  final mE = math.sin(th), mN = math.cos(th);
  final rE = -dE, rN = -dN, proj = rE * mE + rN * mN;
  final min = proj <= 0
      ? math.sqrt(rE * rE + rN * rN)
      : math.sqrt(math.max(0, rE * rE + rN * rN - proj * proj));
  return ClosestApproach(min, proj > 0);
}

String fmtEta(double? m) {
  if (m == null) return '?';
  return m < 1 ? '<1 хв' : '~${m.round()} хв';
}
