import '../data/oblast_lookup.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import 'situation.dart';

/// ⭐ Наскільки небезпечно ЗАРАЗ бути надворі в місці: 0–5 зірок.
///
/// Порт 1:1 з Reader `DangerStars.cs` — ті самі зірки показує бот (📊 Статус).
/// Міняєш правила тут — міняй і там.
///
/// Кожна ціль дає зірки за типом і відстанню до місця:
///                          далеко  поруч  досяжність  «поруч»
///   шахед / невідома ціль    1       2      40 км      12 км
///   реактивний шахед         2       3      60 км      20 км
///   ФПВ / Ланцет             2       3      20 км       8 км
///   КАБ                      3       4      60 км      25 км
///   крилата ракета           3       4     100 км      30 км
///   РСЗВ («Град», «Ураган»)  3       4      40 км      15 км
///   балістика                4       5     200 км      80 км
/// «Поруч» — ближче за радіус «поруч» АБО курс прямо на місце (мимо ≤ 8 км, ближче
/// 2.5 × радіуса). Удаляється — на зірку менше. Вибух поруч — ще +1.
/// Беремо найнебезпечнішу ціль; 5+ бортів у досяжності — ще +1.
/// Авіація над областю: загроза КАБ — 3, лише літаки — 2. Загроза балістики в області
/// (☄ state.ballThreat) — щонайменше 4. Офіційна тривога — щонайменше 1.
class DangerStars {
  final int stars; // 0..5
  final String level; // Спокійно / Низька / Помірна / Висока / Дуже висока / Критична
  final String advice;
  final String reason; // що найнебезпечніше («Реактивний шахед · 14 км»)
  const DangerStars(this.stars, this.level, this.advice, this.reason);
}

class _Spec {
  final int far, near;
  final double reachKm, nearKm;
  const _Spec(this.far, this.near, this.reachKm, this.nearKm);
}

_Spec _specOf(MapTarget t) => switch (t.cat) {
      'balist' => const _Spec(4, 5, 200, 80),
      // РСЗВ («Град», «Ураган») б'є на 20–40 км від фронту — небезпечно лише поруч.
      'missile' when t.label.startsWith('РСЗВ') => const _Spec(3, 4, 40, 15),
      'missile' || 'banderol' => const _Spec(3, 4, 100, 30),
      'kab' => const _Spec(3, 4, 60, 25),
      'avia' => const _Spec(2, 3, 80, 30),
      'fpv' => const _Spec(2, 3, 20, 8),
      _ when t.kmh >= 350 => const _Spec(2, 3, 60, 20), // реактивний шахед
      _ => const _Spec(1, 2, 40, 12), // шахед / ціль невідомого типу
    };

int _forTarget(MapTarget t, MonitoredPlace pl, double km) {
  if (t.approx || t.down || t.forecast || t.areaKm > 0) return 0;
  final s = _specOf(t);
  if (km > s.reachKm) return 0;
  var near = km <= s.nearKm;
  var away = t.stage == 'Passed';
  if (!near && t.heading != null && km <= s.nearKm * 2.5) {
    final ca = closest(t, pl);
    if (ca.approaching && ca.minKm <= 8) {
      near = true;
    } else if (!ca.approaching && t.headingKnown) {
      away = true;
    }
  }
  var stars = near ? s.near : s.far;
  if (t.stage == 'Impact' && km <= s.nearKm) stars++;
  if (away) stars--;
  return stars.clamp(0, 5);
}

/// Зірки для [pl]. [targets] — живі цілі (без приблизних), [aviation] — зони авіації
/// (state.aviation), [officialLevel] — офіційна тривога в місці ('red'/'yellow'),
/// [ballisticFrom] — загроза балістики в області місця (район пуску або ''; null — нема).
DangerStars dangerStarsFor(
  MonitoredPlace pl,
  List<MapTarget> targets, {
  List<MapTarget> aviation = const [],
  String? officialLevel,
  String? ballisticFrom,
}) {
  var best = 0, inReach = 0;
  var reason = '';
  for (final t in targets) {
    final km = kmTo(t, pl);
    final s = _forTarget(t, pl, km);
    if (s <= 0) continue;
    inReach += t.amount < 1 ? 1 : t.amount;
    if (s > best) {
      best = s;
      reason = '${t.label}${t.amount > 1 ? ' ×${t.amount}' : ''} · ${km.round()} км';
    }
  }
  if (best > 0 && inReach >= 5) {
    best = (best + 1).clamp(0, 5);
    reason += ' · у повітрі поруч $inReach';
  }
  final obl = aviation.isEmpty ? null : OblastLookup.instance.nameAt(pl.lat, pl.lon);
  if (obl != null) {
    for (final z in aviation) {
      if (z.id.replaceFirst('avia:', '') != obl) continue;
      final kab = z.label.contains('КАБ');
      final s = kab ? 3 : 2;
      if (s > best) {
        best = s;
        reason = kab ? 'Загроза КАБ над областю' : 'Тактична авіація над областю';
      }
    }
  }
  if (ballisticFrom != null && best < 4) {
    best = 4;
    reason = 'Загроза балістики${ballisticFrom.isNotEmpty ? ' · район пуску: $ballisticFrom' : ''}';
  }
  if (officialLevel != null && best < 1) {
    best = 1;
    reason = 'Повітряна тривога, цілей поруч не видно';
  }
  final (level, advice) = describeStars(best);
  return DangerStars(best, level, advice, reason);
}

(String, String) describeStars(int stars) {
  if (stars >= 5) return ('Критична', 'Негайно в укриття! Подалі від вікон.');
  return switch (stars) {
    4 => ('Дуже висока', 'Не виходь надвір — іди в укриття.'),
    3 => ('Висока', 'Надворі небезпечно: залишайся в приміщенні, правило двох стін.'),
    2 => ('Помірна', 'Скороти час надворі й знай, де найближче укриття.'),
    1 => ('Низька', 'Можна виходити, але не затримуйся й тримай звук увімкненим.'),
    _ => ('Спокійно', 'Загроз поруч немає — можна гуляти.'),
  };
}
