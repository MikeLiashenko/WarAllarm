import 'dart:convert';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../data/ukraine_oblasts.dart';
import '../domain/motion.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';
import 'glass.dart';

/// 3D threat map: our vector Ukraine on a tiltable, rotatable ground plane.
///
/// * oblasts under an official alarm rise as slabs (red high, partial low);
/// * every target flies at its own height with a shadow and a drop line —
///   drones low, cruise missiles a bit lower, КАБ glide down from altitude,
///   ballistic missiles follow an arc with an apogee between launch and target;
/// * launches without a known target grow a reach ring on the ground.
///
/// Drag ↔ rotates, drag ↕ tilts, pinch / wheel zooms, two-finger twist
/// rotates, double-tap resets. Motion comes from [motionOf] (same as 2D).
class Map3D extends StatefulWidget {
  const Map3D({
    super.key,
    required this.targets,
    required this.home,
    required this.homeName,
    this.oblastWhole = const {},
    this.oblastLevels = const {},
    this.dimTargets = false,
    this.insets = EdgeInsets.zero,
  });

  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final String homeName;

  /// Oblasts alarmed as a whole: polygon name → 'red' | 'yellow'.
  final Map<String, String> oblastWhole;

  /// Any official alarm in the oblast (partial too): polygon name → level.
  final Map<String, String> oblastLevels;
  final bool dimTargets;
  final EdgeInsets insets;

  @override
  State<Map3D> createState() => Map3DState();
}

class _Oblast3D {
  _Oblast3D(this.name, this.rings, this.centroid);
  final String name;
  final List<List<Offset>> rings; // (lon, lat)
  final Offset centroid;
}

class Map3DState extends State<Map3D> with SingleTickerProviderStateMixin {
  static List<_Oblast3D>? _oblasts;
  late final AnimationController _anim =
      AnimationController(vsync: this, duration: const Duration(seconds: 2))..repeat();

  double _yaw = -12; // degrees, 0 = north up
  double _tilt = 52; // degrees, 0 = straight down
  double _zoom = 1;

  double _startYaw = 0, _startTilt = 0, _startZoom = 1;
  Offset _startFocal = Offset.zero;

  @override
  void initState() {
    super.initState();
    _oblasts ??= _parse();
  }

  static List<_Oblast3D> _parse() {
    final data = jsonDecode(kOblastsJson) as List;
    return [
      for (final o in data)
        () {
          final rings = <List<Offset>>[
            for (final ring in (o['r'] as List))
              [
                for (final p in (ring as List))
                  Offset((p[0] as num).toDouble(), (p[1] as num).toDouble()),
              ],
          ];
          final outer = rings.first;
          var cx = 0.0, cy = 0.0;
          for (final p in outer) {
            cx += p.dx;
            cy += p.dy;
          }
          return _Oblast3D((o['n'] ?? '').toString(), rings,
              Offset(cx / outer.length, cy / outer.length));
        }(),
    ];
  }

  void zoomIn() => setState(() => _zoom = (_zoom * 1.3).clamp(0.6, 6.0));
  void zoomOut() => setState(() => _zoom = (_zoom / 1.3).clamp(0.6, 6.0));
  void resetView() => setState(() {
        _yaw = -12;
        _tilt = 52;
        _zoom = 1;
      });

  @override
  void dispose() {
    _anim.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Listener(
      onPointerSignal: (s) {
        if (s is PointerScrollEvent) {
          setState(() => _zoom =
              (_zoom * (s.scrollDelta.dy < 0 ? 1.12 : 1 / 1.12)).clamp(0.6, 6.0));
        }
      },
      child: GestureDetector(
        onDoubleTap: resetView,
        onScaleStart: (d) {
          _startYaw = _yaw;
          _startTilt = _tilt;
          _startZoom = _zoom;
          _startFocal = d.localFocalPoint;
        },
        onScaleUpdate: (d) {
          setState(() {
            final delta = d.localFocalPoint - _startFocal;
            if (d.pointerCount >= 2) {
              _zoom = (_startZoom * d.scale).clamp(0.6, 6.0);
              _yaw = _startYaw - d.rotation * 180 / math.pi;
            } else {
              _yaw = _startYaw - delta.dx * 0.35;
              _tilt = (_startTilt - delta.dy * 0.25).clamp(0.0, 72.0);
            }
          });
        },
        child: AnimatedBuilder(
          animation: _anim,
          builder: (context, _) => CustomPaint(
            size: Size.infinite,
            painter: _Map3DPainter(
              oblasts: _oblasts!,
              targets: widget.targets,
              home: widget.home,
              homeName: widget.homeName,
              oblastWhole: widget.oblastWhole,
              oblastLevels: widget.oblastLevels,
              yaw: _yaw,
              tilt: _tilt,
              zoom: _zoom,
              pulse: _anim.value,
              nowMs: DateTime.now().millisecondsSinceEpoch,
              dim: widget.dimTargets,
              insets: widget.insets,
            ),
          ),
        ),
      ),
    );
  }
}

// ── Painter ─────────────────────────────────────────────────────────────────

const double _lat0 = 48.6, _lon0 = 31.4; // camera focus: centre of Ukraine
final double _cosLat0 = math.cos(_lat0 * math.pi / 180);

class _P {
  const _P(this.o, this.depth);
  final Offset o;
  final double depth;
}

class _Map3DPainter extends CustomPainter {
  _Map3DPainter({
    required this.oblasts,
    required this.targets,
    required this.home,
    required this.homeName,
    required this.oblastWhole,
    required this.oblastLevels,
    required this.yaw,
    required this.tilt,
    required this.zoom,
    required this.pulse,
    required this.nowMs,
    required this.dim,
    required this.insets,
  });

  final List<_Oblast3D> oblasts;
  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final String homeName;
  final Map<String, String> oblastWhole;
  final Map<String, String> oblastLevels;
  final double yaw, tilt, zoom, pulse;
  final int nowMs;
  final bool dim;
  final EdgeInsets insets;

  late double _cy, _sy, _ct, _st, _d, _f, _cx0, _cy0;

  static const _bigCities = {
    'Київ', 'Харків', 'Одеса', 'Дніпро', 'Львів', 'Запоріжжя', 'Кривий Ріг', 'Миколаїв',
    'Вінниця', 'Херсон', 'Полтава', 'Чернігів', 'Черкаси', 'Суми', 'Житомир',
    'Хмельницький', 'Рівне', 'Луцьк', 'Тернопіль', 'Ужгород', 'Івано-Франківськ',
    'Чернівці', 'Кропивницький', 'Кропивницкий', 'Донецьк', 'Донецк', 'Луганськ', 'Луганск',
    'Сімферополь', 'Маріуполь',
  };
  static const _red = Color(0xFFC0303F);
  static const _yellow = Color(0xFFD9B21F);

  void _setup(Size size) {
    final yr = yaw * math.pi / 180, tr = tilt * math.pi / 180;
    _cy = math.cos(yr);
    _sy = math.sin(yr);
    _ct = math.cos(tr);
    _st = math.sin(tr);
    _d = 1800; // camera distance, km
    final w = size.width - insets.horizontal, h = size.height - insets.vertical;
    // Ukraine (~1300 km wide) fills ~92% of the narrower usable side at zoom 1.
    // Tilted view foreshortens the country — scale up so it still fills the view.
    _f = 0.82 * math.min(w, h * 1.6) * _d / 1300 * zoom * (1 + 0.25 * _st);
    _cx0 = insets.left + w / 2;
    _cy0 = insets.top + h * (0.5 + 0.06 * _st);
  }

  /// (lat, lon, height km) → screen point + depth (for painter's ordering).
  _P _proj(double lat, double lon, [double z = 0]) {
    final x = (lon - _lon0) * 111.32 * _cosLat0;
    final y = (lat - _lat0) * 110.57;
    final x1 = x * _cy - y * _sy;
    final y1 = x * _sy + y * _cy;
    final yc = y1 * _ct + z * _st;
    final depth = math.max(50.0, _d + y1 * _st - z * _ct);
    return _P(Offset(_cx0 + _f * x1 / depth, _cy0 - _f * yc / depth), depth);
  }

  Path _ring(List<Offset> ring, double z) {
    final p = Path();
    for (var i = 0; i < ring.length; i++) {
      final s = _proj(ring[i].dy, ring[i].dx, z).o;
      i == 0 ? p.moveTo(s.dx, s.dy) : p.lineTo(s.dx, s.dy);
    }
    return p..close();
  }

  @override
  void paint(Canvas canvas, Size size) {
    _setup(size);

    // Sky → horizon haze, so the tilted plane reads as 3D.
    canvas.drawRect(
        Offset.zero & size,
        Paint()
          ..shader = LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [
              Color.lerp(AppColors.mapSea, AppColors.primary, AppColors.isDark ? 0.10 : 0.06)!,
              AppColors.mapSea,
            ],
          ).createShader(Offset.zero & size));

    _grid(canvas);

    // Ground: every oblast, far first.
    final sorted = [...oblasts]
      ..sort((a, b) => _proj(b.centroid.dy, b.centroid.dx).depth
          .compareTo(_proj(a.centroid.dy, a.centroid.dx).depth));
    final land = Paint()..color = AppColors.mapLand;
    final edge = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 0.9
      ..strokeJoin = StrokeJoin.round
      ..color = AppColors.primary.withValues(alpha: 0.45);
    for (final ob in sorted) {
      for (final r in ob.rings) {
        final p = _ring(r, 0);
        canvas.drawPath(p, land);
        canvas.drawPath(p, edge);
      }
    }

    // Alarmed oblasts rise as slabs (far first; sides, then the lid).
    for (final ob in sorted) {
      final whole = oblastWhole[ob.name];
      final any = whole ?? oblastLevels[ob.name];
      if (any == null) continue;
      final red = any == 'red';
      final h = whole != null ? (red ? 26.0 : 14.0) : 7.0;
      final base = red ? _red : _yellow;
      for (final r in ob.rings) {
        _slab(canvas, r, h, base);
      }
    }

    _cities(canvas);
    _home(canvas);

    if (dim) {
      canvas.saveLayer(Offset.zero & size, Paint()..color = Colors.white.withValues(alpha: 0.4));
    }
    // Targets far → near so the closest badge is on top.
    final motions = [
      for (final t in targets) (t: t, m: motionOf(t, nowMs, frozen: dim)),
    ]..sort((a, b) => _proj(b.m.lat, b.m.lon).depth.compareTo(_proj(a.m.lat, a.m.lon).depth));
    for (final e in motions) {
      _target(canvas, e.t, e.m);
    }
    if (dim) canvas.restore();

    _compass(canvas, size);
  }

  void _grid(Canvas canvas) {
    final g = Paint()
      ..strokeWidth = 0.6
      ..color = AppColors.textMuted.withValues(alpha: 0.14);
    for (var lon = 20.0; lon <= 42; lon += 2) {
      final pts = [for (var lat = 43.0; lat <= 54; lat += 0.5) _proj(lat, lon).o];
      for (var i = 1; i < pts.length; i++) {
        canvas.drawLine(pts[i - 1], pts[i], g);
      }
    }
    for (var lat = 43.0; lat <= 54; lat += 1) {
      final pts = [for (var lon = 20.0; lon <= 42; lon += 1) _proj(lat, lon).o];
      for (var i = 1; i < pts.length; i++) {
        canvas.drawLine(pts[i - 1], pts[i], g);
      }
    }
  }

  void _slab(Canvas canvas, List<Offset> ring, double h, Color base) {
    final side = Paint()..color = Color.lerp(base, Colors.black, 0.35)!.withValues(alpha: 0.75);
    final sideEdge = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 0.5
      ..color = base.withValues(alpha: 0.5);
    // Side walls whose outer face looks at the camera.
    final n = ring.length;
    final faces = <(double, Path)>[];
    for (var i = 0; i < n - 1; i++) {
      final a = _proj(ring[i].dy, ring[i].dx), b = _proj(ring[i + 1].dy, ring[i + 1].dx);
      final at = _proj(ring[i].dy, ring[i].dx, h), bt = _proj(ring[i + 1].dy, ring[i + 1].dx, h);
      final cross = (b.o.dx - a.o.dx) * (at.o.dy - a.o.dy) - (b.o.dy - a.o.dy) * (at.o.dx - a.o.dx);
      if (cross > 0) continue; // back face
      faces.add(((a.depth + b.depth) / 2,
          Path()
            ..moveTo(a.o.dx, a.o.dy)
            ..lineTo(b.o.dx, b.o.dy)
            ..lineTo(bt.o.dx, bt.o.dy)
            ..lineTo(at.o.dx, at.o.dy)
            ..close()));
    }
    faces.sort((x, y) => y.$1.compareTo(x.$1));
    for (final f in faces) {
      canvas.drawPath(f.$2, side);
      canvas.drawPath(f.$2, sideEdge);
    }
    // Lid with a soft glow.
    final top = _ring(ring, h);
    canvas.drawPath(top, Paint()..color = base.withValues(alpha: 0.62));
    canvas.drawPath(
        top,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.3
          ..color = Color.lerp(base, Colors.white, 0.35)!);
  }

  void _cities(Canvas canvas) {
    for (final c in kCities) {
      // Oblast centres and big cities only (kCities tier 0 also holds city districts).
      final show = _bigCities.contains(c.name) || (zoom >= 2.2 && c.tier <= 1 && !c.name.contains('район'));
      if (!show) continue;
      final p = _proj(c.lat, c.lon).o;
      canvas.drawCircle(p, c.tier == 0 ? 2.4 : 1.7, Paint()..color = AppColors.textSecondary);
      _text(canvas, c.name, p + const Offset(0, -9), c.tier == 0 ? 10.5 : 9,
          AppColors.textPrimary.withValues(alpha: 0.85));
    }
  }

  void _home(Canvas canvas) {
    final g = _proj(home.lat, home.lon);
    final top = _proj(home.lat, home.lon, 18);
    final c = AppColors.primary;
    canvas.drawCircle(g.o, 6 + 10 * pulse,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.6
          ..color = c.withValues(alpha: 1 - pulse));
    canvas.drawLine(g.o, top.o, Paint()..color = c..strokeWidth = 2);
    canvas.drawCircle(top.o, 4.5, Paint()..color = c);
    canvas.drawCircle(top.o, 2, Paint()..color = Colors.white);
  }

  /// Display height (km, already exaggerated for readability) of a target.
  double _height(MapTarget t, TargetMotion m) {
    if (m.isFlight) {
      final p = m.progress!;
      if (t.cat == 'kab') return 55 * (1 - p); // glides down from the bomber
      final d = kmBetween(t.from![0], t.from![1], t.to![0], t.to![1]);
      return _apogee(d) * 4 * p * (1 - p); // ballistic arc
    }
    return switch (t.cat) {
      'shahed' => 12,
      'missile' => 9,
      'avia' => 30,
      'kab' => 40,
      'balist' => 20,
      _ => 10,
    };
  }

  double _apogee(double distKm) => math.min(240.0, 40 + distKm * 0.35);

  void _target(Canvas canvas, MapTarget t, TargetMotion m) {
    final color = kThreatColors[t.cat] ?? kThreatColors['other']!;
    final z = _height(t, m);
    final ground = _proj(m.lat, m.lon);
    final air = _proj(m.lat, m.lon, z);

    if (m.launch) {
      // Reach ring on the ground around the launch site.
      final pts = <Offset>[
        for (var a = 0; a <= 64; a++)
          () {
            final q = moveKm(m.lat, m.lon, a * 360 / 64, m.reachKm);
            return _proj(q.lat, q.lon).o;
          }(),
      ];
      final ring = Path()..addPolygon(pts, true);
      canvas.drawPath(ring, Paint()..color = color.withValues(alpha: 0.07));
      canvas.drawPath(
          ring,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.4
            ..color = color.withValues(alpha: 0.7));
      _text(canvas, 'пуск: ${t.fromName}', ground.o + const Offset(0, 16), 10.5, color, bold: true);
    }

    if (m.isFlight) {
      final from = t.from!, to = t.to!;
      final kab = t.cat == 'kab';
      final d = kmBetween(from[0], from[1], to[0], to[1]);
      final route = <Offset>[], shadow = <Offset>[];
      const steps = 40;
      for (var i = 0; i <= steps; i++) {
        final p = i / steps;
        final lat = from[0] + (to[0] - from[0]) * p;
        final lon = from[1] + (to[1] - from[1]) * p;
        final h = kab ? 55 * (1 - p) : _apogee(d) * 4 * p * (1 - p);
        route.add(_proj(lat, lon, h).o);
        shadow.add(_proj(lat, lon).o);
      }
      final flownIdx = (m.progress! * steps).round();
      // Ground track (shadow), the whole arc faint, the flown part bright.
      _polyline(canvas, shadow, Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1
        ..color = Colors.black.withValues(alpha: 0.35));
      _polyline(canvas, route, Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.2
        ..color = color.withValues(alpha: 0.35));
      _polyline(canvas, route.sublist(0, flownIdx + 1), Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2.4
        ..strokeCap = StrokeCap.round
        ..color = color);
      // Aim point on the ground; impact flash once it lands.
      final aim = _proj(to[0], to[1]).o;
      canvas.drawCircle(
          aim,
          m.landed ? 10 + 26 * pulse : 6 + 4 * math.sin(pulse * 2 * math.pi).abs(),
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.6
            ..color = (m.landed ? const Color(0xFFFF453A) : color)
                .withValues(alpha: m.landed ? 1 - pulse : 0.9));
      if (!m.landed && m.flightMin != null) {
        final left = (m.flightMin! * (1 - m.progress!)).clamp(0, 99);
        _text(canvas, left < 1 ? '<1 хв' : '${left.round()} хв', air.o + const Offset(18, -14),
            11, color, bold: true);
      }
    } else if (!m.launch && t.heading != null) {
      // Course ahead at flight height.
      final lead = math.min(60.0, speedKmPerMin(t) * 10);
      final q = moveKm(m.lat, m.lon, m.heading, lead);
      final ahead = _proj(q.lat, q.lon, z).o;
      _dashed(canvas, air.o, ahead, Paint()
        ..strokeWidth = 1.3
        ..color = color.withValues(alpha: 0.65));
    }

    // Shadow + drop line + badge in the air.
    canvas.drawOval(Rect.fromCenter(center: ground.o, width: 14, height: 14 * (1 - 0.6 * _st)),
        Paint()..color = Colors.black.withValues(alpha: 0.35));
    _dashed(canvas, ground.o, air.o, Paint()
      ..strokeWidth = 1
      ..color = color.withValues(alpha: 0.45));
    final r = (12.0 * (_d / air.depth)).clamp(8.0, 16.0);
    canvas.drawCircle(air.o, r + 3,
        Paint()
          ..color = color.withValues(alpha: 0.25)
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 5));
    canvas.drawCircle(air.o, r, Paint()..color = const Color(0xF0101419));
    canvas.drawCircle(air.o, r,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.8
          ..color = color);
    paintThreatGlyph(canvas, air.o, r * 1.7, t.cat, color, m.heading - yaw);
    if (t.amount > 1) {
      _text(canvas, '×${t.amount}', air.o + Offset(0, -r - 9), 12, AppColors.textPrimary, bold: true);
    }
  }

  void _compass(Canvas canvas, Size size) {
    final c = Offset(size.width - insets.right - 30, size.height - insets.bottom - 30);
    canvas.drawCircle(c, 18, Paint()..color = AppColors.glass);
    canvas.drawCircle(c, 18,
        Paint()
          ..style = PaintingStyle.stroke
          ..color = AppColors.glassBorder);
    final a = -yaw * math.pi / 180 - math.pi / 2;
    final tip = c + Offset.fromDirection(a, 12);
    final tail = c + Offset.fromDirection(a + math.pi, 8);
    canvas.drawLine(tail, tip, Paint()
      ..strokeWidth = 2.4
      ..strokeCap = StrokeCap.round
      ..color = AppColors.threat);
    _text(canvas, 'Пн', c + Offset.fromDirection(a, 26), 9.5, AppColors.textPrimary, bold: true);
  }

  void _polyline(Canvas canvas, List<Offset> pts, Paint paint) {
    if (pts.length < 2) return;
    canvas.drawPath(Path()..addPolygon(pts, false), paint);
  }

  void _dashed(Canvas canvas, Offset a, Offset b, Paint paint) {
    const dash = 4.0, gap = 4.0;
    final total = (b - a).distance;
    if (total == 0) return;
    final dir = (b - a) / total;
    for (var d = 0.0; d < total; d += dash + gap) {
      canvas.drawLine(a + dir * d, a + dir * math.min(d + dash, total), paint);
    }
  }

  void _text(Canvas canvas, String s, Offset at, double size, Color color, {bool bold = false}) {
    final b = ui.ParagraphBuilder(ui.ParagraphStyle(
        textAlign: TextAlign.center, fontSize: size, fontWeight: bold ? FontWeight.w700 : FontWeight.w600))
      ..pushStyle(ui.TextStyle(color: color, shadows: [
        Shadow(color: AppColors.mapSea, blurRadius: 3),
        Shadow(color: AppColors.mapSea, blurRadius: 2),
      ]))
      ..addText(s);
    final p = b.build()..layout(const ui.ParagraphConstraints(width: 220));
    canvas.drawParagraph(p, at - Offset(110, p.height / 2));
  }

  @override
  bool shouldRepaint(_Map3DPainter old) => true;
}
