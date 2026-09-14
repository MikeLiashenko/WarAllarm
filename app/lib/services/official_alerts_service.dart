import 'dart:convert';

import 'package:http/http.dart' as http;

/// Fetches the OFFICIAL Ukrainian air-raid alarm status per oblast (no token
/// needed) from the public Vadym Klymenko feed. Returns a map of our oblast
/// polygon name -> level ('red' = active air-raid, 'yellow' = warning/partial).
class OfficialAlertsService {
  OfficialAlertsService({http.Client? client})
      : _client = client ?? http.Client();

  final http.Client _client;
  static const _url = 'https://vadimklimenko.com/map/statuses.json';

  /// Ukrainian state name (as the feed uses) -> our oblast polygon `n`.
  static const Map<String, String> _uaToEn = {
    'АР Крим': 'Crimea',
    'Севастополь': "Sevastopol'",
    'Вінницька область': 'Vinnytsya',
    'Волинська область': 'Volyn',
    'Дніпропетровська область': "Dnipropetrovs'k",
    'Донецька область': "Donets'k",
    'Житомирська область': 'Zhytomyr',
    'Закарпатська область': 'Transcarpathia',
    'Запорізька область': 'Zaporizhzhya',
    'Івано-Франківська область': "Ivano-Frankivs'k",
    'Київська область': 'Kiev',
    'Кіровоградська область': 'Kirovohrad',
    'Луганська область': "Luhans'k",
    'Львівська область': "L'viv",
    'Миколаївська область': 'Mykolayiv',
    'Одеська область': 'Odessa',
    'Полтавська область': 'Poltava',
    'Рівненська область': 'Rivne',
    'Сумська область': 'Sumy',
    'Тернопільська область': "Ternopil'",
    'Харківська область': 'Kharkiv',
    'Херсонська область': 'Kherson',
    'Хмельницька область': "Khmel'nyts'kyy",
    'Черкаська область': 'Cherkasy',
    'Чернівецька область': 'Chernivtsi',
    'Чернігівська область': 'Chernihiv',
    'м. Київ': 'Kiev City',
  };

  /// Legacy: oblast polygon name -> level (whole-oblast red, or yellow if any
  /// district is on). Kept for callers that only need the oblast view.
  Future<Map<String, String>> fetch() async => (await fetchFull()).oblasts;

  /// Full snapshot for the raion-level map split (like the official map).
  Future<OfficialAlertsSnapshot> fetchFull() async {
    final res = await _client.get(
      Uri.parse(_url),
      headers: const {'User-Agent': 'WarAlarm/1.0'},
    );
    if (res.statusCode != 200) {
      throw Exception('alerts HTTP ${res.statusCode}');
    }
    final data = jsonDecode(utf8.decode(res.bodyBytes));
    final states = (data is Map ? data['states'] : null);
    final oblasts = <String, String>{};       // legacy per-oblast view
    final raions = <String, String>{};         // "X район" -> level
    final oblastWhole = <String, String>{};    // "n" -> level (whole oblast only)
    if (states is Map) {
      states.forEach((k, v) {
        if (v is! Map) return;
        final en = _uaToEn[k.toString()];
        if (en == null) return;
        final lvl = (v['alert_level'] ?? '').toString();

        var districtsOn = 0;
        final ds = v['districts'];
        if (ds is Map) {
          ds.forEach((dk, dv) {
            if (dv is! Map || dv['enabled'] != true) return;
            districtsOn++;
            final dlvl = (dv['alert_level'] ?? '').toString();
            raions[dk.toString()] = dlvl == 'yellow' ? 'yellow' : 'red';
          });
        }

        if (v['enabled'] == true) {
          final l = lvl == 'yellow' ? 'yellow' : 'red';
          oblasts[en] = l;
          oblastWhole[en] = l; // whole-oblast alarm → fill the whole region
        } else if (districtsOn > 0) {
          oblasts[en] = 'yellow';
        }
      });
    }
    return OfficialAlertsSnapshot(oblasts, raions, oblastWhole);
  }
}

/// Official alerts at two levels: whole oblasts and individual raions.
class OfficialAlertsSnapshot {
  final Map<String, String> oblasts;      // legacy per-oblast
  final Map<String, String> raions;       // "X район" -> red/yellow
  final Map<String, String> oblastWhole;  // "n" -> red/yellow (whole oblast)
  const OfficialAlertsSnapshot(this.oblasts, this.raions, this.oblastWhole);
}
