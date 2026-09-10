import 'dart:convert';
import 'dart:math' as math;

import 'package:http/http.dart' as http;

/// A nearby shelter (bomb shelter / metro) from OpenStreetMap.
class Shelter {
  final String name;
  final double lat;
  final double lon;
  final double distKm;
  const Shelter(this.name, this.lat, this.lon, this.distKm);

  int get meters => (distKm * 1000).round();

  /// Walking-directions link for the phone's maps app.
  String get mapsUrl =>
      'https://www.google.com/maps/dir/?api=1&destination=$lat,$lon&travelmode=walking';
}

/// Finds the nearest shelters + metro around a point via the OSM Overpass API.
/// Data is community-mapped and incomplete — a hint, not a substitute for
/// knowing your own nearest shelter.
class SheltersService {
  SheltersService({http.Client? client}) : _client = client ?? http.Client();
  final http.Client _client;
  static const _api = 'https://overpass-api.de/api/interpreter';

  Future<List<Shelter>> near(double lat, double lon, {int radiusM = 3000}) async {
    final a = '(around:$radiusM,$lat,$lon)';
    final query = '[out:json][timeout:20];('
        'node$a["amenity"="shelter"];'
        'way$a["amenity"="shelter"];'
        'node$a["railway"="subway_entrance"];'
        'node$a["station"="subway"];'
        'node$a["railway"="station"]["subway"="yes"];'
        ');out center 60;';

    final res = await _client
        .post(Uri.parse(_api), body: {'data': query})
        .timeout(const Duration(seconds: 25));
    if (res.statusCode != 200) return [];
    final data = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
    final els = data['elements'] as List<dynamic>? ?? const [];

    final out = <Shelter>[];
    for (final e in els) {
      if (e is! Map) continue;
      double? elat, elon;
      if (e['lat'] != null && e['lon'] != null) {
        elat = (e['lat'] as num).toDouble();
        elon = (e['lon'] as num).toDouble();
      } else if (e['center'] is Map) {
        final c = e['center'] as Map;
        elat = (c['lat'] as num).toDouble();
        elon = (c['lon'] as num).toDouble();
      }
      if (elat == null || elon == null) continue;
      out.add(Shelter(_label(e['tags']), elat, elon, _km(lat, lon, elat, elon)));
    }
    out.sort((x, y) => x.distKm.compareTo(y.distKm));
    return out.take(8).toList();
  }

  static String _label(dynamic tags) {
    if (tags is Map) {
      final n = tags['name'];
      if (n is String && n.trim().isNotEmpty) return n;
      final rw = tags['railway'];
      if (rw == 'subway_entrance') return 'Вхід у метро';
      if (rw == 'station') return 'Станція метро';
      if (tags['station'] != null) return 'Станція метро';
      if (tags['amenity'] == 'shelter') return 'Укриття';
    }
    return 'Укриття';
  }

  static double _km(double la1, double lo1, double la2, double lo2) {
    const r = 6371.0, rad = math.pi / 180;
    final dLa = (la2 - la1) * rad, dLo = (lo2 - lo1) * rad;
    final h = math.sin(dLa / 2) * math.sin(dLa / 2) +
        math.cos(la1 * rad) * math.cos(la2 * rad) *
            math.sin(dLo / 2) * math.sin(dLo / 2);
    return r * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h));
  }
}
