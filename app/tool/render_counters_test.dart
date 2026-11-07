// Renders the map's counter chips (demo targets), with one kind picked, to
// build/counters_preview.png:  flutter test tool/render_counters_test.dart
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:waralarm/controllers/alerts_controller.dart';
import 'package:waralarm/controllers/map_data_controller.dart';
import 'package:waralarm/screens/now_screen.dart';
import 'package:waralarm/services/local_store.dart';
import 'package:waralarm/services/rtdb_service.dart';
import 'package:waralarm/theme/app_colors.dart';

void main() {
  testWidgets('render map counter chips', (tester) async {
    AppColors.applyDark();
    // ignore: invalid_use_of_visible_for_testing_member
    SharedPreferences.setMockInitialValues({});
    final store = await LocalStore.create();
    final data = MapDataController(RtdbService(), store)..setShowDemo(true);
    final alerts = AlertsController();
    final key = GlobalKey();
    String? picked = 'shahed';
    await tester.binding.setSurfaceSize(const Size(420, 260));
    await tester.pumpWidget(MaterialApp(
      home: RepaintBoundary(
        key: key,
        child: ColoredBox(
          color: AppColors.mapSea,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: StatefulBuilder(
              builder: (context, set) => Align(
                alignment: Alignment.topLeft,
                child: ThreatCounters(
                  data: data,
                  alerts: alerts,
                  focusKind: picked,
                  onKind: (k) => set(() => picked = k),
                ),
              ),
            ),
          ),
        ),
      ),
    ));
    await tester.pump(const Duration(milliseconds: 300));
    await tester.runAsync(() async {
      final bnd = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await bnd.toImage(pixelRatio: 2);
      final png = await img.toByteData(format: ui.ImageByteFormat.png);
      File('build/counters_preview.png').writeAsBytesSync(png!.buffer.asUint8List());
    });
    await tester.pumpWidget(const SizedBox());
    data.dispose();
    alerts.dispose();
    await tester.pump(const Duration(seconds: 30));
  });
}
