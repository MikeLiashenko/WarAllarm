/// A compact alarm-journal record for the analytics dashboard.
/// Mirrors the `rows[]` the Reader emits from `alarms.csv`:
/// `{ d: "yyyy-MM-dd", h: hour 0-23, z: zone, k: kind, g: alarm|urgent|impact }`.
class AnalyticsRow {
  final String date;
  final int hour;
  final String zone;
  final String kind;
  final String tag; // alarm | urgent | impact

  const AnalyticsRow({
    required this.date,
    required this.hour,
    required this.zone,
    required this.kind,
    required this.tag,
  });

  bool get isImpact => tag == 'impact';
  bool get isAlarm => tag == 'alarm' || tag == 'urgent';

  factory AnalyticsRow.fromMap(Map<dynamic, dynamic> m) => AnalyticsRow(
        date: (m['d'] ?? '').toString(),
        hour: (m['h'] as num?)?.toInt() ?? 0,
        zone: (m['z'] ?? '').toString(),
        kind: (m['k'] ?? '').toString(),
        tag: (m['g'] ?? 'alarm').toString(),
      );
}
