import 'dart:math' as math;

import '../data/oblast_lookup.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import 'motion.dart';

/// «Коли орієнтовно буде тривога» для місця: найраніший момент, коли якась ціль або
/// група пуску, летячи своїм типовим маршрутом, зайде в область місця або підійде
/// ближче ніж на [kAlarmNearKm] — саме тоді тривогу зазвичай і оголошують.
/// Це оцінка за типовими маршрутами, не офіційне попередження.
class AlarmForecast {
  const AlarmForecast({required this.minutes, required this.via, required this.forecast, this.already = false});

  /// Тривога в області місця вже оголошена.
  final bool already;

  /// Через скільки хвилин орієнтовно.
  final double minutes;

  /// Що саме летить: «Пуск з Орел (реакт.) ×8» / «Шахед з Полтави».
  final String via;

  /// Розрахунок іде від прогнозу групи пуску (а не від справжньої цілі).
  final bool forecast;

  DateTime get at => DateTime.now().add(Duration(seconds: (minutes * 60).round()));

  static const alreadyOn = AlarmForecast(minutes: 0, via: '', forecast: false, already: true);
}

const double kAlarmNearKm = 50;

AlarmForecast? forecastAlarm(MonitoredPlace place, List<MapTarget> targets, {int? nowMs}) {
  final now = nowMs ?? DateTime.now().millisecondsSinceEpoch;
  final obl = OblastLookup.instance.nameAt(place.lat, place.lon);
  final zones = motionAlarmOblasts;
  if (obl != null && zones != null && zones.contains(obl)) return AlarmForecast.alreadyOn;

  AlarmForecast? best;
  for (final t in targets) {
    if (t.route.length < 2 || t.down) continue;
    final spd = speedKmPerMin(t) * 0.85; // km/min along the route
    if (spd <= 0) continue;
    final m = motionOf(t, now);
    final ahead = routeAhead(t.route, m.lat, m.lon);
    if (ahead.length < 2) continue;
    final total = routeLengthKm(ahead);
    double? hitKm;
    for (var d = 0.0; d <= total; d += 4) {
      final p = alongRoute(ahead, d);
      final km = kmBetween(p.lat, p.lon, place.lat, place.lon);
      if (km <= kAlarmNearKm || (km < 250 && obl != null && OblastLookup.instance.contains(obl, p.lat, p.lon))) {
        hitKm = d;
        break;
      }
    }
    if (hitKm == null) continue;
    final minutes = hitKm / spd;
    if (minutes > 8 * 60) continue;
    if (best == null || minutes < best.minutes) {
      final from = t.forecast ? '' : (t.place.isNotEmpty ? ' · зараз: ${t.place}' : '');
      best = AlarmForecast(
        minutes: math.max(0, minutes),
        via: '${t.label}${t.amount > 1 ? ' ×${t.amount}' : ''}$from',
        forecast: t.forecast,
      );
    }
  }
  return best;
}

/// «~1 год 10 хв» / «~25 хв» / «будь-якої хвилини».
String fmtMinutes(double m) {
  if (m < 3) return 'будь-якої хвилини';
  if (m < 60) return '~${m.round()} хв';
  final h = m ~/ 60, mm = (m - h * 60).round();
  return mm == 0 ? '~$h год' : '~$h год $mm хв';
}
