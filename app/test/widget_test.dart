// Widget tests for WarAlarm building blocks.
//
// The full app opens live RTDB streams (SSE) which the test binding blocks, so
// we test the presentation widgets in isolation instead.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:waralarm/data/oblast_lookup.dart';
import 'package:waralarm/domain/alarm_forecast.dart';
import 'package:waralarm/models/api_key.dart';
import 'package:waralarm/models/map_target.dart';
import 'package:waralarm/models/monitored_place.dart';
import 'package:waralarm/domain/motion.dart';
import 'package:waralarm/domain/situation.dart';
import 'package:waralarm/widgets/section_header.dart';

void main() {
  alarmForecastTests();
  testWidgets('SectionHeader renders title and subtitle', (tester) async {
    await tester.pumpWidget(const MaterialApp(
      home: Scaffold(
        body: SectionHeader(
          title: 'Threat map',
          subtitle: 'Monitored zones',
          icon: Icons.map_rounded,
        ),
      ),
    ));

    expect(find.text('Threat map'), findsOneWidget);
    expect(find.text('Monitored zones'), findsOneWidget);
    expect(find.byIcon(Icons.map_rounded), findsOneWidget);
  });

  test('placeThreats keeps targets within 40km and sorts by distance', () {
    const kharkiv = MonitoredPlace(
        id: 'home', icon: '🏠', name: 'Харків', lat: 49.99, lon: 36.23);
    final near = MapTarget(
        label: 'Shahed', cat: 'shahed', lat: 50.10, lon: 36.30, stage: 'Imminent');
    final far = MapTarget(
        label: 'Missile', cat: 'missile', lat: 48.0, lon: 30.0, zone: 'UA');

    final result = placeThreats(kharkiv, [far, near]);
    expect(result.first.label, 'Shahed'); // nearest first
    expect(result.any((t) => t.label == 'Missile'), isFalse); // >40km, other zone
  });

  test('riskOf returns a high score for an imminent nearby threat', () {
    const kharkiv = MonitoredPlace(
        id: 'home', icon: '🏠', name: 'Харків', lat: 49.99, lon: 36.23);
    final t = MapTarget(
        label: 'Балістика', cat: 'balist', lat: 50.0, lon: 36.25, stage: 'Imminent');
    expect(riskOf(kharkiv, [t]).score, greaterThanOrEqualTo(85));
  });

  test('ApiKey.masked hides the middle of the key', () {
    final key = ApiKey(
      id: 'a',
      label: 'Prod',
      key: 'wa_live_abcdef1234567890',
      createdAt: DateTime.now(),
    );

    expect(key.masked.startsWith('wa_live_ab'), isTrue);
    expect(key.masked.contains('•'), isTrue);
    expect(key.masked.endsWith('7890'), isTrue);
  });

  test('ballistic flies From→To at its real speed, then lands', () {
    // Kursk → Kharkiv (~194 km) at 4000 km/h ≈ 2.9 min.
    final t0 = DateTime(2026, 10, 24, 12).millisecondsSinceEpoch;
    final t = MapTarget(
        label: 'Балістика', cat: 'balist', lat: 49.98, lon: 36.25,
        kmh: 4000, from: const [51.73, 36.19], to: const [49.98, 36.25], t0: t0);
    final half = motionOf(t, t0 + 87 * 1000);
    expect(half.isFlight, isTrue);
    expect(half.progress!, closeTo(0.5, 0.05));
    expect(half.lat, closeTo((51.73 + 49.98) / 2, 0.1));
    expect(half.landed, isFalse);
    expect(motionOf(t, t0 + 4 * 60 * 1000).landed, isTrue);
  });

  test('launch without a target grows a reach ring', () {
    final t0 = DateTime(2026, 10, 24, 12).millisecondsSinceEpoch;
    final t = MapTarget(
        label: 'Балістика', cat: 'balist', lat: 53.24, lon: 34.37,
        kmh: 4000, from: const [53.24, 34.37], t0: t0, launch: true);
    final m = motionOf(t, t0 + 60 * 1000);
    expect(m.launch, isTrue);
    expect(m.reachKm, closeTo(4000 / 60, 1));
  });

  test('drone creeps along a REAL course only briefly, then an uncertainty ring grows', () {
    final seen = DateTime(2026, 10, 24, 12).millisecondsSinceEpoch;
    final t = MapTarget(
        label: 'Шахед', cat: 'shahed', lat: 50.0, lon: 30.0,
        heading: 0, kmh: 180, seenMs: seen, headingKnown: true);
    // 10 min of silence: moved only 4 min × 3 km/min × 0.7 ≈ 8.4 km north, then stopped.
    final m = motionOf(t, seen + 10 * 60 * 1000);
    expect(kmBetween(50.0, 30.0, m.lat, m.lon), closeTo(8.4, 0.5));
    expect(m.lat, greaterThan(50.0));
    expect(m.uncertKm, closeTo(15, 0.5)); // 3 km/min × 0.5 × 10 min
  });

  test('a guessed course (seen in one place only) does not move the drone', () {
    final seen = DateTime(2026, 10, 24, 12).millisecondsSinceEpoch;
    final t = MapTarget(
        label: 'Шахед', cat: 'shahed', lat: 50.0, lon: 30.0,
        heading: 270, kmh: 500, seenMs: seen);
    final m = motionOf(t, seen + 12 * 60 * 1000);
    expect(m.lat, 50.0);
    expect(m.lon, 30.0);
    expect(m.uncertKm, closeTo(50, 0.5)); // 500 km/h × 0.5 × 12 min
    expect(motionOf(t, seen + 30 * 60 * 1000).uncertKm, 80); // capped
  });

  group('typical route', () {
    // Полтава → Київ (the Reader's route for a drone seen in the north-east).
    const route = [[49.59, 34.55], [50.45, 30.52]];
    final seen = DateTime(2026, 10, 26, 3).millisecondsSinceEpoch;
    MapTarget drone({int? at, double lat = 49.59, double lon = 34.55}) => MapTarget(
        id: 'd', label: 'Шахед', cat: 'shahed', lat: lat, lon: lon, kmh: 185,
        seenMs: at ?? seen, route: [[lat, lon], route[1]]);

    tearDown(() => setMotionAlarmOblasts(null));

    test('flies the route for 20 min at 85 % speed, then stops', () {
      final m10 = motionOf(drone(), seen + 10 * 60000);
      expect(kmBetween(49.59, 34.55, m10.lat, m10.lon), closeTo(185 / 60 * 0.85 * 10, 1));
      expect(m10.uncertKm, 0);
      final m40 = motionOf(drone(), seen + 40 * 60000);
      expect(kmBetween(49.59, 34.55, m40.lat, m40.lon), closeTo(185 / 60 * 0.85 * 20, 1));
      expect(m40.uncertKm, greaterThan(0)); // stopped → ring grows
    });

    test('never flies out of the alarm zone', () {
      setMotionAlarmOblasts({'Poltava'}); // Київщина — відбій
      final m = motionOf(drone(), seen + 20 * 60000);
      expect(OblastLookupProbe.oblastAt(m.lat, m.lon), 'Poltava');
    });

    test('a new post moves it to the reported place and it flies on from there', () {
      final later = seen + 30 * 60000;
      final m = motionOf(drone(at: later, lat: 50.02, lon: 33.0), later); // «Шахед на Лубни»
      expect(m.lat, closeTo(50.02, 0.01));
      expect(m.lon, closeTo(33.0, 0.01));
    });

    test('a launch group (forecast) flies its whole route without the 20-min stop', () {
      final g = MapTarget(
          id: 'launch:L1', label: 'Пуск з Орла', cat: 'shahed', lat: 52.97, lon: 36.06,
          kmh: 185, seenMs: seen, forecast: true, route: const [[52.97, 36.06], [51.87, 33.48], [50.45, 30.52]]);
      final m = motionOf(g, seen + 90 * 60000);
      expect(kmBetween(52.97, 36.06, m.lat, m.lon), greaterThan(185 / 60 * 0.85 * 60)); // > an hour of flight
    });
  });
}

void alarmForecastTests() {
  group('alarm forecast', () {
    const teremky = MonitoredPlace(id: 'a', icon: '📍', name: 'Теремки', lat: 50.370, lon: 30.459);
    final now = DateTime(2026, 10, 26, 3).millisecondsSinceEpoch;
    MapTarget orel({int ago = 0}) => MapTarget(
        id: 'launch:L1', label: 'Пуск з Орел', cat: 'shahed', lat: 52.97, lon: 36.06, amount: 8,
        kmh: 185, seenMs: now - ago * 60000, forecast: true,
        route: const [[52.97, 36.06], [51.87, 33.48], [51.05, 31.89], [50.51, 30.79], [50.45, 30.52]]);

    tearDown(() => setMotionAlarmOblasts(null));

    test('a launch group from Орел → alarm in Kyiv in ~2.5 h', () {
      final f = forecastAlarm(teremky, [orel()], nowMs: now)!;
      expect(f.forecast, isTrue);
      expect(f.minutes, inInclusiveRange(130, 190));
      expect(f.via, contains('×8'));
    });

    test('an hour later the alarm is an hour closer', () {
      final a = forecastAlarm(teremky, [orel()], nowMs: now)!.minutes;
      final b = forecastAlarm(teremky, [orel(ago: 60)], nowMs: now)!.minutes;
      expect(a - b, closeTo(60, 3));
    });

    test('already an alarm in the place\'s oblast → «already»', () {
      setMotionAlarmOblasts({'Kiev City'});
      expect(forecastAlarm(teremky, [orel()], nowMs: now)!.already, isTrue);
    });

    test('nothing heading this way → no forecast', () {
      final west = MapTarget(id: 'w', label: 'Шахед', cat: 'shahed', lat: 46.97, lon: 32.0, kmh: 185, seenMs: now,
          route: const [[46.97, 32.0], [48.75, 30.22], [49.23, 28.47]]);
      expect(forecastAlarm(teremky, [west], nowMs: now), isNull);
    });
  });
}

/// Test-only access to the point-in-oblast lookup.
class OblastLookupProbe {
  static String? oblastAt(double lat, double lon) => OblastLookup.instance.nameAt(lat, lon);
}
