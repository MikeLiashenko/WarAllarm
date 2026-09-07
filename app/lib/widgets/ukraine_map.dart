import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../data/ukraine_path.dart';
import '../models/map_target.dart';
import '../utils/svg_path.dart';

/// Full custom-painted Ukraine threat map — Ukraine silhouette, tiered cities,
/// detailed rotated target silhouettes, AI course prediction, ×N clusters,
/// pulsing home marker, pan/zoom and dead-reckoning target movement.
///
/// A faithful Flutter port of the SVG map in `src/Reader/MapWriter.cs`.
class UkraineMap extends StatefulWidget {
  const UkraineMap({
    super.key,
    required this.targets,
    required this.home,
    required this.homeName,
  });

  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final String homeName;

  @override
  State<UkraineMap> createState() => _UkraineMapState();
}

class _UkraineMapState extends State<UkraineMap>
    with SingleTickerProviderStateMixin {
  late final Path _outline; // world-space Ukraine outline
  late final Rect _fb; // world-space bbox
  late final Map<String, List<_FigPathPart>> _figs;
  late final AnimationController _anim;

  double _scale = 1;
  double _tx = 0, _ty = 0;
  Size _size = Size.zero;
  bool _focused = false;

  // Gesture bookkeeping.
  double _startScale = 1;
  Offset _startT = Offset.zero;
  Offset _focalBase = Offset.zero;

  @override
  void initState() {
    super.initState();
    _outline = _buildOutline();
    _fb = _outline.getBounds();
    _figs = _buildFigs();
    _anim = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1600),
    )..repeat();
  }

  Path _buildOutline() {
    // Raw path → world coords via transform (x*0.1, 1024 - y*0.1).
    final raw = parseSvgPath(kUkrainePathRaw);
    final m = Matrix4.identity()
      ..translateByDouble(0.0, 1024.0, 0.0, 1.0)
      ..scaleByDouble(0.1, -0.1, 1.0, 1.0);
    return raw.transform(m.storage);
  }

  Map<String, List<_FigPathPart>> _buildFigs() {
    final cats = ['shahed', 'missile', 'balist', 'kab', 'avia', 'other'];
    final out = <String, List<_FigPathPart>>{};
    for (final c in cats) {
      out[c] = figParts(c)
          .map((p) => _FigPathPart(
                parseSvgPath(p.d),
                figPartIsDark(p)
                    ? const Color(0x800C101A)
                    : figPartIsLight(p)
                        ? const Color(0xB8FFFFFF)
                        : null,
                figPartIsBody(p),
              ))
          .toList();
    }
    return out;
  }

  @override
  void dispose() {
    _anim.dispose();
    super.dispose();
  }

  // --- Projection helpers (recomputed from current size) ----------------------

  _Fit _fit(Size size) {
    const pad = 12.0;
    final aW = size.width - 2 * pad, aH = size.height - 2 * pad;
    final s = math.min(aW / _fb.width, aH / _fb.height);
    final pw = _fb.width * s, ph = _fb.height * s;
    final oX = (size.width - pw) / 2, oY = (size.height - ph) / 2;
    return _Fit(s: s, pw: pw, ph: ph, oX: oX, oY: oY, fb: _fb);
  }

  void _autoFocus(Size size) {
    // No targets → show the whole country, centred (recognisable Ukraine),
    // instead of zooming onto an empty area around home.
    if (widget.targets.isEmpty) {
      _scale = 1;
      _tx = 0;
      _ty = 0;
      return;
    }
    final fit = _fit(size);
    final lons = <double>[widget.home.lon];
    final lats = <double>[widget.home.lat];
    for (final t in widget.targets) {
      lons.add(t.lon);
      lats.add(t.lat);
    }
    final cx = (lons.reduce(math.min) + lons.reduce(math.max)) / 2;
    final cy = (lats.reduce(math.min) + lats.reduce(math.max)) / 2;
    final lo = math.max(4.4, (lons.reduce(math.max) - lons.reduce(math.min)) * 1.5);
    final la = math.max(3.2, (lats.reduce(math.max) - lats.reduce(math.min)) * 1.5);
    final a = fit.project(cx - lo / 2, cy + la / 2);
    final b = fit.project(cx + lo / 2, cy - la / 2);
    final bw = (b.dx - a.dx).abs(), bh = (b.dy - a.dy).abs();
    _scale = math.max(1, math.min(3.4, math.min(size.width / bw, size.height / bh)));
    final mid = fit.project(cx, cy);
    _tx = size.width / 2 - mid.dx * _scale;
    _ty = size.height / 2 - mid.dy * _scale;
  }

  void _zoomAt(Offset center, double factor) {
    final ns = (_scale * factor).clamp(1.0, 8.0);
    final base = (center - Offset(_tx, _ty)) / _scale;
    setState(() {
      _scale = ns;
      _tx = center.dx - ns * base.dx;
      _ty = center.dy - ns * base.dy;
    });
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final size = Size(constraints.maxWidth, constraints.maxHeight);
        if (size != _size) {
          _size = size;
          if (!_focused && size.width > 0) {
            _autoFocus(size);
            _focused = true;
          }
        }
        return Stack(
          children: [
            GestureDetector(
              onScaleStart: (d) {
                _startScale = _scale;
                _startT = Offset(_tx, _ty);
                _focalBase = (d.localFocalPoint - _startT) / _startScale;
              },
              onScaleUpdate: (d) {
                setState(() {
                  _scale = (_startScale * d.scale).clamp(1.0, 8.0);
                  _tx = d.localFocalPoint.dx - _scale * _focalBase.dx;
                  _ty = d.localFocalPoint.dy - _scale * _focalBase.dy;
                });
              },
              child: AnimatedBuilder(
                animation: _anim,
                builder: (context, _) => CustomPaint(
                  size: Size.infinite,
                  painter: _MapPainter(
                    outline: _outline,
                    figs: _figs,
                    targets: widget.targets,
                    home: widget.home,
                    scale: _scale,
                    tx: _tx,
                    ty: _ty,
                    pulse: _anim.value,
                    nowMs: DateTime.now().millisecondsSinceEpoch,
                    textDir: Directionality.of(context),
                  ),
                ),
              ),
            ),
            Positioned(
              right: 12,
              top: 12,
              child: Column(
                children: [
                  _zoomButton(Icons.add, () =>
                      _zoomAt(_size.center(Offset.zero), 1.3)),
                  const SizedBox(height: 8),
                  _zoomButton(Icons.remove, () =>
                      _zoomAt(_size.center(Offset.zero), 1 / 1.3)),
                  const SizedBox(height: 8),
                  _zoomButton(Icons.center_focus_strong_rounded, () {
                    setState(() {
                      _focused = false;
                      _autoFocus(_size);
                      _focused = true;
                    });
                  }),
                ],
              ),
            ),
            const Positioned(left: 12, bottom: 12, child: _Legend()),
          ],
        );
      },
    );
  }

  Widget _zoomButton(IconData icon, VoidCallback onTap) {
    return Material(
      color: Colors.white,
      elevation: 2,
      shadowColor: const Color(0x22123A6B),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(9),
        side: const BorderSide(color: Color(0xFFE4ECF5)),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(9),
        onTap: onTap,
        child: SizedBox(
          width: 34,
          height: 34,
          child: Icon(icon, color: const Color(0xFF1E7BE0), size: 18),
        ),
      ),
    );
  }
}

class _FigPathPart {
  final Path path;
  final Color? overrideColor; // null → use category colour
  final bool body;
  _FigPathPart(this.path, this.overrideColor, this.body);
}

class _Fit {
  final double s, pw, ph, oX, oY;
  final Rect fb;
  _Fit({required this.s, required this.pw, required this.ph, required this.oX, required this.oY, required this.fb});

  /// lon/lat → base (pre-zoom) pixel space. Same geo bounds as the website.
  Offset project(double lon, double lat) => Offset(
        oX + ((lon - 22.14) / 18.09) * pw,
        oY + ((52.38 - lat) / 7.99) * ph,
      );
}

class _MapPainter extends CustomPainter {
  _MapPainter({
    required this.outline,
    required this.figs,
    required this.targets,
    required this.home,
    required this.scale,
    required this.tx,
    required this.ty,
    required this.pulse,
    required this.nowMs,
    required this.textDir,
  });

  final Path outline;
  final Map<String, List<_FigPathPart>> figs;
  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final double scale, tx, ty, pulse;
  final int nowMs;
  final TextDirection textDir;

  Offset _toScreen(Offset base) => Offset(tx + scale * base.dx, ty + scale * base.dy);

  static Offset _moveKm(double lon, double lat, double brgDeg, double dkm) {
    const rad = math.pi / 180;
    final la = lat * rad;
    return Offset(
      lon + (dkm * math.sin(brgDeg * rad)) / (111 * math.cos(la)),
      lat + (dkm * math.cos(brgDeg * rad)) / 111,
    );
  }

  MapCity? _nearestCity(double lon, double lat) {
    MapCity? best;
    double bd = 1e9;
    for (final c in kCities) {
      final dx = (c.lon - lon) * 71, dy = (c.lat - lat) * 111;
      final d = math.sqrt(dx * dx + dy * dy);
      if (d < bd) {
        bd = d;
        best = c;
      }
    }
    return bd <= 28 ? best : null;
  }

  @override
  void paint(Canvas canvas, Size size) {
    final fit = _fit(size);
    // Pixels-per-unit for the target silhouettes (fig coords are ~±10 units,
    // so a shahed ≈ 19 units × figScale). Fixed on-screen size, independent of
    // zoom; a touch larger on wide desktop windows.
    final ppu = (size.width / 340).clamp(2.6, 4.2);

    // --- Hot-zone glow under target clusters ---
    for (final t in targets) {
      final p = _toScreen(fit.project(t.lon, t.lat));
      final glow = Paint()
        ..color = const Color(0xFFFF3B30).withValues(alpha: 0.16)
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 22);
      canvas.drawCircle(p, 26 + 6.0 * t.amount, glow);
    }

    // --- Ukraine outline (world→screen affine) ---
    final a = scale * fit.s;
    final txO = tx + scale * fit.oX - a * fit.fb.left;
    final tyO = ty + scale * fit.oY - a * fit.fb.top;
    canvas.save();
    canvas.transform((Matrix4.identity()
          ..translateByDouble(txO, tyO, 0.0, 1.0)
          ..scaleByDouble(a, a, 1.0, 1.0))
        .storage);
    canvas.drawPath(
      outline,
      Paint()
        ..style = PaintingStyle.fill
        ..color = const Color(0xFFFFFFFF), // white land on light-blue sea
    );
    canvas.drawPath(
      outline,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.6 / a
        ..strokeJoin = StrokeJoin.round
        ..color = const Color(0xFF3B9EFF),
    );
    canvas.restore();

    // --- Cities (tiered by zoom) ---
    for (final c in kCities) {
      final show = c.tier == 0 ||
          (c.tier == 1 && scale >= 1.7) ||
          (c.tier == 2 && scale >= 2.8);
      if (!show) continue;
      final p = _toScreen(fit.project(c.lon, c.lat));
      final r = c.tier == 0 ? 2.4 : c.tier == 1 ? 1.9 : 1.4;
      canvas.drawCircle(p, r, Paint()..color = const Color(0xFF3B9EFF));
      canvas.drawCircle(p, r,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1
            ..color = const Color(0xFFFFFFFF));
      final fs = c.tier == 0 ? 10.5 : c.tier == 1 ? 9.0 : 7.5;
      _text(canvas, c.name, p + Offset(0, -(r + 3)),
          fs, const Color(0xE60F2035), center: true, stroke: true);
    }

    // --- Home marker (pulsing) ---
    final hp = _toScreen(fit.project(home.lon, home.lat));
    final ringR = 7 + 3 * math.sin(pulse * 2 * math.pi).abs();
    canvas.drawCircle(
        hp,
        ringR,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.6
          ..color = const Color(0xFF5AA9FF).withValues(alpha: 1 - pulse * 0.6));
    canvas.drawCircle(hp, 3.2, Paint()..color = const Color(0xFF5AA9FF));

    // --- Targets ---
    for (final t in targets) {
      final cat = figs.containsKey(t.cat) ? t.cat : 'other';
      final color = kThreatColors[t.cat] ?? kThreatColors['other']!;
      final known = t.heading != null;
      final spd = speedKmMin(t.cat, t.label);

      // Dead-reckoning: extrapolate along heading from last observed position.
      double lon = t.lon, lat = t.lat;
      if (known) {
        final em = math.max(0, (nowMs - (t.seenMs == 0 ? nowMs : t.seenMs)) / 60000);
        final d = math.min(60.0, spd * em);
        final m = _moveKm(t.lon, t.lat, t.heading!, d);
        lon = m.dx;
        lat = m.dy;
      }
      final scr = _toScreen(fit.project(lon, lat));

      // Trail.
      if (t.trail.length > 1) {
        final tp = Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.2
          ..color = color.withValues(alpha: 0.35);
        final path = Path();
        for (var i = 0; i < t.trail.length; i++) {
          final s = _toScreen(fit.project(t.trail[i][1], t.trail[i][0]));
          i == 0 ? path.moveTo(s.dx, s.dy) : path.lineTo(s.dx, s.dy);
        }
        canvas.drawPath(path, tp);
      }

      // AI course prediction: dashed line + ghost + predicted city.
      if (known && spd > 0) {
        final lead = math.min(55.0, spd * 10);
        final pd = _moveKm(lon, lat, t.heading!, lead);
        final ps = _toScreen(fit.project(pd.dx, pd.dy));
        _dashedLine(canvas, scr, ps,
            Paint()
              ..color = color.withValues(alpha: 0.55)
              ..strokeWidth = 1.3
              ..strokeCap = StrokeCap.round);
        _drawFig(canvas, ps, t.heading!, cat, color, ppu * 0.85, ghost: true);
        final nc = _nearestCity(pd.dx, pd.dy);
        if (nc != null) {
          _text(canvas, '→ ${nc.name}?', ps + const Offset(9, -9), 11,
              color.withValues(alpha: 0.95), stroke: true, bold: true);
        }
      }

      // Main silhouette.
      final figH = 19 * figScale(cat) * ppu; // approx on-screen height
      _drawFig(canvas, scr, t.heading ?? 0, cat, color, ppu);

      // ×N cluster label.
      if (t.amount > 1) {
        _text(canvas, '×${t.amount}', scr + Offset(figH * 0.42, -figH * 0.5),
            14, color, stroke: true, bold: true);
      }
      // Place label when zoomed in.
      if (scale >= 2.2 && t.place.isNotEmpty) {
        _text(canvas, t.place, scr + Offset(figH * 0.42, figH * 0.36), 10,
            color, stroke: true, bold: true);
      }
    }
  }

  _Fit _fit(Size size) {
    const pad = 12.0;
    final aW = size.width - 2 * pad, aH = size.height - 2 * pad;
    final bounds = outline.getBounds();
    final s = math.min(aW / bounds.width, aH / bounds.height);
    final pw = bounds.width * s, ph = bounds.height * s;
    return _Fit(
      s: s,
      pw: pw,
      ph: ph,
      oX: (size.width - pw) / 2,
      oY: (size.height - ph) / 2,
      fb: bounds,
    );
  }

  void _drawFig(Canvas canvas, Offset at, double headingDeg, String cat,
      Color color, double ppu,
      {bool ghost = false}) {
    canvas.save();
    canvas.translate(at.dx, at.dy);
    canvas.rotate(headingDeg * math.pi / 180);
    canvas.scale(figScale(cat) * ppu); // fig coords are ~±10 units → px
    final parts = figs[cat]!;
    for (final part in parts) {
      if (ghost && !part.body) continue;
      final paint = Paint()
        ..style = PaintingStyle.fill
        ..color = ghost
            ? color.withValues(alpha: 0.3)
            : (part.overrideColor ?? color);
      canvas.drawPath(part.path, paint);
      if (!ghost && part.body) {
        canvas.drawPath(
          part.path,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 0.35
            ..color = const Color(0x990A0F19),
        );
      }
    }
    canvas.restore();
  }

  void _dashedLine(Canvas canvas, Offset a, Offset b, Paint paint) {
    const dash = 5.0, gap = 4.0;
    final total = (b - a).distance;
    if (total == 0) return;
    final dir = (b - a) / total;
    double d = 0;
    while (d < total) {
      final start = a + dir * d;
      final end = a + dir * math.min(d + dash, total);
      canvas.drawLine(start, end, paint);
      d += dash + gap;
    }
  }

  void _text(Canvas canvas, String s, Offset at, double size, Color color,
      {bool center = false, bool stroke = false, bool bold = false}) {
    final builder = ui.ParagraphBuilder(ui.ParagraphStyle(
      textAlign: center ? TextAlign.center : TextAlign.left,
      fontSize: size,
      fontWeight: bold ? FontWeight.w700 : FontWeight.w600,
    ))
      ..pushStyle(ui.TextStyle(
        color: color,
        shadows: stroke
            ? const [
                Shadow(color: Color(0xFFFFFFFF), blurRadius: 3),
                Shadow(color: Color(0xFFFFFFFF), blurRadius: 2),
              ]
            : null,
      ))
      ..addText(s);
    final para = builder.build()
      ..layout(const ui.ParagraphConstraints(width: 200));
    final dx = center ? at.dx - 100 : at.dx;
    final dy = center ? at.dy - para.height : at.dy - para.height / 2;
    canvas.drawParagraph(para, Offset(dx, dy));
  }

  @override
  bool shouldRepaint(_MapPainter old) => true;
}

class _Legend extends StatelessWidget {
  const _Legend();

  @override
  Widget build(BuildContext context) {
    const items = [
      ('Шахед', Color(0xFFFF9F45)),
      ('Крилата', Color(0xFFFF7A3C)),
      ('Балістика', Color(0xFFFF3B30)),
      ('КАБ', Color(0xFFC77DFF)),
      ('Авіація', Color(0xFFF4C550)),
    ];
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: const Color(0xF2FFFFFF),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: const Color(0xFFE4ECF5)),
        boxShadow: const [
          BoxShadow(color: Color(0x14123A6B), blurRadius: 10, offset: Offset(0, 3)),
        ],
      ),
      child: Wrap(
        spacing: 12,
        runSpacing: 4,
        children: [
          for (final it in items)
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 9,
                  height: 9,
                  decoration: BoxDecoration(
                    color: it.$2,
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
                const SizedBox(width: 5),
                Text(it.$1,
                    style: const TextStyle(
                        color: Color(0xFF5B6B7F),
                        fontSize: 11,
                        fontWeight: FontWeight.w600)),
              ],
            ),
        ],
      ),
    );
  }
}
