/// A live target on the threat map (drone / missile / etc.).
///
/// Mirrors the `threats[]` objects the website map consumes, so the same
/// backend JSON can feed both.
class MapTarget {
  final String label;
  final String cat; // shahed | missile | balist | kab | avia | other
  final double lat;
  final double lon;
  final double? heading; // degrees, 0 = North
  final int amount; // ×N cluster size
  final String place;
  final String dir;
  final String stageLabel;
  final double? distKm;
  final double? etaMin;
  final int seenMs; // last time this position was observed
  final List<List<double>> trail; // [[lat,lon], ...]

  const MapTarget({
    required this.label,
    required this.cat,
    required this.lat,
    required this.lon,
    this.heading,
    this.amount = 1,
    this.place = '',
    this.dir = '',
    this.stageLabel = '',
    this.distKm,
    this.etaMin,
    this.seenMs = 0,
    this.trail = const [],
  });

  factory MapTarget.fromMap(Map<dynamic, dynamic> m) {
    double? d(dynamic v) => v == null ? null : (v as num).toDouble();
    return MapTarget(
      label: (m['label'] ?? 'Ціль').toString(),
      cat: (m['cat'] ?? 'other').toString(),
      lat: (m['lat'] as num).toDouble(),
      lon: (m['lon'] as num).toDouble(),
      heading: d(m['heading']),
      amount: (m['n'] as num?)?.toInt() ?? 1,
      place: (m['place'] ?? '').toString(),
      dir: (m['dir'] ?? '').toString(),
      stageLabel: (m['stageLabel'] ?? '').toString(),
      distKm: d(m['dist']),
      etaMin: d(m['eta']),
      seenMs: (m['seen'] as num?)?.toInt() ?? 0,
      trail: (m['trail'] as List<dynamic>? ?? [])
          .map((e) => [
                ((e as List)[0] as num).toDouble(),
                (e)[1] == null ? 0.0 : (e[1] as num).toDouble(),
              ])
          .toList(),
    );
  }
}
