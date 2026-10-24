import 'dart:math' as math;

import 'package:flutter/material.dart';

/// The WarAlarm mark: a gold shield on a deep-night squircle with a radar
/// inside — range rings, a signal-yellow sweep and a red target blip.
///
/// One painter for everything: the in-app [BrandMark], and the launcher /
/// Windows icons rendered by `tool/render_icon_test.dart`.
class BrandIconPainter extends CustomPainter {
  const BrandIconPainter({this.background = true});

  /// Draw the squircle tile (false → shield only, e.g. for adaptive icons).
  final bool background;

  static const _gold1 = Color(0xFFFFE066);
  static const _gold2 = Color(0xFFF2A900);
  static const _blue = Color(0xFF3D8BFF);
  static const _red = Color(0xFFFF453A);

  @override
  void paint(Canvas canvas, Size size) {
    final s = size.shortestSide;
    final o = Offset((size.width - s) / 2, (size.height - s) / 2);
    canvas.save();
    canvas.translate(o.dx, o.dy);

    if (background) {
      final tile = RRect.fromRectAndRadius(
          Rect.fromLTWH(0, 0, s, s), Radius.circular(s * 0.23));
      canvas.drawRRect(
          tile,
          Paint()
            ..shader = const LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [Color(0xFF14233F), Color(0xFF070A12)],
            ).createShader(Rect.fromLTWH(0, 0, s, s)));
      // Soft blue glow behind the shield.
      canvas.drawCircle(
          Offset(s * 0.5, s * 0.48),
          s * 0.42,
          Paint()
            ..shader = RadialGradient(colors: [
              _blue.withValues(alpha: 0.28),
              _blue.withValues(alpha: 0),
            ]).createShader(Rect.fromCircle(
                center: Offset(s * 0.5, s * 0.48), radius: s * 0.42)));
    }

    // Shield outline (pointed bottom, gently curved top shoulders).
    final shield = _shield(s);
    canvas.drawPath(
        shield,
        Paint()
          ..shader = const LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [Color(0xFF0F1B33), Color(0xFF080C16)],
          ).createShader(shield.getBounds()));

    // Radar inside the shield.
    canvas.save();
    canvas.clipPath(shield);
    final c = Offset(s * 0.5, s * 0.5);
    final ringPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = s * 0.012
      ..color = _blue.withValues(alpha: 0.55);
    for (final r in [0.11, 0.21, 0.31]) {
      canvas.drawCircle(c, s * r, ringPaint);
    }
    // Sweep wedge (towards upper-right) fading out anticlockwise.
    const sweepEnd = -math.pi / 4; // pointing up-right
    const sweepLen = math.pi / 2.1;
    final rect = Rect.fromCircle(center: c, radius: s * 0.4);
    canvas.drawArc(
        rect,
        sweepEnd - sweepLen,
        sweepLen,
        true,
        Paint()
          ..shader = SweepGradient(
            endAngle: sweepLen,
            colors: [_gold1.withValues(alpha: 0), _gold1.withValues(alpha: 0.6)],
            // Angles are absolute — rotate the 0..sweepLen ramp into place.
            transform: const GradientRotation(sweepEnd - sweepLen),
          ).createShader(rect));
    canvas.drawLine(
        c,
        c + Offset.fromDirection(sweepEnd, s * 0.4),
        Paint()
          ..strokeWidth = s * 0.022
          ..strokeCap = StrokeCap.round
          ..color = _gold1);
    canvas.restore();

    // Red target blip on the sweep's edge + its halo.
    final blip = c + Offset.fromDirection(sweepEnd - 0.42, s * 0.2);
    canvas.drawCircle(blip, s * 0.075, Paint()..color = _red.withValues(alpha: 0.22));
    canvas.drawCircle(blip, s * 0.045, Paint()..color = _red);
    canvas.drawCircle(
        blip + Offset(-s * 0.012, -s * 0.012),
        s * 0.014,
        Paint()..color = Colors.white.withValues(alpha: 0.75));

    // Home point in the centre.
    canvas.drawCircle(c, s * 0.03, Paint()..color = Colors.white);

    // Gold shield rim on top.
    canvas.drawPath(
        shield,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = s * 0.045
          ..strokeJoin = StrokeJoin.round
          ..shader = const LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [_gold1, _gold2],
          ).createShader(shield.getBounds()));
    canvas.restore();
  }

  Path _shield(double s) {
    final top = s * 0.15, bottom = s * 0.88;
    final l = s * 0.2, r = s * 0.8;
    return Path()
      ..moveTo(s * 0.5, top)
      ..cubicTo(s * 0.62, top + s * 0.07, s * 0.71, top + s * 0.09, r, top + s * 0.09)
      ..lineTo(r, s * 0.47)
      ..cubicTo(r, s * 0.68, s * 0.66, s * 0.8, s * 0.5, bottom)
      ..cubicTo(s * 0.34, s * 0.8, l, s * 0.68, l, s * 0.47)
      ..lineTo(l, top + s * 0.09)
      ..cubicTo(s * 0.29, top + s * 0.09, s * 0.38, top + s * 0.07, s * 0.5, top)
      ..close();
  }

  @override
  bool shouldRepaint(BrandIconPainter old) => old.background != background;
}

/// The app mark as a widget (navigation rail, menu).
class BrandMark extends StatelessWidget {
  const BrandMark({super.key, this.size = 34});
  final double size;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(size * 0.23),
        boxShadow: [
          BoxShadow(
              color: const Color(0xFF3D8BFF).withValues(alpha: 0.25),
              blurRadius: 14,
              spreadRadius: -4),
        ],
      ),
      child: SizedBox(
        width: size,
        height: size,
        child: const CustomPaint(painter: BrandIconPainter()),
      ),
    );
  }
}
