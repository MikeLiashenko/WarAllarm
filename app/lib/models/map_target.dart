/// A live target on the threat map (drone / missile / etc.).
///
/// Mirrors the `threats[]` objects the website map consumes, so the same
/// backend JSON can feed both.
class MapTarget {
  /// Stable id from the Reader (select / follow a target across updates).
  final String id;
  final String label;
  final String cat; // shahed | missile | balist | kab | avia | other
  final String zone; // e.g. "Харків" / "Київ" / "UA"
  final String stage; // Approaching | Imminent | Overhead | Impact | Passed
  final double lat;
  final double lon;
  final double? heading; // degrees, 0 = North
  final int amount; // ×N cluster size
  final String place;
  final String dir;
  final String stageLabel;
  final String icon;
  final double? distKm;
  final double? etaMin;
  final int seenMs; // last time this position was observed
  final List<List<double>> trail; // [[lat,lon], ...]
  /// Приблизна точка: місто не знайдено → центр області (для Київщини це сам Київ).
  /// Показуємо на карті, але НЕ рахуємо для тривоги.
  final bool approx;

  /// Скільки незалежних каналів бачили ціль (хаб джерел Reader) і перші з них.
  final int sources;
  final List<String> srcs;

  /// Швидкість засобу, км/год (словник Reader: Х-101 750, Іскандер-М 7000…); 0 — невідома.
  final double kmh;

  /// Траєкторія: звідки ([lat, lon]) → куди, і коли почався політ (мс). Для балістики/КАБ —
  /// політ From→To з реальною швидкістю; для дронів — звідки йде курс.
  final List<double>? from;
  final List<double>? to;
  final String fromName;
  /// Район пуску / точку скиду оцінив Reader (канал не назвав).
  final bool fromGuess;
  final int t0;
  /// Лише пуск, ціль ще невідома (коло досяжності з точки пуску).
  final bool launch;

  /// Курс із реального руху (дві точки / «з X на Y»). Без нього курс — лише здогадка
  /// Reader «вглиб країни», і маркер НЕ рухаємо (інакше дрони «летіли» в зони відбою).
  final bool headingKnown;

  /// Висота з тексту каналу, м (null — не писали) і примітка: «низько», «знижується»,
  /// «набирає висоту», «падає», «збито».
  final double? altM;
  final String altNote;

  /// Падає / збита — зараз зникне з карти.
  final bool down;

  /// Типовий маршрут далі ([[lat, lon], …], перша точка — де ціль зараз): на Київ / на
  /// захід або вздовж реального курсу. Застосунок веде мітку ним між постами.
  final List<List<double>> route;

  /// Група пуску в польоті — ПРОГНОЗ типовим маршрутом від місця пуску (не справжня
  /// ціль): блідо на карті, не вмикає тривогу й не рахується в лічильниках.
  final bool forecast;

  /// Per-place distance, filled in by the situation logic.
  final double? pdist;

  const MapTarget({
    this.id = '',
    required this.label,
    required this.cat,
    required this.lat,
    required this.lon,
    this.zone = 'UA',
    this.stage = 'Approaching',
    this.heading,
    this.amount = 1,
    this.place = '',
    this.dir = '',
    this.stageLabel = '',
    this.icon = '',
    this.distKm,
    this.etaMin,
    this.seenMs = 0,
    this.trail = const [],
    this.approx = false,
    this.pdist,
    this.sources = 0,
    this.srcs = const [],
    this.kmh = 0,
    this.from,
    this.to,
    this.fromName = '',
    this.fromGuess = false,
    this.t0 = 0,
    this.launch = false,
    this.headingKnown = false,
    this.altM,
    this.altNote = '',
    this.down = false,
    this.route = const [],
    this.forecast = false,
  });

  MapTarget withPdist(double? d) => MapTarget(
        id: id, label: label, cat: cat, lat: lat, lon: lon, zone: zone, stage: stage,
        heading: heading, amount: amount, place: place, dir: dir,
        stageLabel: stageLabel, icon: icon, distKm: distKm, etaMin: etaMin,
        seenMs: seenMs, trail: trail, pdist: d, approx: approx,
        sources: sources, srcs: srcs, kmh: kmh,
        from: from, to: to, fromName: fromName, fromGuess: fromGuess,
        t0: t0, launch: launch,
        headingKnown: headingKnown, altM: altM, altNote: altNote, down: down, route: route, forecast: forecast,
      );

  static List<List<double>> _route(dynamic v) => v is List
      ? [for (final p in v) if (p is List && p.length == 2) [(p[0] as num).toDouble(), (p[1] as num).toDouble()]]
      : const [];

  static List<double>? _pair(dynamic v) =>
      v is List && v.length == 2 ? [(v[0] as num).toDouble(), (v[1] as num).toDouble()] : null;

  factory MapTarget.fromMap(Map<dynamic, dynamic> m) {
    double? d(dynamic v) => v == null ? null : (v as num).toDouble();
    return MapTarget(
      id: (m['id'] ?? '').toString(),
      label: (m['label'] ?? 'Ціль').toString(),
      cat: (m['cat'] ?? 'other').toString(),
      zone: (m['zone'] ?? 'UA').toString(),
      stage: (m['stage'] ?? 'Approaching').toString(),
      lat: (m['lat'] as num).toDouble(),
      lon: (m['lon'] as num).toDouble(),
      heading: d(m['heading']),
      amount: (m['n'] as num?)?.toInt() ?? 1,
      place: (m['place'] ?? '').toString(),
      dir: (m['dir'] ?? '').toString(),
      stageLabel: (m['stageLabel'] ?? '').toString(),
      icon: (m['icon'] ?? '').toString(),
      distKm: d(m['dist']),
      etaMin: d(m['eta']),
      seenMs: (m['seen'] as num?)?.toInt() ?? 0,
      approx: m['approx'] == true,
      sources: (m['src'] as num?)?.toInt() ?? 0,
      srcs: (m['srcs'] as List<dynamic>? ?? []).map((e) => e.toString()).toList(),
      kmh: d(m['kmh']) ?? 0,
      from: _pair(m['from']),
      to: _pair(m['to']),
      fromName: (m['fromName'] ?? '').toString(),
      fromGuess: m['fromGuess'] == true,
      t0: (m['t0'] as num?)?.toInt() ?? 0,
      launch: m['launch'] == true,
      headingKnown: m['hdgReal'] == true,
      altM: d(m['alt']),
      altNote: (m['altNote'] ?? '').toString(),
      down: m['down'] == true,
      route: _route(m['route']),
      trail: (m['trail'] as List<dynamic>? ?? [])
          .map((e) => [
                ((e as List)[0] as num).toDouble(),
                (e)[1] == null ? 0.0 : (e[1] as num).toDouble(),
              ])
          .toList(),
    );
  }
}
