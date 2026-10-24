import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';

/// A live radar centred on the selected place: range rings, a rotating sweep,
/// the alarm radius (dashed) and every target as a blip at its real bearing and
/// distance. Targets beyond the range sit on the rim as faint ticks, so you
/// still see where they come from. The range auto-zooms to the nearest threat.
class RadarScope extends StatefulWidget {
  const RadarScope({
    super.key,
    required this.lat,
    required this.lon,
    required this.targets,
    required this.tone,
    this.alarmRadiusKm,
    this.size = 220,
    this.active = false,
  });

  final double lat;
  final double lon;
  final List<MapTarget> targets;
  final Color tone;
  final double? alarmRadiusKm;
  final double size;
  final bool active;

  @override
  State<RadarScope> createState() => _RadarScopeState();
}

class _RadarScopeState extends State<RadarScope>
    with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
      vsync: this, duration: const Duration(milliseconds: 4200))
    ..repeat();

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  static const _steps = [25.0, 50.0, 100.0, 200.0, 400.0];

  List<_Blip> _blips() {
    final cosLat = math.cos(widget.lat * math.pi / 180);
    return [
      for (final t in widget.targets)
        () {
          final dx = (t.lon - widget.lon) * 111.32 * cosLat;
          final dy = (t.lat - widget.lat) * 110.57;
          return _Blip(t, dx, dy, math.sqrt(dx * dx + dy * dy));
        }(),
    ]..sort((a, b) => a.dist.compareTo(b.dist));
  }

  double _range(List<_Blip> blips) {
    final nearest = blips.isEmpty ? 0.0 : blips.first.dist;
    final want = math.max(
        math.max(nearest * 1.25, (widget.alarmRadiusKm ?? 0) * 1.15), 20.0);
    for (final s in _steps) {
      if (s >= want) return s;
    }
    return _steps.last;
  }

  @override
  Widget build(BuildContext context) {
    final blips = _blips();
    final range = _range(blips);
    return RepaintBoundary(
      child: SizedBox(
        width: widget.size,
        height: widget.size,
        child: AnimatedBuilder(
          animation: _c,
          builder: (_, _) => CustomPaint(
            painter: _RadarPainter(
              t: _c.value,
              blips: blips,
              rangeKm: range,
              alarmKm: widget.alarmRadiusKm,
              tone: widget.tone,
              active: widget.active,
              grid: AppColors.isDark
                  ? Colors.white.withValues(alpha: 0.07)
                  : Colors.black.withValues(alpha: 0.07),
              label: AppColors.textMuted,
              face: AppColors.surface2,
              threat: AppColors.threat,
              home: AppColors.textPrimary,
            ),
          ),
        ),
      ),
    );
  }
}

class _Blip {
  _Blip(this.t, this.dx, this.dy, this.dist);
  final MapTarget t;
  final double dx; // km east
  final double dy; // km north
  final double dist;
}

class _RadarPainter extends CustomPainter {
  _RadarPainter({
    required this.t,
    required this.blips,
    required this.rangeKm,
    required this.alarmKm,
    required this.tone,
    required this.active,
    required this.grid,
    required this.label,
    required this.face,
    required this.threat,
    required this.home,
  });

  final double t;
  final List<_Blip> blips;
  final double rangeKm;
  final double? alarmKm;
  final Color tone;
  final bool active;
  final Color grid;
  final Color label;
  final Color face;
  final Color threat;
  final Color home;

  @override
  void paint(Canvas canvas, Size size) {
    final c = Offset(size.width / 2, size.height / 2);
    final r = size.width / 2 - 14; // room for the compass letters
    final kmPx = r / rangeKm;

    // Face + glow.
    canvas.drawCircle(
        c,
        r + 2,
        Paint()
          ..shader = RadialGradient(colors: [
            tone.withValues(alpha: active ? 0.20 : 0.10),
            face.withValues(alpha: 0.0),
          ]).createShader(Rect.fromCircle(center: c, radius: r + 2)));

    // Range rings + crosshair.
    final gridPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = grid;
    for (var i = 1; i <= 4; i++) {
      canvas.drawCircle(c, r * i / 4, gridPaint);
    }
    canvas.drawCircle(
        c,
        r,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.4
          ..color = tone.withValues(alpha: 0.45));
    canvas.drawLine(c.translate(-r, 0), c.translate(r, 0), gridPaint);
    canvas.drawLine(c.translate(0, -r), c.translate(0, r), gridPaint);

    // Compass letters.
    _text(canvas, 'Пн', c.translate(0, -r - 8), label, 9.5, bold: true);
    _text(canvas, 'Пд', c.translate(0, r + 8), label, 9.5);
    _text(canvas, 'Сх', c.translate(r + 8, 0), label, 9.5);
    _text(canvas, 'Зх', c.translate(-r - 8, 0), label, 9.5);

    // Range label on the outer ring (lower-right).
    final lp = c + Offset.fromDirection(math.pi / 4, r * 0.75);
    _text(canvas, '${rangeKm.round()} км', lp, label, 9);

    // Alarm radius (dashed).
    if (alarmKm != null && alarmKm! > 0 && alarmKm! <= rangeKm) {
      _dashedCircle(canvas, c, alarmKm! * kmPx,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.3
            ..color = threat.withValues(alpha: 0.55));
    }

    // Sweep.
    final ang = t * 2 * math.pi - math.pi / 2;
    final sweepRect = Rect.fromCircle(center: c, radius: r);
    canvas.save();
    canvas.translate(c.dx, c.dy);
    canvas.rotate(ang);
    canvas.translate(-c.dx, -c.dy);
    canvas.drawArc(
        sweepRect,
        -math.pi / 2.6,
        math.pi / 2.6,
        true,
        Paint()
          ..shader = SweepGradient(
            startAngle: -math.pi / 2.6,
            endAngle: 0,
            colors: [tone.withValues(alpha: 0), tone.withValues(alpha: 0.22)],
          ).createShader(sweepRect));
    canvas.restore();
    canvas.drawLine(
        c,
        c + Offset.fromDirection(ang, r),
        Paint()
          ..strokeWidth = 1.6
          ..color = tone.withValues(alpha: 0.8));

    // Blips (far first so the nearest is drawn on top).
    for (final b in blips.reversed) {
      final color = kThreatColors[b.t.cat] ?? kThreatColors['other']!;
      final inside = b.dist <= rangeKm;
      if (!inside) {
        // Rim tick pointing at the off-scope target.
        final dir = math.atan2(-b.dy, b.dx);
        final p1 = c + Offset.fromDirection(dir, r - 4);
        final p2 = c + Offset.fromDirection(dir, r + 3);
        canvas.drawLine(
            p1,
            p2,
            Paint()
              ..strokeWidth = 2.4
              ..strokeCap = StrokeCap.round
              ..color = color.withValues(alpha: 0.55));
        continue;
      }
      final p = c + Offset(b.dx * kmPx, -b.dy * kmPx);

      // Brighten as the sweep passes over the blip.
      final bAng = math.atan2(p.dy - c.dy, p.dx - c.dx);
      var since = (ang - bAng) % (2 * math.pi);
      if (since < 0) since += 2 * math.pi;
      final fresh = 1 - (since / (2 * math.pi));
      final inAlarm = alarmKm != null && b.dist <= alarmKm!;

      if (inAlarm) {
        final pr = 7 + 9 * ((t * 3) % 1);
        canvas.drawCircle(
            p,
            pr,
            Paint()
              ..style = PaintingStyle.stroke
              ..strokeWidth = 1.5
              ..color = threat.withValues(alpha: 1 - ((t * 3) % 1)));
      }
      canvas.drawCircle(
          p, 8, Paint()..color = color.withValues(alpha: 0.12 + 0.25 * fresh));
      // Heading tail.
      final h = b.t.heading;
      if (h != null) {
        final hd = (h - 90) * math.pi / 180;
        canvas.drawLine(
            p,
            p + Offset.fromDirection(hd, 11),
            Paint()
              ..strokeWidth = 1.6
              ..strokeCap = StrokeCap.round
              ..color = color.withValues(alpha: 0.85));
      }
      canvas.drawCircle(p, 3.8, Paint()..color = color);
      if (b.t.amount > 1) {
        _text(canvas, '×${b.t.amount}', p.translate(0, -12), color, 9,
            bold: true);
      }
    }

    // Home.
    canvas.drawCircle(c, 5.5, Paint()..color = face);
    canvas.drawCircle(c, 3.5, Paint()..color = home);
  }

  void _dashedCircle(Canvas canvas, Offset c, double radius, Paint p) {
    const dashes = 48;
    for (var i = 0; i < dashes; i += 2) {
      final a0 = i * 2 * math.pi / dashes;
      canvas.drawArc(Rect.fromCircle(center: c, radius: radius), a0,
          2 * math.pi / dashes, false, p);
    }
  }

  void _text(Canvas canvas, String s, Offset at, Color color, double size,
      {bool bold = false}) {
    final tp = TextPainter(
      text: TextSpan(
          text: s,
          style: TextStyle(
              color: color,
              fontSize: size,
              fontWeight: bold ? FontWeight.w800 : FontWeight.w600)),
      textDirection: TextDirection.ltr,
    )..layout();
    tp.paint(canvas, at - Offset(tp.width / 2, tp.height / 2));
  }

  @override
  bool shouldRepaint(_RadarPainter old) => true;
}
