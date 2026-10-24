/// A live target on the threat map (drone / missile / etc.).
///
/// Mirrors the `threats[]` objects the website map consumes, so the same
/// backend JSON can feed both.
class MapTarget {
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

  /// Per-place distance, filled in by the situation logic.
  final double? pdist;

  const MapTarget({
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
  });

  MapTarget withPdist(double? d) => MapTarget(
        label: label, cat: cat, lat: lat, lon: lon, zone: zone, stage: stage,
        heading: heading, amount: amount, place: place, dir: dir,
        stageLabel: stageLabel, icon: icon, distKm: distKm, etaMin: etaMin,
        seenMs: seenMs, trail: trail, pdist: d, approx: approx,
        sources: sources, srcs: srcs, kmh: kmh,
      );

  factory MapTarget.fromMap(Map<dynamic, dynamic> m) {
    double? d(dynamic v) => v == null ? null : (v as num).toDouble();
    return MapTarget(
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
      trail: (m['trail'] as List<dynamic>? ?? [])
          .map((e) => [
                ((e as List)[0] as num).toDouble(),
                (e)[1] == null ? 0.0 : (e[1] as num).toDouble(),
              ])
          .toList(),
    );
  }
}
