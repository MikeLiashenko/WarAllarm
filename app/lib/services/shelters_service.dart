import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import 'package:path_provider/path_provider.dart';

/// A nearby shelter (bomb shelter / metro / underground parking) from OpenStreetMap.
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
///
/// Was broken (26.11): one Overpass server, no User-Agent, 25 s wait — when it was
/// busy or refused, the sheet failed; and `amenity=shelter` is mostly bus-stop
/// canopies and gazebos, which came up as «Укриття 40 м». Now: several mirrors in
/// turn, an identifying User-Agent, bus stops/gazebos excluded, bunkers and
/// underground parking added, entrances of one metro station merged.
class SheltersService {
  SheltersService({http.Client? client}) : _client = client ?? http.Client();
  final http.Client _client;

  static const _mirrors = [
    'https://overpass-api.de/api/interpreter',
    'https://overpass.private.coffee/api/interpreter',
    'https://overpass.kumi.systems/api/interpreter',
  ];

  /// Overpass asks clients to identify themselves; anonymous requests get refused.
  static const _ua = 'WarAlarm/1.0 (shelter search)';

  static String query(double lat, double lon, int radiusM) {
    final a = '(around:$radiusM,$lat,$lon)';
    return '[out:json][timeout:12];('
        'nw$a["amenity"="shelter"]'
        '["shelter_type"!~"^(public_transport|picnic_shelter|weather_shelter|lean_to|basic_hut|field_shelter|rock_shelter|sun_shelter|gazebo|changing_rooms)\$"]'
        '["public_transport"!~"."]["bus"!="yes"];'
        'nw$a["building"="bunker"];'
        'node$a["railway"="subway_entrance"];'
        'nw$a["amenity"="parking"]["parking"="underground"];'
        ');out center tags 60;';
  }

  /// Weekly file of all shelters in Ukraine from OSM (built by GitHub, «shelters-latest»):
  /// searched on the phone, instant and without Overpass, which is often down during raids.
  static const _dataUrl =
      'https://github.com/MikeLiashenko/WarAllarm/releases/download/shelters-latest/shelters.json';
  static List<List<dynamic>>? _points;

  static List<List<dynamic>> _decode(String body) => [
        for (final p in (jsonDecode(body) as Map<String, dynamic>)['p'] as List) (p as List),
      ];

  Future<List<List<dynamic>>?> _dataset() async {
    if (_points != null) return _points;
    File? f;
    try {
      f = File('${(await getApplicationSupportDirectory()).path}/shelters.json');
      if (await f.exists() && DateTime.now().difference(await f.lastModified()).inDays < 8) {
        return _points = await compute(_decode, await f.readAsString());
      }
    } catch (_) {}
    try {
      final res = await _client
          .get(Uri.parse(_dataUrl), headers: {'User-Agent': _ua})
          .timeout(const Duration(seconds: 30));
      if (res.statusCode == 200) {
        final body = utf8.decode(res.bodyBytes);
        final pts = await compute(_decode, body);
        try {
          await f?.writeAsString(body);
        } catch (_) {}
        return _points = pts;
      }
    } catch (_) {}
    // No network for the fresh file — an older copy is still far better than nothing.
    try {
      if (f != null && await f.exists()) return _points = await compute(_decode, await f.readAsString());
    } catch (_) {}
    return null;
  }

  /// Nearest from the local file (within 1.5 km, else 3 km); no file — live Overpass:
  /// first 1.5 km (a light query for a busy server), nothing there — 3 km.
  Future<List<Shelter>> near(double lat, double lon) async {
    final pts = await _dataset();
    if (pts != null) {
      final r1 = local(pts, lat, lon, 1.5);
      return r1.isNotEmpty ? r1 : local(pts, lat, lon, 3);
    }
    final first = await _ask(lat, lon, 1500);
    if (first.isNotEmpty) return first;
    return _ask(lat, lon, 3000);
  }

  /// Nearest points of the file within [km], one line per place.
  static List<Shelter> local(List<List<dynamic>> pts, double lat, double lon, double km) {
    final dLat = km / 111.0, dLon = km / (111.0 * math.cos(lat * math.pi / 180));
    final all = <Shelter>[];
    for (final p in pts) {
      final pl = (p[0] as num).toDouble(), po = (p[1] as num).toDouble();
      if ((pl - lat).abs() > dLat || (po - lon).abs() > dLon) continue;
      final d = _km(lat, lon, pl, po);
      if (d <= km) all.add(Shelter((p[3] as String?)?.isNotEmpty == true ? p[3] as String : 'Укриття', pl, po, d));
    }
    return _merge(all);
  }

  Future<List<Shelter>> _ask(double lat, double lon, int radiusM) async {
    final q = query(lat, lon, radiusM);
    Object? lastError;
    for (final url in _mirrors) {
      for (var attempt = 0; attempt < 2; attempt++) {
        try {
          final res = await _client
              .post(Uri.parse(url), headers: {'User-Agent': _ua}, body: {'data': q})
              .timeout(const Duration(seconds: 15));
          if (res.statusCode == 200) {
            final data = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
            return parse(data, lat, lon);
          }
          lastError = 'HTTP ${res.statusCode}';
          if (res.statusCode == 400 || res.statusCode == 406) break; // a retry won't help
        } catch (e) {
          lastError = e;
        }
        await Future<void>.delayed(const Duration(seconds: 2));
      }
    }
    throw Exception('Overpass unavailable: $lastError');
  }

  static List<Shelter> parse(Map<String, dynamic> data, double lat, double lon) {
    final els = data['elements'] as List<dynamic>? ?? const [];
    final all = <Shelter>[];
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
      all.add(Shelter(_label(e['tags']), elat, elon, _km(lat, lon, elat, elon)));
    }
    return _merge(all);
  }

  static List<Shelter> _merge(List<Shelter> all) {
    all.sort((x, y) => x.distKm.compareTo(y.distKm));
    // One line per place: several entrances of the same station, a station and its
    // entrance, a shelter drawn as both a node and a building — within 120 m.
    final out = <Shelter>[];
    for (final s in all) {
      // Same name (another entrance of «Майдан Незалежності») — within 500 m; metro entrances — 120 m.
      if (out.any((o) {
        final d = _km(o.lat, o.lon, s.lat, s.lon);
        return o.name == s.name && d < 0.5 ||
            d < 0.12 && o.name.toLowerCase().contains('метро') && s.name.toLowerCase().contains('метро');
      })) {
        continue;
      }
      out.add(s);
      if (out.length >= 8) break;
    }
    return out;
  }

  static String _label(dynamic tags) {
    if (tags is! Map) return 'Укриття';
    final n = (tags['name'] as String?)?.trim() ?? '';
    final st = (tags['station'] ?? '') == 'subway' || tags['subway'] == 'yes';
    if (tags['railway'] == 'subway_entrance') {
      final ref = (tags['ref'] as String?)?.trim();
      return n.isNotEmpty ? 'Метро «$n» (вхід)' : ref != null && ref.isNotEmpty ? 'Вхід у метро №$ref' : 'Вхід у метро';
    }
    if (st) return n.isNotEmpty ? 'Метро «$n»' : 'Станція метро';
    if (tags['amenity'] == 'parking') return n.isNotEmpty ? 'Підземний паркінг «$n»' : 'Підземний паркінг';
    if (tags['building'] == 'bunker' || tags['bunker_type'] != null) {
      return n.isNotEmpty ? n : 'Укриття (сховище)';
    }
    final addr = [tags['addr:street'], tags['addr:housenumber']].whereType<String>().join(', ');
    return n.isNotEmpty ? n : addr.isNotEmpty ? 'Укриття · $addr' : 'Укриття';
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
