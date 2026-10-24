// Widget tests for WarAlarm building blocks.
//
// The full app opens live RTDB streams (SSE) which the test binding blocks, so
// we test the presentation widgets in isolation instead.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:waralarm/models/api_key.dart';
import 'package:waralarm/models/map_target.dart';
import 'package:waralarm/models/monitored_place.dart';
import 'package:waralarm/domain/motion.dart';
import 'package:waralarm/domain/situation.dart';
import 'package:waralarm/widgets/section_header.dart';

void main() {
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

  test('drone dead-reckons along its heading at weapon speed', () {
    final seen = DateTime(2026, 10, 24, 12).millisecondsSinceEpoch;
    final t = MapTarget(
        label: 'Шахед', cat: 'shahed', lat: 50.0, lon: 30.0,
        heading: 0, kmh: 180, seenMs: seen);
    final m = motionOf(t, seen + 10 * 60 * 1000); // 10 min → 30 km north
    expect(kmBetween(50.0, 30.0, m.lat, m.lon), closeTo(30, 1));
    expect(m.lat, greaterThan(50.0));
  });
}
