import '../data/oblast_lookup.dart';
import '../models/analytics_row.dart';
import '../models/monitored_place.dart';
import 'motion.dart' show kmBetween;

/// 📈 Історія тривог МІСЦЯ з Firebase `areaLog/<область>/<день>` (Reader `AreaLog.cs`,
/// джерело — офіційні тривоги по районах і громадах з @UkraineAlarmSignal).
///
/// День: `{"a": [[назва, початокХв, кінецьХв|0, рівень(2 черв/1 жовт), lat?, lon?]],
///        "t": [[хв, тип, місце, lat, lon]]}` — хвилини від епохи Unix (UTC).
/// Тривога стосується місця, якщо це його район (за назвою) або громада ближче [cityKm].
/// Перекриття (район + громада) зливаємо — одна тривога, а не дві.

/// Типи сповіщень громад, від найнебезпечнішого.
const _kindRank = ['Ballistic', 'Missile', 'Kab', 'Drone', 'Shelling', 'Unknown'];

String _napo(String s) =>
    s.replaceAll('’', "'").replaceAll('ʼ', "'").replaceAll('`', "'").toLowerCase();

DateTime _at(num min) => DateTime.fromMillisecondsSinceEpoch(min.toInt() * 60000);

String _day(DateTime t) =>
    '${t.year.toString().padLeft(4, '0')}-${t.month.toString().padLeft(2, '0')}-${t.day.toString().padLeft(2, '0')}';

class _Span {
  DateTime start, end;
  int level;
  _Span(this.start, this.end, this.level);
}

/// Записи аналітики для [pl] з вузла області [node] (усі дні). [raion] — район місця
/// (null — визначити за координатами). Тривоги → tag 'alarm' (kind — найнебезпечніше
/// сповіщення поруч під час тривоги, інакше 'Official'); «вибухи» поруч → tag 'impact'.
List<AnalyticsRow> placeRows(
  MonitoredPlace pl,
  Map<dynamic, dynamic>? node, {
  String? raion,
  DateTime? now,
  double cityKm = 12,
  double noteKm = 25,
  double impactKm = 15,
}) {
  if (node == null || node.isEmpty) return const [];
  final r = raion ?? OblastLookup.raions.nameAt(pl.lat, pl.lon);
  final myRaion = r == null ? null : _napo(r);
  final nowT = now ?? DateTime.now();
  final spans = <_Span>[];
  final notes = <(DateTime, String, double)>[]; // час, тип, км до місця
  for (final day in node.values) {
    if (day is! Map) continue;
    for (final a in (day['a'] as List? ?? const [])) {
      if (a is! List || a.length < 4) continue;
      final name = a[0].toString();
      bool mine;
      if (a.length >= 6 && a[4] is num && a[5] is num) {
        mine = kmBetween(pl.lat, pl.lon, (a[4] as num).toDouble(), (a[5] as num).toDouble()) <= cityKm;
      } else {
        mine = myRaion != null && _napo(name) == myRaion;
      }
      if (!mine || a[1] is! num) continue;
      final s = _at(a[1] as num);
      final e = (a[2] is num && (a[2] as num) > 0) ? _at(a[2] as num) : nowT;
      spans.add(_Span(s, e.isBefore(s) ? s : e, (a[3] as num?)?.toInt() ?? 2));
    }
    for (final t in (day['t'] as List? ?? const [])) {
      if (t is! List || t.length < 5 || t[0] is! num || t[3] is! num || t[4] is! num) continue;
      final km = kmBetween(pl.lat, pl.lon, (t[3] as num).toDouble(), (t[4] as num).toDouble());
      if (km <= noteKm) notes.add((_at(t[0] as num), t[1].toString(), km));
    }
  }
  spans.sort((a, b) => a.start.compareTo(b.start));
  final merged = <_Span>[];
  for (final s in spans) {
    if (merged.isNotEmpty && !s.start.isAfter(merged.last.end)) {
      final m = merged.last;
      if (s.end.isAfter(m.end)) m.end = s.end;
      if (s.level > m.level) m.level = s.level;
    } else {
      merged.add(_Span(s.start, s.end, s.level));
    }
  }
  final rows = <AnalyticsRow>[];
  for (final s in merged) {
    // Що загрожувало поруч під час тривоги (за 10 хв до неї — теж).
    var kind = 'Official';
    var best = _kindRank.length;
    for (final n in notes) {
      if (n.$2 == 'impact') continue;
      if (n.$1.isBefore(s.start.subtract(const Duration(minutes: 10))) || n.$1.isAfter(s.end)) continue;
      final rank = _kindRank.indexOf(n.$2);
      final r2 = rank < 0 ? _kindRank.length - 1 : rank;
      if (r2 < best) {
        best = r2;
        kind = n.$2;
      }
    }
    rows.add(AnalyticsRow(
      date: _day(s.start),
      hour: s.start.hour,
      zone: pl.name,
      kind: kind,
      tag: 'alarm',
      minutes: s.end.difference(s.start).inMinutes,
      level: s.level,
    ));
  }
  for (final n in notes) {
    if (n.$2 != 'impact' || n.$3 > impactKm) continue;
    // Тип прильоту — що загрожувало в тривозі, під час якої були вибухи.
    var kind = 'Unknown';
    for (var i = 0; i < merged.length; i++) {
      final s = merged[i];
      if (!n.$1.isBefore(s.start) && !n.$1.isAfter(s.end.add(const Duration(minutes: 15)))) {
        kind = rows[i].kind == 'Official' ? 'Unknown' : rows[i].kind;
        break;
      }
    }
    rows.add(AnalyticsRow(date: _day(n.$1), hour: n.$1.hour, zone: pl.name, kind: kind, tag: 'impact'));
  }
  return rows;
}
