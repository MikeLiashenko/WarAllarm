// Renders the 2D map zoomed on Kyiv with Kyiv oblast raions and the city under alarm
// to build/map2d_kyiv.png — checks that oblast and raion borders line up when close
// (the old 13-point Kyiv City left dark wedges):  flutter test tool/render_kyiv_test.dart
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/theme/app_colors.dart';
import 'package:waralarm/widgets/ukraine_map.dart';

void main() {
  testWidgets('render 2D map zoomed on Kyiv', (tester) async {
    AppColors.applyDark();
    final key = GlobalKey();
    final map = GlobalKey<UkraineMapState>();
    await tester.binding.setSurfaceSize(const Size(1100, 900));
    await tester.pumpWidget(MaterialApp(
      home: RepaintBoundary(
        key: key,
        child: ColoredBox(
          color: AppColors.mapSea,
          child: UkraineMap(
            key: map,
            targets: const [],
            home: (lat: 50.362, lon: 30.446),
            homeName: 'Київ',
            oblastWhole: const {'Kiev City': 'yellow'},
            raionLevels: const {
              'Бучанський район': 'yellow', 'Вишгородський район': 'yellow', 'Броварський район': 'yellow',
              'Бориспільський район': 'yellow', 'Обухівський район': 'yellow', 'Фастівський район': 'yellow',
            },
            showControls: false,
            showLegend: false,
          ),
        ),
      ),
    ));
    await tester.pump(const Duration(milliseconds: 100));
    map.currentState!.focusOn(50.45, 30.52, scale: 14);
    for (var i = 0; i < 3; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 400)));
      await tester.pump(const Duration(milliseconds: 100));
    }
    await tester.runAsync(() async {
      final bnd = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await bnd.toImage(pixelRatio: 1);
      final png = await img.toByteData(format: ui.ImageByteFormat.png);
      File('build/map2d_kyiv.png').writeAsBytesSync(png!.buffer.asUint8List());
    });
  });
}
