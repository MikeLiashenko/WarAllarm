// Widget tests for WarAlarm building blocks.
//
// The full app opens live RTDB streams (SSE) which the test binding blocks, so
// we test the presentation widgets in isolation instead.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:waralarm/models/api_key.dart';
import 'package:waralarm/models/map_target.dart';
import 'package:waralarm/models/monitored_place.dart';
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
}
