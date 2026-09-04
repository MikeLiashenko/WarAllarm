import 'package:flutter/material.dart';

import '../models/analytics_row.dart';

/// Analytics aggregations ported from the Reader's `map.html`
/// (rowsIn / chartByDay / chartByHour / chartByCat / patternCards).

/// Threat-kind display metadata (name + colour).
const Map<String, ({String name, Color color})> kKindMeta = {
  'Drone': (name: 'Шахед/БпЛА', color: Color(0xFFFF9F45)),
  'Ballistic': (name: 'Балістика', color: Color(0xFFFF3B30)),
  'Missile': (name: 'Ракета', color: Color(0xFFFF5A3C)),
  'Kab': (name: 'КАБ', color: Color(0xFFFF6B4A)),
  'Aviation': (name: 'Авіація', color: Color(0xFFF4C550)),
  'Target': (name: 'Ціль', color: Color(0xFF8792A6)),
  'Unknown': (name: 'Інше', color: Color(0xFF8792A6)),
};

String _dayAgo(int n) => DateTime.now()
    .subtract(Duration(days: n))
    .toIso8601String()
    .substring(0, 10);

/// Filter rows to the selected period ('7' | '30' | '90' | 'all').
List<AnalyticsRow> rowsIn(List<AnalyticsRow> rows, String period) {
  if (period == 'all') return rows;
  final cut = _dayAgo(int.parse(period));
  return rows.where((r) => r.date.compareTo(cut) >= 0).toList();
}

class AnalyticsKpis {
  final int alarms;
  final int impacts;
  final int daysWithAlarms;
  final double avgPerDay;
  const AnalyticsKpis(
      this.alarms, this.impacts, this.daysWithAlarms, this.avgPerDay);
}

AnalyticsKpis computeKpis(List<AnalyticsRow> rows, String period) {
  final al = rows.where((r) => !r.isImpact).toList();
  final im = rows.where((r) => r.isImpact).toList();
  final days = <String>{for (final r in al) r.date};
  final pd = period == 'all' ? (days.isEmpty ? 1 : days.length) : int.parse(period);
  return AnalyticsKpis(al.length, im.length, days.length, al.length / pd);
}

/// Alarms per day for the period → ordered [(dateLabel, count)].
List<(String, int)> byDay(List<AnalyticsRow> alarms, String period) {
  final counts = <String, int>{};
  for (final r in alarms) {
    counts[r.date] = (counts[r.date] ?? 0) + 1;
  }
  final days = <(String, int)>[];
  if (period != 'all') {
    for (var i = int.parse(period) - 1; i >= 0; i--) {
      final d = _dayAgo(i);
      days.add((d, counts[d] ?? 0));
    }
  } else {
    final keys = counts.keys.toList()..sort();
    for (final d in keys) {
      days.add((d, counts[d]!));
    }
  }
  if (days.isEmpty) days.add((_dayAgo(0), 0));
  return days;
}

/// Alarms per hour of day → 24 counts.
List<int> byHour(List<AnalyticsRow> alarms) {
  final h = List<int>.filled(24, 0);
  for (final r in alarms) {
    h[r.hour % 24]++;
  }
  return h;
}

/// Alarms grouped by a field ('kind' or 'zone') → sorted [(key, count)] desc.
List<(String, int)> byField(List<AnalyticsRow> alarms, bool byKind) {
  final g = <String, int>{};
  for (final r in alarms) {
    final k = (byKind ? r.kind : r.zone);
    final key = k.isEmpty ? '—' : k;
    g[key] = (g[key] ?? 0) + 1;
  }
  final arr = g.entries.map((e) => (e.key, e.value)).toList();
  arr.sort((a, b) => b.$2.compareTo(a.$2));
  return arr;
}

class Patterns {
  final int peakHour;
  final String topKind;
  final String weekTrend;
  final String topImpactZone;
  const Patterns(
      this.peakHour, this.topKind, this.weekTrend, this.topImpactZone);
}

Patterns? computePatterns(List<AnalyticsRow> rows) {
  if (rows.length < 3) return null;
  final al = rows.where((r) => !r.isImpact).toList();
  final hh = List<int>.filled(24, 0);
  for (final r in al) {
    hh[r.hour % 24]++;
  }
  var peak = 0, peakV = -1;
  for (var i = 0; i < 24; i++) {
    if (hh[i] > peakV) {
      peakV = hh[i];
      peak = i;
    }
  }
  final kc = <String, int>{};
  for (final r in al) {
    kc[r.kind] = (kc[r.kind] ?? 0) + 1;
  }
  final topKind = kc.isEmpty
      ? '—'
      : (kc.entries.toList()..sort((a, b) => b.value.compareTo(a.value)))
          .first
          .key;

  final w1 = al.where((r) => r.date.compareTo(_dayAgo(7)) >= 0).length;
  final w0 = al
      .where((r) =>
          r.date.compareTo(_dayAgo(14)) >= 0 && r.date.compareTo(_dayAgo(7)) < 0)
      .length;
  final trend = w0 == 0
      ? (w1 > 0 ? '+100%' : '—')
      : '${w1 - w0 >= 0 ? '+' : ''}${((w1 - w0) / w0 * 100).round()}%';

  final im = rows.where((r) => r.isImpact).toList();
  final zc = <String, int>{};
  for (final r in im) {
    zc[r.zone] = (zc[r.zone] ?? 0) + 1;
  }
  final topZone = zc.isEmpty
      ? '—'
      : (zc.entries.toList()..sort((a, b) => b.value.compareTo(a.value)))
          .first
          .key;

  return Patterns(peak, topKind, trend, topZone);
}
