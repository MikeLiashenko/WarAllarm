// Renders the WarAlarm icon (BrandIconPainter) into every platform asset:
//   assets/icon/waralarm_1024.png        — master PNG
//   windows/runner/resources/app_icon.ico — exe / window / taskbar icon
//   android/.../mipmap-*/ic_launcher.png  — launcher icons
//
// Run after changing the painter:  flutter test tool/render_icon_test.dart
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/widgets/brand_icon.dart';

Future<Uint8List> _png(int px) async {
  final rec = ui.PictureRecorder();
  final canvas = Canvas(rec);
  const BrandIconPainter().paint(canvas, Size(px.toDouble(), px.toDouble()));
  final img = await rec.endRecording().toImage(px, px);
  final data = await img.toByteData(format: ui.ImageByteFormat.png);
  return data!.buffer.asUint8List();
}

/// ICO container with PNG-compressed entries (supported since Vista).
Uint8List _ico(List<(int, Uint8List)> images) {
  final b = BytesBuilder();
  void u16(int v) => b.add([v & 0xFF, (v >> 8) & 0xFF]);
  void u32(int v) => b.add([v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF, (v >> 24) & 0xFF]);
  u16(0); // reserved
  u16(1); // type: icon
  u16(images.length);
  var offset = 6 + 16 * images.length;
  for (final (px, data) in images) {
    b.add([px >= 256 ? 0 : px, px >= 256 ? 0 : px, 0, 0]); // w, h, palette, reserved
    u16(1); // colour planes
    u16(32); // bits per pixel
    u32(data.length);
    u32(offset);
    offset += data.length;
  }
  for (final (_, data) in images) {
    b.add(data);
  }
  return b.toBytes();
}

void main() {
  testWidgets('render WarAlarm icons', (tester) async {
    await tester.runAsync(() async {
      Directory('assets/icon').createSync(recursive: true);
      File('assets/icon/waralarm_1024.png').writeAsBytesSync(await _png(1024));

      final sizes = [16, 24, 32, 48, 64, 128, 256];
      final ico = [for (final s in sizes) (s, await _png(s))];
      File('windows/runner/resources/app_icon.ico').writeAsBytesSync(_ico(ico));

      const mip = {'mdpi': 48, 'hdpi': 72, 'xhdpi': 96, 'xxhdpi': 144, 'xxxhdpi': 192};
      for (final e in mip.entries) {
        File('android/app/src/main/res/mipmap-${e.key}/ic_launcher.png')
            .writeAsBytesSync(await _png(e.value));
      }
    });
  });
}
