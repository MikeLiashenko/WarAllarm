// Renders a sample 3D scene (ballistic in flight, КАБ, launch ring, drones,
// raised alarmed oblasts) to build/map3d_preview.png — a visual check of the
// 3D map without live data:  flutter test tool/render_map3d_test.dart
import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/data/ukraine_raions.dart';
import 'package:waralarm/models/map_target.dart';
import 'package:waralarm/theme/app_colors.dart';
import 'package:waralarm/widgets/map3d.dart';

void main() {
  testWidgets('render 3D map preview', (tester) async {
    AppColors.applyDark();
    final now = DateTime.now().millisecondsSinceEpoch;
    final targets = [
      // Іскандер-М Бєлгород → Дніпро, ~40% of the way.
      MapTarget(label: 'Балістика (Іскандер-М)', cat: 'balist', lat: 48.47, lon: 35.04, kmh: 7000,
          from: const [50.60, 36.59], to: const [48.47, 35.04], t0: now - 55 * 1000),
      // КАБ to Куп'янськ, half way down.
      MapTarget(label: 'КАБ', cat: 'kab', lat: 49.71, lon: 37.62, kmh: 600,
          from: const [49.70, 37.90], to: const [49.71, 37.62], t0: now - 60 * 1000),
      // Launch from Брянськ, target unknown.
      MapTarget(label: 'Балістика', cat: 'balist', lat: 53.24, lon: 34.37, kmh: 4000,
          from: const [53.24, 34.37], t0: now - 90 * 1000, launch: true),
      // Drones / cruise missile.
      MapTarget(label: 'Шахед', cat: 'shahed', lat: 50.51, lon: 30.79, heading: 220, kmh: 500, amount: 3, seenMs: now),
      MapTarget(label: 'Шахед', cat: 'shahed', lat: 49.23, lon: 28.47, heading: 300, kmh: 185, seenMs: now),
      MapTarget(label: 'Ракета (Х-101)', cat: 'missile', lat: 48.9, lon: 32.0, heading: 290, kmh: 750, seenMs: now),
      // Launch group from Орел an hour ago (forecast) + a drone flying its typical route to Kyiv.
      MapTarget(id: 'launch:L1', label: 'Пуск з Орел (реакт.)', cat: 'shahed', lat: 52.97, lon: 36.06, amount: 8,
          kmh: 185, seenMs: now - 70 * 60000, forecast: true,
          route: const [[52.97, 36.06], [51.87, 33.48], [51.05, 31.89], [50.51, 30.79], [50.45, 30.52]]),
      MapTarget(id: 'd1', label: 'Шахед', cat: 'shahed', lat: 49.59, lon: 34.55, kmh: 185, seenMs: now - 8 * 60000,
          route: const [[49.59, 34.55], [50.45, 30.52]]),
    ];
    // The east under a mass alarm: every raion east of the Dnipro red, plus
    // whole-oblast alarms around them (raions inside those must not stack).
    final east = <String, String>{
      for (final o in jsonDecode(kRaionsJson) as List)
        if ((((o['r'] as List).first as List).first as List)[0] as num > 34.6 &&
            (((o['r'] as List).first as List).first as List)[1] as num < 50.4)
          (o['n'] as String): 'red',
    };
    final key = GlobalKey();
    final scene = ValueNotifier<bool>(false);
    final mapKey = GlobalKey<Map3DState>();
    await tester.binding.setSurfaceSize(const Size(1100, 760));
    await tester.pumpWidget(MaterialApp(
      home: RepaintBoundary(
        key: key,
        child: ColoredBox(
          color: AppColors.mapSea,
          child: ValueListenableBuilder<bool>(
            valueListenable: scene,
            builder: (context, mass, _) => Map3D(
              key: mapKey,
              targets: targets,
              home: (lat: 50.362, lon: 30.446),
              homeName: 'Київ',
              oblastWhole: mass
                  ? const {'Kharkiv': 'red', 'Donets\'k': 'red', 'Poltava': 'yellow'}
                  : const {'Kharkiv': 'red', 'Sumy': 'yellow', 'Donets\'k': 'red'},
              raionLevels: mass
                  ? east
                  : const {'Павлоградський район': 'red', 'Запорізький район': 'yellow', 'Броварський район': 'yellow'},
            ),
          ),
        ),
      ),
    ));
    await tester.pump(const Duration(milliseconds: 300));
    await tester.runAsync(() async {
      final bnd = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await bnd.toImage(pixelRatio: 1);
      final png = await img.toByteData(format: ui.ImageByteFormat.png);
      File('build/map3d_preview.png').writeAsBytesSync(png!.buffer.asUint8List());
    });
    // Close-up: camera over Kyiv at ×14 — raion borders, small places, labels.
    mapKey.currentState!.focusOn(50.45, 30.52, zoom: 14);
    await tester.pump(const Duration(milliseconds: 300));
    await tester.runAsync(() async {
      final bnd = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await bnd.toImage(pixelRatio: 1);
      final png = await img.toByteData(format: ui.ImageByteFormat.png);
      File('build/map3d_zoom.png').writeAsBytesSync(png!.buffer.asUint8List());
    });
    // Mass alarm over the east, seen around Харків.
    scene.value = true;
    mapKey.currentState!.focusOn(48.9, 36.4, zoom: 2.4);
    await tester.pump(const Duration(milliseconds: 300));
    await tester.runAsync(() async {
      final bnd = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await bnd.toImage(pixelRatio: 1);
      final png = await img.toByteData(format: ui.ImageByteFormat.png);
      File('build/map3d_mass.png').writeAsBytesSync(png!.buffer.asUint8List());
    });
  });
}
