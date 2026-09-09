import 'dart:math' as math;

import '../data/oblast_lookup.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import 'situation.dart';

/// Attack-probability engine. Turns the live threat picture into an explainable
/// chance (0–100%) that a given place gets struck in the near term, plus a
/// plain verdict ("прилетить / навряд"), the main contributing threats, and a
/// per-kind breakdown.
///
/// The same model is ported to the Reader (C# `RiskModel`) so the identical
/// numbers are published to Firebase and served over the public API
/// (`state.risk`).

/// One threat's contribution to a place's probability.
class ProbFactor {
  final MapTarget target;
  final int percent; // this target's own hit chance for the place
  final double distKm;
  const ProbFactor(this.target, this.percent, this.distKm);
}

class ThreatProbability {
  final MonitoredPlace place;
  final int percent; // 0-100 overall chance of a strike near the place
  final String verdict; // Дуже ймовірно / Ймовірно / Можливо / Малоймовірно / Спокійно
  final String cls; // alarm | warn | calm
  final String advice;
  final List<ProbFactor> factors; // top contributors, strongest first
  final Map<String, int> byKind; // cat -> percent
  final String? officialLevel; // red | yellow | null (oblast air-raid status)
  const ThreatProbability({
    required this.place,
    required this.percent,
    required this.verdict,
    required this.cls,
    required this.advice,
    required this.factors,
    required this.byKind,
    this.officialLevel,
  });
}

/// Per-target hit chance (0..~0.985) for [pl], from stage, distance, trajectory,
/// kind and cluster size.
double _perTarget(MapTarget t, MonitoredPlace pl) {
  final d = kmTo(t, pl);
  final stage = kRank[t.stage] ?? 0;
  double base;
  if (stage >= 5) {
    base = 0.98; // Impact
  } else if (stage >= 4) {
    base = 0.9; // Overhead
  } else if (stage >= 3) {
    base = 0.75; // Imminent
  } else if (t.stage == 'Passed') {
    base = 0.05;
  } else {
    // Approaching → driven by how close it already is.
    if (d <= 8) {
      base = 0.6;
    } else if (d <= 15) {
      base = 0.42;
    } else if (d <= 25) {
      base = 0.28;
    } else if (d <= 40) {
      base = 0.16;
    } else if (d <= 80) {
      base = 0.07;
    } else {
      base = 0.02;
    }
  }

  // Trajectory: is its course actually pointing at this place?
  if (t.heading != null) {
    final ca = closest(t, pl);
    if (ca.approaching && ca.minKm <= 12) {
      base = base * 1.6 + 0.08;
    } else if (ca.approaching && ca.minKm <= 30) {
      base = base * 1.25;
    } else if (!ca.approaching) {
      base *= 0.5; // moving away
    }
  }

  // Kind: ballistics/missiles are harder to stop; drones are often intercepted.
  base *= switch (t.cat) {
    'balist' => 1.12,
    'missile' => 1.05,
    'kab' => 1.0,
    'avia' => 0.8,
    'shahed' => 0.9,
    _ => 0.9,
  };

  // Cluster size: a few extra units nudge the chance up (not linearly).
  base *= 1 + math.min(t.amount - 1, 6) * 0.03;

  return base.clamp(0.0, 0.985);
}

/// Full probability assessment for [pl]. [officialLevel] is the oblast air-raid
/// status ('red'/'yellow') if known — it sets a floor under the estimate.
ThreatProbability probabilityFor(
  MonitoredPlace pl,
  List<MapTarget> targets, {
  String? officialLevel,
}) {
  // Combine independent-ish per-target chances: P(any) = 1 - Π(1 - p).
  double none = 1;
  final factors = <ProbFactor>[];
  final kindNone = <String, double>{};
  for (final t in targets) {
    final p = _perTarget(t, pl);
    if (p < 0.02) continue;
    none *= (1 - p);
    kindNone[t.cat] = (kindNone[t.cat] ?? 1) * (1 - p);
    factors.add(ProbFactor(t, (p * 100).round(), kmTo(t, pl)));
  }
  double overall = 1 - none;

  // Official air-raid status is a strong prior: an active alarm means the
  // military sees inbound threats we may not have geolocated yet.
  if (officialLevel == 'red') {
    overall = math.max(overall, 0.5);
  } else if (officialLevel == 'yellow') {
    overall = math.max(overall, 0.22);
  }

  final percent = (overall * 100).round().clamp(0, 99);
  factors.sort((a, b) => b.percent.compareTo(a.percent));

  final byKind = <String, int>{
    for (final e in kindNone.entries) e.key: ((1 - e.value) * 100).round(),
  };

  final (verdict, cls, advice) = _verdict(percent, officialLevel);

  return ThreatProbability(
    place: pl,
    percent: percent,
    verdict: verdict,
    cls: cls,
    advice: advice,
    factors: factors.take(5).toList(),
    byKind: byKind,
    officialLevel: officialLevel,
  );
}

(String, String, String) _verdict(int p, String? official) {
  if (p >= 75) {
    return (
      'Дуже ймовірно',
      'alarm',
      'В укриття НЕГАЙНО. Подалі від вікон, правило двох стін.'
    );
  }
  if (p >= 50) {
    return (
      'Ймовірно',
      'alarm',
      'Готуйся до укриття: одягнись, візьми телефон і повербанк, будь поруч з укриттям.'
    );
  }
  if (p >= 25) {
    return (
      'Можливо',
      'warn',
      'Стеж за ситуацією — тримай звук увімкненим, будь напоготові.'
    );
  }
  if (p >= 10) {
    return (
      'Малоймовірно',
      'warn',
      'Загроза є, але шанс невеликий. Просто тримай звук увімкненим.'
    );
  }
  return (
    'Спокійно',
    'calm',
    official == null
        ? 'Явних загроз немає. Можна займатися справами.'
        : 'Тривога є поруч, але прямої загрози цьому місцю не видно.'
  );
}

/// Convenience: oblast air-raid level for a place, given the official levels map
/// keyed by English oblast polygon name (from AlertsController.levels).
String? officialLevelFor(
    MonitoredPlace pl, Map<String, String> oblastLevels) {
  if (oblastLevels.isEmpty) return null;
  final name = OblastLookup.instance.nameAt(pl.lat, pl.lon);
  if (name == null) return null;
  return oblastLevels[name];
}
