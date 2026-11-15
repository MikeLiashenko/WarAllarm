import 'package:flutter/material.dart';

import '../models/analytics_row.dart';

/// Analytics aggregations over the alarm journal (`rows` from the Reader's
/// `alarms.csv`: date, hour, zone, kind, tag alarm|urgent|impact).

/// Threat-kind display metadata (name + colour + icon).
const Map<String, ({String name, Color color})> kKindMeta = {
  'Drone': (name: 'Шахед/БпЛА', color: Color(0xFFFFC43D)),
  'Ballistic': (name: 'Балістика', color: Color(0xFFFF3B5C)),
  'Missile': (name: 'Ракета', color: Color(0xFFFF7A3C)),
  'Kab': (name: 'КАБ', color: Color(0xFFB57BFF)),
  'Aviation': (name: 'Авіація', color: Color(0xFF5AC8FA)),
  'Fpv': (name: 'FPV-дрон', color: Color(0xFF2DD4BF)),
  'Target': (name: 'Ціль', color: Color(0xFF8792A6)),
  'Unknown': (name: 'Інше', color: Color(0xFF8792A6)),
};

const Map<String, IconData> kKindIcon = {
  'Drone': Icons.flight_rounded,
  'Ballistic': Icons.rocket_launch_rounded,
  'Missile': Icons.rocket_rounded,
  'Kab': Icons.south_rounded,
  'Aviation': Icons.airplanemode_active_rounded,
  'Fpv': Icons.toys_rounded,
  'Target': Icons.gps_fixed_rounded,
  'Unknown': Icons.help_outline_rounded,
};

/// Journal zone → display name ('UA' = country-wide alerts).
String zoneName(String z) => switch (z) {
      'UA' => 'Уся країна',
      '' || '—' => 'Без регіону',
      _ => z,
    };

String kindName(String k) => kKindMeta[k]?.name ?? (k.isEmpty ? 'Інше' : k);
Color kindColor(String k) => kKindMeta[k]?.color ?? const Color(0xFF8792A6);
IconData kindIcon(String k) => kKindIcon[k] ?? Icons.gps_fixed_rounded;

const kWeekdays = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Нд'];

String _day(DateTime d) => d.toIso8601String().substring(0, 10);
String _dayAgo(int n) => _day(DateTime.now().subtract(Duration(days: n)));

/// Weekday 0 = Mon … 6 = Sun of a yyyy-MM-dd date ('' → -1).
int weekdayOf(String date) {
  final d = DateTime.tryParse(date);
  return d == null ? -1 : d.weekday - 1;
}

/// Filter rows to the selected period ('1' | '7' | '30' | '90' | 'all').
List<AnalyticsRow> rowsIn(List<AnalyticsRow> rows, String period) {
  if (period == 'all') return rows;
  final cut = _dayAgo(int.parse(period) - 1);
  return rows.where((r) => r.date.compareTo(cut) >= 0).toList();
}

/// Rows of the period right before the selected one (for ↑/↓ deltas).
List<AnalyticsRow> rowsBefore(List<AnalyticsRow> rows, String period) {
  if (period == 'all') return const [];
  final n = int.parse(period);
  final from = _dayAgo(2 * n - 1), to = _dayAgo(n - 1);
  return rows
      .where((r) => r.date.compareTo(from) >= 0 && r.date.compareTo(to) < 0)
      .toList();
}

/// Number of calendar days the period covers.
int periodDays(List<AnalyticsRow> allRows, String period) {
  if (period != 'all') return int.parse(period);
  if (allRows.isEmpty) return 1;
  final dates = allRows.map((r) => r.date).where((d) => d.isNotEmpty).toList()..sort();
  final a = DateTime.tryParse(dates.first), b = DateTime.now();
  return a == null ? 1 : b.difference(a).inDays + 1;
}

/// Everything the analytics screen shows, computed once per filter change.
class AnalyticsSummary {
  AnalyticsSummary._({
    required this.days,
    required this.alarms,
    required this.urgent,
    required this.impacts,
    required this.prevAlarms,
    required this.prevImpacts,
    required this.daily,
    required this.hourly,
    required this.hourlyImpacts,
    required this.weekday,
    required this.heat,
    required this.kinds,
    required this.zones,
    required this.impactZones,
    required this.impactKinds,
    required this.daysWithAlarms,
    required this.longestStreak,
    required this.currentStreak,
    required this.longestQuiet,
    required this.nightAlarms,
    required this.recent,
  });

  final int days;
  final int alarms, urgent, impacts;
  final int? prevAlarms, prevImpacts;

  /// [(yyyy-MM-dd, alarms, impacts)] for every day of the period, oldest first.
  final List<(String, int, int)> daily;
  final List<int> hourly; // 24 alarms by hour
  final List<int> hourlyImpacts; // 24 impacts by hour
  final List<int> weekday; // 7 alarms by weekday (Mon first)
  final List<List<int>> heat; // [weekday][hour]
  final List<(String, int)> kinds; // alarms by kind, desc
  final List<(String, int, int)> zones; // (zone, alarms, impacts) desc
  final List<(String, int)> impactZones;
  final List<(String, int)> impactKinds;
  final int daysWithAlarms;
  final int longestStreak, currentStreak, longestQuiet;
  final int nightAlarms; // 22:00–05:59
  final List<AnalyticsRow> recent; // newest first

  double get avgPerDay => alarms / (days <= 0 ? 1 : days);
  int get quietDays => (days - daysWithAlarms).clamp(0, days);
  double get nightShare => alarms == 0 ? 0 : nightAlarms / alarms;
  double get impactRate => alarms == 0 ? 0 : impacts / alarms;

  int get peakHour {
    var p = 0;
    for (var i = 1; i < 24; i++) {
      if (hourly[i] > hourly[p]) p = i;
    }
    return p;
  }

  int get peakWeekday {
    var p = 0;
    for (var i = 1; i < 7; i++) {
      if (weekday[i] > weekday[p]) p = i;
    }
    return p;
  }

  /// The calmest 3-hour window of the day (start hour).
  int get quietestWindow {
    var best = 0, bestV = 1 << 30;
    for (var h = 0; h < 24; h++) {
      final v = hourly[h] + hourly[(h + 1) % 24] + hourly[(h + 2) % 24];
      if (v < bestV) {
        bestV = v;
        best = h;
      }
    }
    return best;
  }

  (String, int, int)? get busiestDay {
    (String, int, int)? b;
    for (final d in daily) {
      if (b == null || d.$2 > b.$2) b = d;
    }
    return (b == null || b.$2 == 0) ? null : b;
  }

  /// % change vs the previous period of the same length (null — nothing to compare).
  static int? delta(int now, int? before) {
    if (before == null) return null;
    if (before == 0) return now == 0 ? 0 : null;
    return ((now - before) / before * 100).round();
  }

  static AnalyticsSummary compute(
    List<AnalyticsRow> all,
    String period, {
    Set<String> kinds = const {},
    String? zone,
  }) {
    bool keep(AnalyticsRow r) =>
        (kinds.isEmpty || kinds.contains(r.kind)) && (zone == null || r.zone == zone);

    final rows = rowsIn(all, period).where(keep).toList();
    final prev = period == 'all' ? null : rowsBefore(all, period).where(keep).toList();
    final days = periodDays(all, period);

    final al = rows.where((r) => !r.isImpact).toList();
    final im = rows.where((r) => r.isImpact).toList();

    // Per day.
    final dA = <String, int>{}, dI = <String, int>{};
    for (final r in al) {
      dA[r.date] = (dA[r.date] ?? 0) + 1;
    }
    for (final r in im) {
      dI[r.date] = (dI[r.date] ?? 0) + 1;
    }
    final daily = <(String, int, int)>[];
    for (var i = days - 1; i >= 0; i--) {
      final d = _dayAgo(i);
      daily.add((d, dA[d] ?? 0, dI[d] ?? 0));
    }

    // Streaks over the daily series.
    var longest = 0, run = 0, quiet = 0, qrun = 0;
    for (final d in daily) {
      if (d.$2 > 0) {
        run++;
        qrun = 0;
      } else {
        qrun++;
        run = 0;
      }
      if (run > longest) longest = run;
      if (qrun > quiet) quiet = qrun;
    }
    var current = 0;
    for (var i = daily.length - 1; i >= 0 && daily[i].$2 > 0; i--) {
      current++;
    }

    final hourly = List<int>.filled(24, 0), hourlyI = List<int>.filled(24, 0);
    final weekday = List<int>.filled(7, 0);
    final heat = List.generate(7, (_) => List<int>.filled(24, 0));
    var night = 0;
    for (final r in al) {
      final h = r.hour % 24;
      hourly[h]++;
      if (h >= 22 || h < 6) night++;
      final w = weekdayOf(r.date);
      if (w >= 0) {
        weekday[w]++;
        heat[w][h]++;
      }
    }
    for (final r in im) {
      hourlyI[r.hour % 24]++;
    }

    List<(String, int)> count(Iterable<AnalyticsRow> it, String Function(AnalyticsRow) f) {
      final m = <String, int>{};
      for (final r in it) {
        final k = f(r).isEmpty ? '—' : f(r);
        m[k] = (m[k] ?? 0) + 1;
      }
      return m.entries.map((e) => (e.key, e.value)).toList()
        ..sort((a, b) => b.$2.compareTo(a.$2));
    }

    final zA = count(al, (r) => r.zone), zI = {for (final e in count(im, (r) => r.zone)) e.$1: e.$2};
    final zones = <(String, int, int)>[
      for (final z in zA) (z.$1, z.$2, zI[z.$1] ?? 0),
      for (final z in zI.entries)
        if (!zA.any((a) => a.$1 == z.key)) (z.key, 0, z.value),
    ];

    final recent = [...rows]..sort((a, b) {
        final c = b.date.compareTo(a.date);
        return c != 0 ? c : b.hour.compareTo(a.hour);
      });

    return AnalyticsSummary._(
      days: days,
      alarms: al.length,
      urgent: al.where((r) => r.tag == 'urgent').length,
      impacts: im.length,
      prevAlarms: prev?.where((r) => !r.isImpact).length,
      prevImpacts: prev?.where((r) => r.isImpact).length,
      daily: daily,
      hourly: hourly,
      hourlyImpacts: hourlyI,
      weekday: weekday,
      heat: heat,
      kinds: count(al, (r) => r.kind),
      zones: zones,
      impactZones: count(im, (r) => r.zone),
      impactKinds: count(im, (r) => r.kind),
      daysWithAlarms: dA.length,
      longestStreak: longest,
      currentStreak: current,
      longestQuiet: quiet,
      nightAlarms: night,
      recent: recent.take(40).toList(),
    );
  }
}

// --- Legacy helpers (AI screen) ----------------------------------------------

class AnalyticsKpis {
  final int alarms;
  final int impacts;
  final int daysWithAlarms;
  final double avgPerDay;
  const AnalyticsKpis(this.alarms, this.impacts, this.daysWithAlarms, this.avgPerDay);
}

AnalyticsKpis computeKpis(List<AnalyticsRow> rows, String period) {
  final al = rows.where((r) => !r.isImpact).toList();
  final im = rows.where((r) => r.isImpact).toList();
  final days = <String>{for (final r in al) r.date};
  final pd = period == 'all' ? (days.isEmpty ? 1 : days.length) : int.parse(period);
  return AnalyticsKpis(al.length, im.length, days.length, al.length / pd);
}

class Patterns {
  final int peakHour;
  final String topKind;
  final String weekTrend;
  final String topImpactZone;
  const Patterns(this.peakHour, this.topKind, this.weekTrend, this.topImpactZone);
}

Patterns? computePatterns(List<AnalyticsRow> rows) {
  if (rows.length < 3) return null;
  final s = AnalyticsSummary.compute(rows, 'all');
  final w1 = AnalyticsSummary.compute(rows, '7');
  final d = AnalyticsSummary.delta(w1.alarms, w1.prevAlarms);
  final trend = d == null ? (w1.alarms > 0 ? '+100%' : '—') : '${d >= 0 ? '+' : ''}$d%';
  return Patterns(
    s.peakHour,
    s.kinds.isEmpty ? '—' : s.kinds.first.$1,
    trend,
    s.impactZones.isEmpty ? '—' : s.impactZones.first.$1,
  );
}
