import '../models/map_target.dart';
import '../models/monitored_place.dart';
import 'probability.dart';
import 'situation.dart';

/// Pure alarm decision shared by the foreground [AlarmController] and the
/// background service isolate, so both raise the alarm on exactly the same rule.
class AlarmDecision {
  final String level; // calm | warn | alarm
  final String title;
  final String detail;
  final String? cat; // top threat category (picks the siren tone)
  final int probability; // 0-99 for the worst place
  final String placeName;
  const AlarmDecision({
    required this.level,
    required this.title,
    required this.detail,
    required this.probability,
    required this.placeName,
    this.cat,
  });
}

/// Evaluates the situation for [places] (the ones that want notifications).
/// [radiusKm] is the user's alarm radius. Mirrors AlarmController's rule.
AlarmDecision evaluateAlarm({
  required List<MapTarget> targets,
  required List<MonitoredPlace> places,
  required double radiusKm,
}) {
  RiskResult? worst;
  for (final pl in places) {
    final r = riskOf(pl, targets);
    if (worst == null || r.score > worst.score) worst = r;
  }
  var nearest = double.infinity;
  for (final pl in places) {
    for (final t in placeThreats(pl, targets)) {
      final d = t.pdist ?? double.infinity;
      if (d < nearest) nearest = d;
    }
  }

  final score = worst?.score ?? 0;
  final level = (score >= 55 || nearest <= radiusKm)
      ? 'alarm'
      : (score >= 35 || nearest <= radiusKm * 1.7)
          ? 'warn'
          : 'calm';

  final title = switch (level) {
    'alarm' => 'ТРИВОГА',
    'warn' => 'Загроза поруч',
    _ => 'Спокійно',
  };

  String detail = '';
  int prob = 0;
  String placeName = '';
  if (worst != null) {
    placeName = worst.place.name;
    prob = probabilityFor(worst.place, targets).percent;
    final t = worst.top;
    if (t != null) {
      detail = [
        '${t.label}${t.amount > 1 ? ' ×${t.amount}' : ''}',
        if (t.pdist != null) '${t.pdist!.round()} км від «${worst.place.name}»',
        fmtEta(etaNow(t, DateTime.now().millisecondsSinceEpoch)),
        if (t.place.isNotEmpty) t.place,
      ].where((s) => s.isNotEmpty).join(' · ');
    } else {
      detail = worst.advice;
    }
  }

  return AlarmDecision(
    level: level,
    title: title,
    detail: detail,
    probability: prob,
    placeName: placeName,
    cat: worst?.top?.cat,
  );
}
