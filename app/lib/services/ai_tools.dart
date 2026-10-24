import 'dart:math' as math;

import '../controllers/map_data_controller.dart';
import '../controllers/places_controller.dart';
import '../controllers/settings_controller.dart';
import '../controllers/theme_controller.dart';
import '../data/places_gazetteer.dart';
import '../data/ukraine_geo.dart';
import '../domain/probability.dart';

/// Gives the AI assistant real *hands*: a small set of tools it can call to read
/// the live threat picture and to change the app (add places, tune settings…).
///
/// Provider-agnostic: [specs] is a plain JSON-schema list that both the
/// Anthropic (`tools`) and Gemini (`functionDeclarations`) payloads wrap.
/// [run] executes a call and returns a JSON-serialisable result map. Every
/// *change* also appends a short Ukrainian note to [log] so the UI can show the
/// user exactly what the assistant did.
class AiToolbox {
  AiToolbox({
    required this.map,
    required this.places,
    required this.settings,
    required this.theme,
  });

  final MapDataController map;
  final PlacesController places;
  final SettingsController settings;
  final ThemeController theme;

  /// Human-readable notes of the *changes* made this turn (for the chat footer).
  final List<String> log = [];

  // --- Tool catalogue --------------------------------------------------------

  List<Map<String, dynamic>> get specs => [
        _tool(
          'get_targets',
          'Отримати список поточних повітряних цілей (загроз) із живого монітора: '
              'тип, кількість, відстань від дому, ETA, напрямок, стадія.',
          {},
        ),
        _tool(
          'get_places',
          'Список збережених користувачем місць, які він відстежує, і чи '
              'увімкнені для них сповіщення.',
          {},
        ),
        _tool(
          'get_attack_probability',
          'Обчислити ймовірність удару (0–99%) по кожному місцю користувача та '
              'дому: відсоток, вердикт (прилетить/навряд) і головні чинники.',
          {},
        ),
        _tool(
          'get_settings',
          'Поточні налаштування застосунку: радіус тривоги, радіус чутності, '
              'гучність, тема.',
          {},
        ),
        _tool(
          'add_place',
          'Додати місто/село/район у «Мої місця» для відстеження. Координати '
              'визначаються автоматично за назвою.',
          {
            'name': _p('string', 'Назва міста, села або району (українською).'),
          },
          required: ['name'],
        ),
        _tool(
          'remove_place',
          'Видалити збережене місце зі списку відстеження за назвою.',
          {
            'name': _p('string', 'Назва місця, яке треба видалити.'),
          },
          required: ['name'],
        ),
        _tool(
          'set_place_notify',
          'Увімкнути або вимкнути сповіщення (дзвіночок) для збереженого місця.',
          {
            'name': _p('string', 'Назва місця.'),
            'on': _p('boolean', 'true — сповіщати, false — не сповіщати.'),
          },
          required: ['name', 'on'],
        ),
        _tool(
          'change_settings',
          'Змінити одне або кілька налаштувань. Передавай лише ті поля, які '
              'треба змінити.',
          {
            'alarm_radius_km':
                _p('number', 'Радіус тривоги, км (5–100). У межах цього радіуса '
                    'від місця вмикається сирена.'),
            'audibility_km':
                _p('number', 'Радіус чутності вибуху, км (3–60) — коло на карті.'),
            'volume_percent':
                _p('number', 'Гучність сирени у відсотках (0–100).'),
            'show_audibility':
                _p('boolean', 'Показувати коло чутності на карті.'),
            'dark_theme': _p('boolean', 'Темна тема (true) чи світла (false).'),
          },
        ),
        _tool(
          'focus_place',
          'Виділити місце на карті/як активне у застосунку. Передай "all" для '
              'усіх місць.',
          {
            'name': _p('string', 'Назва місця або "all".'),
          },
          required: ['name'],
        ),
      ];

  // --- Execution -------------------------------------------------------------

  Future<Map<String, dynamic>> run(String name, Map<String, dynamic> args) async {
    switch (name) {
      case 'get_targets':
        return _getTargets();
      case 'get_places':
        return _getPlaces();
      case 'get_attack_probability':
        return _getProbability();
      case 'get_settings':
        return _getSettings();
      case 'add_place':
        return _addPlace(_str(args['name']));
      case 'remove_place':
        return _removePlace(_str(args['name']));
      case 'set_place_notify':
        return _setNotify(_str(args['name']), _bool(args['on']));
      case 'change_settings':
        return _changeSettings(args);
      case 'focus_place':
        return _focus(_str(args['name']));
      default:
        return {'ok': false, 'error': 'Невідомий інструмент: $name'};
    }
  }

  // --- Reads -----------------------------------------------------------------

  Map<String, dynamic> _getTargets() {
    final home = map.home;
    final list = map.targets
        .map((t) => (t, t.distKm ?? _km(home.lat, home.lon, t.lat, t.lon)))
        .toList()
      ..sort((a, b) => a.$2.compareTo(b.$2));
    return {
      'ok': true,
      'live': map.live,
      'demo': map.isDemo,
      'count': list.length,
      'targets': [
        for (final (t, dist) in list.take(14))
          {
            'label': t.label,
            'kind': t.cat,
            'count': t.amount,
            'dist_km_from_home': dist.round(),
            if (t.etaMin != null) 'eta_min': t.etaMin!.round(),
            if (t.place.isNotEmpty) 'near': t.place,
            if (t.dir.isNotEmpty) 'direction': t.dir,
            'stage': t.stageLabel.isNotEmpty ? t.stageLabel : t.stage,
          },
      ],
    };
  }

  Map<String, dynamic> _getPlaces() {
    return {
      'ok': true,
      'places': [
        for (final p in places.places)
          {
            'name': p.name,
            if (p.region.isNotEmpty) 'region': p.region,
            'notify': p.notify,
          },
      ],
    };
  }

  Map<String, dynamic> _getProbability() {
    final rows = <Map<String, dynamic>>[];
    for (final pl in map.places) {
      final pr = probabilityFor(pl, map.targets);
      rows.add({
        'place': pl.name,
        'percent': pr.percent,
        'verdict': pr.verdict,
        if (pr.byKind.isNotEmpty) 'byKind': pr.byKind,
        'factors': [
          for (final f in pr.factors.take(3))
            {'label': f.target.label, 'dist_km': f.distKm.round(), 'percent': f.percent},
        ],
      });
    }
    rows.sort((a, b) => (b['percent'] as int).compareTo(a['percent'] as int));
    return {'ok': true, 'places': rows};
  }

  Map<String, dynamic> _getSettings() {
    return {
      'ok': true,
      'alarm_radius_km': settings.alarmRadiusKm.round(),
      'audibility_km': settings.audibilityKm.round(),
      'volume_percent': (settings.alarmVolume * 100).round(),
      'show_audibility': settings.showAudibility,
      'dark_theme': theme.dark,
    };
  }

  // --- Changes ---------------------------------------------------------------

  Map<String, dynamic> _addPlace(String query) {
    if (query.isEmpty) return {'ok': false, 'error': 'Порожня назва.'};
    // Already saved?
    final existing = places.places.where(
        (p) => p.name.toLowerCase() == query.toLowerCase());
    if (existing.isNotEmpty) {
      return {'ok': true, 'already': true, 'name': existing.first.name};
    }
    final hit = _resolve(query);
    if (hit == null) {
      return {
        'ok': false,
        'error': 'Не знайшов такого місця у довіднику.',
        'suggestions': _suggest(query),
      };
    }
    places.add(name: hit.name, region: '', lat: hit.lat, lng: hit.lon);
    log.add('додав «${hit.name}»');
    return {'ok': true, 'name': hit.name, 'lat': hit.lat, 'lon': hit.lon};
  }

  Map<String, dynamic> _removePlace(String query) {
    final p = _findSaved(query);
    if (p == null) {
      return {'ok': false, 'error': 'Такого місця немає у списку.'};
    }
    places.remove(p.id);
    log.add('видалив «${p.name}»');
    return {'ok': true, 'name': p.name};
  }

  Map<String, dynamic> _setNotify(String query, bool on) {
    final p = _findSaved(query);
    if (p == null) {
      return {'ok': false, 'error': 'Такого місця немає у списку.'};
    }
    if (p.notify != on) {
      places.toggleNotify(p.id);
      log.add('${on ? 'увімкнув' : 'вимкнув'} сповіщення для «${p.name}»');
    }
    return {'ok': true, 'name': p.name, 'notify': on};
  }

  Map<String, dynamic> _changeSettings(Map<String, dynamic> a) {
    final changed = <String>[];
    if (a['alarm_radius_km'] != null) {
      final v = _num(a['alarm_radius_km']).clamp(5, 100).toDouble();
      settings.setAlarmRadiusKm(v);
      changed.add('радіус тривоги ${v.round()} км');
    }
    if (a['audibility_km'] != null) {
      final v = _num(a['audibility_km']).clamp(3, 60).toDouble();
      settings.setAudibilityKm(v);
      changed.add('радіус чутності ${v.round()} км');
    }
    if (a['volume_percent'] != null) {
      final v = (_num(a['volume_percent']) / 100).clamp(0.0, 1.0);
      settings.setAlarmVolume(v);
      changed.add('гучність ${(v * 100).round()}%');
    }
    if (a['show_audibility'] != null) {
      final v = _bool(a['show_audibility']);
      settings.setShowAudibility(v);
      changed.add(v ? 'коло чутності увімкнено' : 'коло чутності вимкнено');
    }
    if (a['dark_theme'] != null) {
      final v = _bool(a['dark_theme']);
      theme.setDark(v);
      changed.add(v ? 'темна тема' : 'світла тема');
    }
    if (changed.isEmpty) {
      return {'ok': false, 'error': 'Не передано жодного налаштування.'};
    }
    log.addAll(changed);
    return {'ok': true, 'changed': changed, ...(_getSettings()..remove('ok'))};
  }

  Map<String, dynamic> _focus(String query) {
    final q = query.trim().toLowerCase();
    if (q == 'all' || q == 'всі' || q == 'все' || q.isEmpty) {
      map.select('all');
      log.add('показав усі місця на карті');
      return {'ok': true, 'focus': 'all'};
    }
    for (final p in map.places) {
      if (p.name.toLowerCase() == q || p.name.toLowerCase().contains(q)) {
        map.select(p.id);
        log.add('виділив «${p.name}» на карті');
        return {'ok': true, 'focus': p.name};
      }
    }
    return {'ok': false, 'error': 'Місце «$query» не збережене — спершу додай.'};
  }

  // --- Geocoding + lookup helpers -------------------------------------------

  /// All searchable places: the big city list first (so a name resolves to the
  /// largest match), then our curated Kharkiv/Kyiv neighbourhoods.
  static final List<GeoPlace> _search = () {
    final seen = <String>{};
    final out = <GeoPlace>[];
    for (final c in kCities) {
      if (seen.add(c.name.toLowerCase())) out.add(GeoPlace(c.name, c.lat, c.lon));
    }
    for (final g in kGazetteer) {
      if (seen.add(g.name.toLowerCase())) out.add(g);
    }
    return out;
  }();

  GeoPlace? _resolve(String query) {
    final s = query.trim().toLowerCase();
    if (s.isEmpty) return null;
    GeoPlace? starts;
    GeoPlace? contains;
    for (final c in _search) {
      final n = c.name.toLowerCase();
      if (n == s) return c;
      if (starts == null && n.startsWith(s)) starts = c;
      if (contains == null && n.contains(s)) contains = c;
    }
    return starts ?? contains;
  }

  List<String> _suggest(String query) {
    final s = query.trim().toLowerCase();
    if (s.isEmpty) return const [];
    final out = <String>[];
    for (final c in _search) {
      if (c.name.toLowerCase().contains(s)) {
        out.add(c.name);
        if (out.length >= 5) break;
      }
    }
    return out;
  }

  dynamic _findSaved(String query) {
    final s = query.trim().toLowerCase();
    if (s.isEmpty) return null;
    for (final p in places.places) {
      if (p.name.toLowerCase() == s) return p;
    }
    for (final p in places.places) {
      if (p.name.toLowerCase().contains(s)) return p;
    }
    return null;
  }

  static double _km(double la1, double lo1, double la2, double lo2) {
    const r = 6371.0, rad = math.pi / 180;
    final dLa = (la2 - la1) * rad, dLo = (lo2 - lo1) * rad;
    final h = math.sin(dLa / 2) * math.sin(dLa / 2) +
        math.cos(la1 * rad) * math.cos(la2 * rad) *
            math.sin(dLo / 2) * math.sin(dLo / 2);
    return r * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h));
  }

  // --- Tiny schema/arg helpers ----------------------------------------------

  static Map<String, dynamic> _tool(
    String name,
    String description,
    Map<String, dynamic> properties, {
    List<String> required = const [],
  }) =>
      {
        'name': name,
        'description': description,
        'input_schema': {
          'type': 'object',
          'properties': properties,
          if (required.isNotEmpty) 'required': required,
        },
      };

  static Map<String, dynamic> _p(String type, String description) =>
      {'type': type, 'description': description};

  static String _str(dynamic v) => v?.toString().trim() ?? '';
  static bool _bool(dynamic v) =>
      v is bool ? v : v?.toString().toLowerCase() == 'true';
  static double _num(dynamic v) =>
      v is num ? v.toDouble() : double.tryParse(v?.toString() ?? '') ?? 0;
}
