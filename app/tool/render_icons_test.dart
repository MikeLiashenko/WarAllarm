// Renders every threat silhouette large (as on the maps: glow + outline +
// gradient) to build/icons_preview.png:  flutter test tool/render_icons_test.dart
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/data/ukraine_geo.dart';
import 'package:waralarm/theme/app_colors.dart';
import 'package:waralarm/widgets/glass.dart';

class _Sheet extends CustomPainter {
  static const keys = ['shahed', 'shahed_jet', 'banderol', 'missile', 'balist', 'kab', 'avia', 'fpv', 'other'];
  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawRect(Offset.zero & size, Paint()..color = AppColors.mapLand);
    for (var i = 0; i < keys.length; i++) {
      final c = Offset(90.0 + (i % 5) * 170, 100.0 + (i ~/ 5) * 190);
      final col = kThreatColors[keys[i]] ?? kThreatColors['other']!;
      paintThreatIcon(canvas, c, 120, keys[i], col, 0);
    }
  }

  @override
  bool shouldRepaint(_Sheet old) => false;
}

void main() {
  testWidgets('render icon sheet', (tester) async {
    AppColors.applyDark();
    final key = GlobalKey();
    await tester.binding.setSurfaceSize(const Size(860, 400));
    await tester.pumpWidget(MaterialApp(
      home: RepaintBoundary(key: key, child: CustomPaint(size: const Size(860, 400), painter: _Sheet())),
    ));
    await tester.runAsync(() async {
      final bnd = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await bnd.toImage(pixelRatio: 1);
      final png = await img.toByteData(format: ui.ImageByteFormat.png);
      File('build/icons_preview.png').writeAsBytesSync(png!.buffer.asUint8List());
    });
  });
}
