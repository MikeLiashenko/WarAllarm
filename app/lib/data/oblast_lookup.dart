import 'dart:convert';

import 'ukraine_oblasts.dart';

/// Point-in-oblast lookup over the same polygon set the map draws
/// ([kOblastsJson]). Used to attach the official (red/yellow) alert level of a
/// place's oblast to its attack-probability estimate.
class OblastLookup {
  OblastLookup._(this._polys);

  final List<_Poly> _polys;

  static OblastLookup? _instance;
  static OblastLookup get instance => _instance ??= _build();

  static OblastLookup _build() {
    final raw = jsonDecode(kOblastsJson) as List<dynamic>;
    final polys = <_Poly>[];
    for (final o in raw) {
      if (o is! Map) continue;
      final name = (o['n'] ?? '').toString();
      final rings = <List<List<double>>>[];
      for (final r in (o['r'] as List<dynamic>? ?? const [])) {
        final ring = <List<double>>[];
        for (final p in (r as List<dynamic>)) {
          ring.add([(p[0] as num).toDouble(), (p[1] as num).toDouble()]);
        }
        if (ring.length >= 3) rings.add(ring);
      }
      if (rings.isNotEmpty) polys.add(_Poly(name, rings));
    }
    return OblastLookup._(polys);
  }

  /// Is [lat],[lon] inside the oblast polygon called [name]? (One polygon only —
  /// cheap enough to call along a whole route.)
  bool contains(String name, double lat, double lon) {
    for (final poly in _polys) {
      if (poly.name != name) continue;
      var inside = false;
      for (final ring in poly.rings) {
        for (var i = 0, j = ring.length - 1; i < ring.length; j = i++) {
          final xi = ring[i][0], yi = ring[i][1];
          final xj = ring[j][0], yj = ring[j][1];
          if (((yi > lat) != (yj > lat)) && (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)) inside = !inside;
        }
      }
      return inside;
    }
    return false;
  }

  /// English polygon name of the oblast containing [lat],[lon], or null.
  /// Even-odd across all rings handles the Kyiv-oblast hole automatically.
  String? nameAt(double lat, double lon) {
    for (final poly in _polys) {
      var inside = false;
      for (final ring in poly.rings) {
        for (var i = 0, j = ring.length - 1; i < ring.length; j = i++) {
          final xi = ring[i][0], yi = ring[i][1];
          final xj = ring[j][0], yj = ring[j][1];
          final intersect = ((yi > lat) != (yj > lat)) &&
              (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi);
          if (intersect) inside = !inside;
        }
      }
      if (inside) return poly.name;
    }
    return null;
  }
}

class _Poly {
  final String name;
  final List<List<List<double>>> rings; // [ring][point][lon,lat]
  _Poly(this.name, this.rings);
}
