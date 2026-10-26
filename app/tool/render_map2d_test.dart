// Renders a sample 2D map (shahed group, «бандероль», ballistic, КАБ, FPV,
// a picked target) to build/map2d_preview.png — a visual check of the 2D
// target icons without live data:  flutter test tool/render_map2d_test.dart
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/models/map_target.dart';
import 'package:waralarm/theme/app_colors.dart';
import 'package:waralarm/widgets/ukraine_map.dart';

void main() {
  testWidgets('render 2D map preview', (tester) async {
    AppColors.applyDark();
    final now = DateTime.now().millisecondsSinceEpoch;
    final targets = [
      MapTarget(id: 's', label: 'Шахед', cat: 'shahed', lat: 50.9, lon: 34.8, heading: 210, kmh: 185, amount: 3, seenMs: now),
      MapTarget(id: 'r', label: 'Шахед (реакт.)', cat: 'shahed', lat: 49.6, lon: 34.5, heading: 280, kmh: 500, seenMs: now,
          stage: 'Imminent'),
      MapTarget(id: 'b', label: 'Бандероль', cat: 'banderol', lat: 48.9, lon: 32.0, heading: 300, kmh: 800, seenMs: now),
      MapTarget(id: 'm', label: 'Ракета (Х-101)', cat: 'missile', lat: 47.5, lon: 31.0, heading: 320, kmh: 750, seenMs: now),
      MapTarget(id: 'k', label: 'КАБ', cat: 'kab', lat: 49.71, lon: 37.62, heading: 260, seenMs: now),
      MapTarget(id: 'a', label: 'Авіація', cat: 'avia', lat: 50.5, lon: 36.9, heading: 250, seenMs: now),
      MapTarget(id: 'f', label: 'ФПВ', cat: 'fpv', lat: 47.64, lon: 34.63, seenMs: now),
      MapTarget(id: 'p', label: 'Шахед', cat: 'shahed', lat: 50.45, lon: 31.5, heading: 260, kmh: 185, seenMs: now),
      // Aviation zones: KAB threat over north Харківщина, aviation only over Сумщина.
      MapTarget(id: 'avia:Kharkiv', label: 'Тактична авіація · загроза КАБ', cat: 'avia', lat: 50.08, lon: 36.48,
          place: 'Харківщина', seenMs: now, areaKm: 60),
      MapTarget(id: 'avia:Donets\'k', label: 'Загроза КАБ', cat: 'avia', lat: 48.73, lon: 37.56,
          place: 'Донеччина', seenMs: now, areaKm: 60),
    ];
    final key = GlobalKey();
    await tester.binding.setSurfaceSize(const Size(1100, 760));
    await tester.pumpWidget(MaterialApp(
      home: RepaintBoundary(
        key: key,
        child: ColoredBox(
          color: AppColors.mapSea,
          child: UkraineMap(
            targets: targets,
            home: (lat: 50.362, lon: 30.446),
            homeName: 'Київ',
            selectedId: 'p',
            showControls: false,
            showLegend: false,
          ),
        ),
      ),
    ));
    await tester.pump(const Duration(milliseconds: 300));
    await tester.runAsync(() async {
      final bnd = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await bnd.toImage(pixelRatio: 1.5);
      final png = await img.toByteData(format: ui.ImageByteFormat.png);
      File('build/map2d_preview.png').writeAsBytesSync(png!.buffer.asUint8List());
    });
  });
}
