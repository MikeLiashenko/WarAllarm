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

  Future<Map<String, String>> fetch() async {
    final res = await _client.get(
      Uri.parse(_url),
      headers: const {'User-Agent': 'WarAlarm/1.0'},
    );
    if (res.statusCode != 200) {
      throw Exception('alerts HTTP ${res.statusCode}');
    }
    final data = jsonDecode(utf8.decode(res.bodyBytes));
    final states = (data is Map ? data['states'] : null);
    final out = <String, String>{};
    if (states is Map) {
      states.forEach((k, v) {
        if (v is! Map) return;
        final en = _uaToEn[k.toString()];
        if (en == null) return;
        final lvl = (v['alert_level'] ?? '').toString();

        // Count district (hromada) alerts inside this oblast.
        var districtsOn = 0;
        final ds = v['districts'];
        if (ds is Map) {
          ds.forEach((_, dv) {
            if (dv is Map && dv['enabled'] == true) districtsOn++;
          });
        }

        if (v['enabled'] == true) {
          // Oblast-wide alarm: red, unless the feed marks it yellow (e.g. Kyiv).
          out[en] = lvl == 'yellow' ? 'yellow' : 'red';
        } else if (districtsOn > 0) {
          // Only some districts under alarm → partial → yellow (matches the
          // official map, which shows these oblasts orange/yellow).
          out[en] = 'yellow';
        }
      });
    }
    return out;
  }
}
