import 'dart:convert';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../data/ukraine_geo.dart';
import '../data/ukraine_oblasts.dart';
import '../data/ukraine_raions.dart';
import '../domain/motion.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';
import 'glass.dart';

/// 3D threat map: our vector Ukraine on a tiltable, rotatable ground plane.
///
/// * the same alarm picture as the 2D map — whole oblasts under alarm rise as
///   slabs, partial alarms raise only the affected raions;
/// * targets fly at their own height with a ground shadow and a drop line —
///   drones low, cruise missiles lower, КАБ gliding down, ballistic on an arc;
/// * launches without a known target grow a reach ring on the ground;
/// * zooms down to village level (×40): raion borders and smaller places
///   appear as you get closer.
///
/// Controls (like a maps app): drag — move the map; wheel / pinch — zoom to
/// the cursor / fingers; right-drag or Ctrl-drag — rotate & tilt; two-finger
/// twist — rotate; double-tap / double-click — zoom in there; buttons for
/// rotate / tilt / north. Motion comes from [motionOf] (same as 2D).
class Map3D extends StatefulWidget {
  const Map3D({
    super.key,
    required this.targets,
    required this.home,
    required this.homeName,
    this.oblastWhole = const {},
    this.raionLevels = const {},
    this.dimTargets = false,
    this.insets = EdgeInsets.zero,
    this.selectedId,
    this.followId,
    this.onTargetTap,
    this.onUserMove,
  });

  /// Picked target (highlighted) and the one the camera follows (by id).
  final String? selectedId;
  final String? followId;

  /// Tap on the map: the target under the finger, or null for empty map.
  final ValueChanged<MapTarget?>? onTargetTap;

  /// The user moved the camera themselves (e.g. to stop following).
  final VoidCallback? onUserMove;

  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final String homeName;

  /// Oblasts alarmed as a whole: polygon name → 'red' | 'yellow'.
  final Map<String, String> oblastWhole;

  /// Raions under alarm (like the official map): «X район» → level.
  final Map<String, String> raionLevels;
  final bool dimTargets;
  final EdgeInsets insets;

  @override
  State<Map3D> createState() => Map3DState();
}

class _Poly {
  _Poly(this.name, this.rings) {
    var minLat = 90.0, maxLat = -90.0, minLon = 180.0, maxLon = -180.0, cx = 0.0, cy = 0.0;
    for (final p in rings.first) {
      minLat = math.min(minLat, p.dy);
      maxLat = math.max(maxLat, p.dy);
      minLon = math.min(minLon, p.dx);
      maxLon = math.max(maxLon, p.dx);
      cx += p.dx;
      cy += p.dy;
    }
    bbox = (minLat: minLat, maxLat: maxLat, minLon: minLon, maxLon: maxLon);
    centroid = Offset(cx / rings.first.length, cy / rings.first.length);
  }
  final String name;
  final List<List<Offset>> rings; // (lon, lat)
  late final ({double minLat, double maxLat, double minLon, double maxLon}) bbox;
  late final Offset centroid;
}

List<_Poly> _parsePolys(String json) {
  final data = jsonDecode(json) as List;
  return [
    for (final o in data)
      _Poly((o['n'] ?? '').toString(), [
        for (final ring in (o['r'] as List))
          [for (final p in (ring as List)) Offset((p[0] as num).toDouble(), (p[1] as num).toDouble())],
      ]),
  ];
}

double _apogee(double distKm) => math.min(240.0, 40 + distKm * 0.35);

/// Display height of a target (km, exaggerated), shrinking with zoom ([hs]).
double targetHeight3d(MapTarget t, TargetMotion m, double hs) {
  if (m.isFlight) {
    final p = m.progress!;
    if (t.cat == 'kab') return 55 * (1 - p) * hs;
    final d = kmBetween(t.from![0], t.from![1], t.to![0], t.to![1]);
    return _apogee(d) * 4 * p * (1 - p) * hs;
  }
  // The channel told us how high it flies (shahed ≈1 km ↔ 12 units) / it's going down.
  if (t.down) return 2 * hs;
  if (t.altM case final a?) return (12 * a / 1000).clamp(2.5, 45.0) * hs;
  final double base = switch (t.cat) {
    'shahed' => 12,
    'missile' => 9,
    'avia' => 30,
    'kab' => 40,
    'balist' => 20,
    _ => 10,
  };
  return base * hs;
}

String _napo(String s) => s.replaceAll('’', "'").replaceAll('ʼ', "'").replaceAll('`', "'");

/// The camera: focus point on the ground, rotation, tilt, zoom → projection
/// of (lat, lon, height) to the screen and back (screen → ground).
class _Cam {
  _Cam({
    required this.focusLat,
    required this.focusLon,
    required this.yaw,
    required this.tilt,
    required this.zoom,
    required Size size,
    required EdgeInsets insets,
  }) {
    final yr = yaw * math.pi / 180, tr = tilt * math.pi / 180;
    cy = math.cos(yr);
    sy = math.sin(yr);
    ct = math.cos(tr);
    st = math.sin(tr);
    final w = size.width - insets.horizontal, h = size.height - insets.vertical;
    f = 0.82 * math.min(w, h * 1.6) * d / 1300 * zoom * (1 + 0.25 * st);
    cx0 = insets.left + w / 2;
    cy0 = insets.top + h * (0.5 + 0.06 * st);
  }

  final double focusLat, focusLon, yaw, tilt, zoom;
  static const double d = 1800; // camera distance, km
  static final double cosLat = math.cos(48.6 * math.pi / 180);
  late final double cy, sy, ct, st, f, cx0, cy0;

  /// Heights shrink as you zoom in, so slabs and badges don't tower over a town.
  double get hScale => 1 / math.pow(math.max(1, zoom), 0.55);

  ({Offset o, double depth}) proj(double lat, double lon, [double z = 0]) {
    final x = (lon - focusLon) * 111.32 * cosLat;
    final y = (lat - focusLat) * 110.57;
    final x1 = x * cy - y * sy;
    final y1 = x * sy + y * cy;
    final yc = y1 * ct + z * st;
    final depth = math.max(30.0, d + y1 * st - z * ct);
    return (o: Offset(cx0 + f * x1 / depth, cy0 - f * yc / depth), depth: depth);
  }

  /// Screen point → ground (lat, lon), or null when it points above the horizon.
  ({double lat, double lon})? ground(Offset s) {
    final v = cy0 - s.dy; // = f * y1 * ct / depth, depth = d + y1 * st
    final den = f * ct - v * st;
    if (den <= 1e-6) return null;
    final y1 = v * d / den;
    final depth = d + y1 * st;
    if (depth <= 30) return null;
    final x1 = (s.dx - cx0) * depth / f;
    final x = x1 * cy + y1 * sy; // inverse rotation
    final y = -x1 * sy + y1 * cy;
    return (lat: focusLat + y / 110.57, lon: focusLon + x / (111.32 * cosLat));
  }
}

class Map3DState extends State<Map3D> with SingleTickerProviderStateMixin {
  static List<_Poly>? _oblasts;
  static List<_Poly>? _raions;
  late final AnimationController _anim =
      AnimationController(vsync: this, duration: const Duration(seconds: 2))..repeat();

  static const double _homeLat = 48.6, _homeLon = 31.4;
  double _focusLat = _homeLat, _focusLon = _homeLon;
  double _yaw = -8; // degrees, 0 = north up
  double _tilt = 50; // degrees, 0 = straight down
  double _zoom = 1;
  static const double _minZoom = 0.7, _maxZoom = 40;

  Size _size = Size.zero;
  bool _rotateMode = false; // right mouse / Ctrl held
  Offset _lastFocal = Offset.zero, _lastPan = Offset.zero;
  double _lastScale = 1, _lastRotation = 0;
  Offset _lastTap = Offset.zero;

  @override
  void initState() {
    super.initState();
    _oblasts ??= _parsePolys(kOblastsJson);
    _raions ??= _parsePolys(kRaionsJson);
  }

  @override
  void dispose() {
    _anim.dispose();
    super.dispose();
  }

  _Cam _cam() => _Cam(
        focusLat: _focusLat,
        focusLon: _focusLon,
        yaw: _yaw,
        tilt: _tilt,
        zoom: _zoom,
        size: _size,
        insets: widget.insets,
      );

  /// Zoom by [factor] keeping the ground point under [at] fixed on screen.
  void _zoomAt(Offset at, double factor) {
    final before = _cam().ground(at);
    _zoom = (_zoom * factor).clamp(_minZoom, _maxZoom);
    final after = _cam().ground(at);
    if (before != null && after != null) {
      _focusLat += before.lat - after.lat;
      _focusLon += before.lon - after.lon;
    }
    _clampFocus();
  }

  /// Move the map so the ground under [from] ends up under [to].
  void _panScreen(Offset from, Offset to) {
    final cam = _cam();
    final a = cam.ground(from), b = cam.ground(to);
    if (a == null || b == null) return;
    _focusLat += a.lat - b.lat;
    _focusLon += a.lon - b.lon;
    _clampFocus();
  }

  void _clampFocus() {
    _focusLat = _focusLat.clamp(43.5, 53.5);
    _focusLon = _focusLon.clamp(21.0, 41.5);
  }

  Offset get _center => Offset(
      widget.insets.left + (_size.width - widget.insets.horizontal) / 2,
      widget.insets.top + (_size.height - widget.insets.vertical) / 2);

  // ── public controls (map screen buttons) ──
  void zoomIn() => setState(() => _zoomAt(_center, 1.6));
  void zoomOut() => setState(() => _zoomAt(_center, 1 / 1.6));
  void resetView() => setState(() {
        _focusLat = _homeLat;
        _focusLon = _homeLon;
        _yaw = -8;
        _tilt = 50;
        _zoom = 1;
      });
  void rotate(double deg) => setState(() => _yaw = (_yaw + deg) % 360);
  void tiltBy(double deg) => setState(() => _tilt = (_tilt + deg).clamp(0.0, 75.0));
  void northUp() => setState(() => _yaw = 0);

  /// Fly the camera to a place (e.g. tapping the place card).
  void focusOn(double lat, double lon, {double zoom = 6}) => setState(() {
        _focusLat = lat;
        _focusLon = lon;
        _zoom = zoom.clamp(_minZoom, _maxZoom);
        _clampFocus();
      });

  // ── Picking & following ──
  String? _followStarted;

  /// The target under [local] (nearest badge within ~30 px), or null.
  MapTarget? targetAt(Offset local) {
    final cam = _cam();
    final now = DateTime.now().millisecondsSinceEpoch;
    MapTarget? best;
    var bd = 30.0;
    for (final t in widget.targets) {
      final m = motionOf(t, now, frozen: widget.dimTargets);
      final o = cam.proj(m.lat, m.lon, targetHeight3d(t, m, cam.hScale)).o;
      final d = (o - local).distance;
      if (d < bd) {
        bd = d;
        best = t;
      }
    }
    return best;
  }

  /// Every frame: keep the followed target at the focus (camera rides along).
  void _followTick() {
    final id = widget.followId;
    if (id == null) {
      _followStarted = null;
      return;
    }
    final t = widget.targets.where((x) => x.id == id).firstOrNull;
    if (t == null) return;
    if (_followStarted != id) {
      _followStarted = id;
      if (_zoom < 6) _zoom = 6; // come closer once when following starts
    }
    final m = motionOf(t, DateTime.now().millisecondsSinceEpoch, frozen: widget.dimTargets);
    _focusLat = m.lat;
    _focusLon = m.lon;
    _clampFocus();
  }

  void _userMoved() {
    if (widget.followId != null) widget.onUserMove?.call();
  }

  bool get _ctrl => HardwareKeyboard.instance.isControlPressed || HardwareKeyboard.instance.isShiftPressed;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(builder: (context, c) {
      _size = Size(c.maxWidth, c.maxHeight);
      return Stack(
        children: [
          Positioned.fill(
            child: Listener(
              onPointerDown: (e) => _rotateMode = (e.buttons & kSecondaryMouseButton) != 0 || _ctrl,
              onPointerSignal: (s) {
                if (s is PointerScrollEvent) {
                  // Wheel: zoom to the cursor (bigger steps for a real wheel notch).
                  final dy = s.scrollDelta.dy;
                  if (dy == 0) return;
                  final f = math.pow(1.0035, -dy.clamp(-240.0, 240.0)).toDouble();
                  setState(() => _zoomAt(s.localPosition, f));
                } else if (s is PointerScaleEvent) {
                  setState(() => _zoomAt(s.localPosition, s.scale));
                }
              },
              // Trackpad: two fingers move the map, pinch zooms at the fingers.
              onPointerPanZoomStart: (e) {
                _lastPan = Offset.zero;
                _lastScale = 1;
              },
              onPointerPanZoomUpdate: (e) => setState(() {
                final pos = e.localPosition;
                if ((e.localPan - _lastPan).distance > 2) _userMoved();
                _panScreen(pos, pos + (e.localPan - _lastPan));
                _zoomAt(pos, e.scale / _lastScale);
                _yaw = (_yaw - (e.rotation - _lastRotation) * 180 / math.pi) % 360;
                _lastPan = e.localPan;
                _lastScale = e.scale;
                _lastRotation = e.rotation;
              }),
              onPointerPanZoomEnd: (_) => _lastRotation = 0,
              child: GestureDetector(
                // Touch & mouse only — trackpad gestures are handled above, otherwise a
                // two-finger scroll was read as a drag and the map drifted away.
                supportedDevices: const {
                  PointerDeviceKind.touch,
                  PointerDeviceKind.mouse,
                  PointerDeviceKind.stylus,
                  PointerDeviceKind.invertedStylus,
                },
                trackpadScrollCausesScale: false,
                // Count the drag from the touch-down point, not after the slop — the
                // first pixels of a pan used to be lost.
                dragStartBehavior: DragStartBehavior.down,
                onTapUp: (d) => widget.onTargetTap?.call(targetAt(d.localPosition)),
                onDoubleTapDown: (d) => _lastTap = d.localPosition,
                onDoubleTap: () => setState(() => _zoomAt(_lastTap, 2)),
                onScaleStart: (d) {
                  _lastFocal = d.localFocalPoint;
                  _lastScale = 1;
                  _lastRotation = 0;
                },
                onScaleUpdate: (d) {
                  setState(() {
                    final delta = d.localFocalPoint - _lastFocal;
                    if (d.pointerCount >= 2) {
                      // Pinch zoom at the fingers, twist rotates, both move pans.
                      _zoomAt(d.localFocalPoint, d.scale / _lastScale);
                      _yaw = (_yaw - (d.rotation - _lastRotation) * 180 / math.pi) % 360;
                      _panScreen(_lastFocal, d.localFocalPoint);
                    } else if (_rotateMode) {
                      _yaw = (_yaw - delta.dx * 0.35) % 360;
                      _tilt = (_tilt - delta.dy * 0.3).clamp(0.0, 75.0);
                    } else {
                      if (delta.distance > 2) _userMoved(); // dragging the map stops following
                      _panScreen(_lastFocal, d.localFocalPoint);
                    }
                    _lastFocal = d.localFocalPoint;
                    _lastScale = d.scale;
                    _lastRotation = d.rotation;
                  });
                },
                onScaleEnd: (_) => _rotateMode = false,
                child: AnimatedBuilder(
                  animation: _anim,
                  builder: (context, _) {
                    _followTick();
                    return CustomPaint(
                    size: Size.infinite,
                    painter: _Map3DPainter(
                      cam: _cam(),
                      oblasts: _oblasts!,
                      raions: _raions!,
                      targets: widget.targets,
                      home: widget.home,
                      oblastWhole: widget.oblastWhole,
                      raionLevels: {for (final e in widget.raionLevels.entries) _napo(e.key): e.value},
                      pulse: _anim.value,
                      nowMs: DateTime.now().millisecondsSinceEpoch,
                      dim: widget.dimTargets,
                      selectedId: widget.selectedId,
                    ),
                  );
                  },
                ),
              ),
            ),
          ),
          // Rotate / tilt / north — bottom-right, above the place card.
          Positioned(
            right: widget.insets.right + 12,
            bottom: widget.insets.bottom + 8,
            child: _Controls3D(
              yaw: _yaw,
              onRotate: rotate,
              onTilt: tiltBy,
              onNorth: northUp,
            ),
          ),
        ],
      );
    });
  }
}

class _Controls3D extends StatelessWidget {
  const _Controls3D({
    required this.yaw,
    required this.onRotate,
    required this.onTilt,
    required this.onNorth,
  });
  final double yaw;
  final ValueChanged<double> onRotate, onTilt;
  final VoidCallback onNorth;

  @override
  Widget build(BuildContext context) {
    Widget b(IconData i, String tip, VoidCallback f) => Tooltip(
          message: tip,
          child: InkResponse(
            onTap: f,
            radius: 20,
            child: SizedBox(width: 38, height: 38, child: Icon(i, size: 20, color: AppColors.textPrimary)),
          ),
        );
    return Glass(
      radius: 20,
      padding: const EdgeInsets.all(4),
      child: Material(
        type: MaterialType.transparency,
        child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          // Compass: tap → north up.
          Tooltip(
            message: 'Північ угорі',
            child: InkResponse(
              onTap: onNorth,
              radius: 20,
              child: SizedBox(
                width: 38,
                height: 38,
                child: Transform.rotate(
                  angle: -yaw * math.pi / 180,
                  child: CustomPaint(painter: _NeedlePainter()),
                ),
              ),
            ),
          ),
          b(Icons.rotate_left_rounded, 'Повернути ліворуч', () => onRotate(-20)),
          b(Icons.rotate_right_rounded, 'Повернути праворуч', () => onRotate(20)),
          b(Icons.keyboard_double_arrow_up_rounded, 'Нахилити (більше 3D)', () => onTilt(10)),
          b(Icons.keyboard_double_arrow_down_rounded, 'Вирівняти (згори)', () => onTilt(-10)),
        ],
        ),
      ),
    );
  }
}

class _NeedlePainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final c = size.center(Offset.zero);
    final n = Path()
      ..moveTo(c.dx, c.dy - 13)
      ..lineTo(c.dx + 5, c.dy)
      ..lineTo(c.dx - 5, c.dy)
      ..close();
    final s = Path()
      ..moveTo(c.dx, c.dy + 13)
      ..lineTo(c.dx + 5, c.dy)
      ..lineTo(c.dx - 5, c.dy)
      ..close();
    canvas.drawPath(n, Paint()..color = AppColors.threat);
    canvas.drawPath(s, Paint()..color = AppColors.textMuted);
  }

  @override
  bool shouldRepaint(_NeedlePainter old) => false;
}

// ── Painter ─────────────────────────────────────────────────────────────────

class _Map3DPainter extends CustomPainter {
  _Map3DPainter({
    required this.cam,
    required this.oblasts,
    required this.raions,
    required this.targets,
    required this.home,
    required this.oblastWhole,
    required this.raionLevels,
    required this.pulse,
    required this.nowMs,
    required this.dim,
    this.selectedId,
  });

  final String? selectedId;
  final _Cam cam;
  final List<_Poly> oblasts, raions;
  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final Map<String, String> oblastWhole;
  final Map<String, String> raionLevels; // apostrophes normalised
  final double pulse;
  final int nowMs;
  final bool dim;

  static const _red = Color(0xFFC0303F);
  static const _yellow = Color(0xFFD9B21F);
  late Rect _view;

  /// Roughly on screen (bbox corners projected) — skips far-away polygons when zoomed.
  bool _visible(_Poly p) {
    if (cam.zoom < 2) return true;
    final b = p.bbox;
    final a1 = cam.proj(b.minLat, b.minLon).o, a2 = cam.proj(b.minLat, b.maxLon).o;
    final a3 = cam.proj(b.maxLat, b.minLon).o, a4 = cam.proj(b.maxLat, b.maxLon).o;
    final r = Rect.fromPoints(a1, a2).expandToInclude(Rect.fromPoints(a3, a4));
    return r.overlaps(_view);
  }

  Path _ring(List<Offset> ring, double z) {
    final p = Path();
    for (var i = 0; i < ring.length; i++) {
      final s = cam.proj(ring[i].dy, ring[i].dx, z).o;
      i == 0 ? p.moveTo(s.dx, s.dy) : p.lineTo(s.dx, s.dy);
    }
    return p..close();
  }

  double _depthOf(_Poly p) => cam.proj(p.centroid.dy, p.centroid.dx).depth;

  @override
  void paint(Canvas canvas, Size size) {
    _view = (Offset.zero & size).inflate(80);

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
    final obs = oblasts.where(_visible).toList()..sort((a, b) => _depthOf(b).compareTo(_depthOf(a)));
    final land = Paint()..color = AppColors.mapLand;
    final edge = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.0
      ..strokeJoin = StrokeJoin.round
      ..color = AppColors.primary.withValues(alpha: 0.5);
    for (final ob in obs) {
      for (final r in ob.rings) {
        canvas.drawPath(_ring(r, 0), land);
      }
    }
    // Raion borders once you're close enough to care.
    if (cam.zoom >= 2.2) {
      final rb = Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 0.6
        ..color = AppColors.textMuted.withValues(alpha: 0.25);
      for (final r in raions) {
        if (!_visible(r)) continue;
        for (final ring in r.rings) {
          canvas.drawPath(_ring(ring, 0), rb);
        }
      }
    }
    for (final ob in obs) {
      for (final r in ob.rings) {
        canvas.drawPath(_ring(r, 0), edge);
      }
    }

    // Alarm slabs — same picture as the 2D map: whole oblasts, else the raions
    // (never both: a raion inside a whole-oblast alarm would stack a second slab).
    final slabs = <(List<List<Offset>>, Color)>[];
    final wholeObs = <_Poly>[];
    for (final ob in obs) {
      final whole = oblastWhole[ob.name];
      if (whole == null) continue;
      wholeObs.add(ob);
      slabs.add((ob.rings, whole == 'red' ? _red : _yellow));
    }
    for (final r in raions) {
      final lvl = raionLevels[_napo(r.name)];
      if (lvl == null || !_visible(r)) continue;
      if (wholeObs.any((ob) => _inside(ob, r.centroid))) continue;
      slabs.add((r.rings, lvl == 'red' ? _red : _yellow));
    }
    _slabs(canvas, slabs, 9.0 * cam.hScale);

    _cities(canvas, size);
    _home(canvas);

    if (dim) {
      canvas.saveLayer(Offset.zero & size, Paint()..color = Colors.white.withValues(alpha: 0.4));
    }
    final motions = [
      for (final t in targets) (t: t, m: motionOf(t, nowMs, frozen: dim)),
    ]..sort((a, b) => cam.proj(b.m.lat, b.m.lon).depth.compareTo(cam.proj(a.m.lat, a.m.lon).depth));
    for (final e in motions) {
      _target(canvas, e.t, e.m);
    }
    if (dim) canvas.restore();
  }

  void _grid(Canvas canvas) {
    final g = Paint()
      ..strokeWidth = 0.6
      ..color = AppColors.textMuted.withValues(alpha: 0.12);
    // Finer grid when zoomed in; only around the focus.
    final step = cam.zoom >= 8 ? 0.25 : cam.zoom >= 3 ? 0.5 : 1.0;
    final latA = (cam.focusLat - 30 / cam.zoom).clamp(42.0, 55.0), latB = (cam.focusLat + 30 / cam.zoom).clamp(42.0, 55.0);
    final lonA = (cam.focusLon - 40 / cam.zoom).clamp(19.0, 43.0), lonB = (cam.focusLon + 40 / cam.zoom).clamp(19.0, 43.0);
    for (var lon = (lonA / step).floor() * step; lon <= lonB; lon += step) {
      Offset? prev;
      for (var lat = latA; lat <= latB; lat += step / 4) {
        final p = cam.proj(lat, lon).o;
        if (prev != null) canvas.drawLine(prev, p, g);
        prev = p;
      }
    }
    for (var lat = (latA / step).floor() * step; lat <= latB; lat += step) {
      Offset? prev;
      for (var lon = lonA; lon <= lonB; lon += step / 4) {
        final p = cam.proj(lat, lon).o;
        if (prev != null) canvas.drawLine(prev, p, g);
        prev = p;
      }
    }
  }

  /// Point-in-polygon (lon/lat) against a polygon's outer rings.
  static bool _inside(_Poly poly, Offset pt) {
    final b = poly.bbox;
    if (pt.dy < b.minLat || pt.dy > b.maxLat || pt.dx < b.minLon || pt.dx > b.maxLon) return false;
    for (final ring in poly.rings) {
      var inside = false;
      for (var i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        final a = ring[i], c = ring[j];
        if ((a.dy > pt.dy) != (c.dy > pt.dy) &&
            pt.dx < (c.dx - a.dx) * (pt.dy - a.dy) / (c.dy - a.dy) + a.dx) {
          inside = !inside;
        }
      }
      if (inside) return true;
    }
    return false;
  }

  /// All alarm slabs as ONE extruded surface of equal height: every wall of
  /// every slab far → near first, then the solid tops. Tops are opaque and all
  /// at the same height, so walls between neighbouring raions and the back
  /// walls are hidden — no jagged walls poking through (colour tells red/yellow).
  /// A region's rings (islands and holes, e.g. Київ inside Київська) at
  /// height [z] as one even-odd path, so holes stay open.
  Path _rings(List<List<Offset>> rings, double z) {
    final p = Path()..fillType = PathFillType.evenOdd;
    for (final r in rings) {
      p.addPath(_ring(r, z), Offset.zero);
    }
    return p;
  }

  void _slabs(Canvas canvas, List<(List<List<Offset>>, Color)> slabs, double h) {
    if (slabs.isEmpty) return;
    final land = AppColors.mapLand;
    final walls = <(double, Path, Color)>[];
    for (final (rings, base) in slabs) {
      final side = Color.alphaBlend(Color.lerp(base, Colors.black, 0.4)!.withValues(alpha: 0.85), land);
      for (final ring in rings) {
        final n = ring.length;
        for (var i = 0; i < n; i++) {
          final p0 = ring[i], p1 = ring[(i + 1) % n];
          if (p0 == p1) continue;
          final a = cam.proj(p0.dy, p0.dx), b = cam.proj(p1.dy, p1.dx);
          final at = cam.proj(p0.dy, p0.dx, h), bt = cam.proj(p1.dy, p1.dx, h);
          walls.add(((a.depth + b.depth) / 2,
              Path()
                ..moveTo(a.o.dx, a.o.dy)
                ..lineTo(b.o.dx, b.o.dy)
                ..lineTo(bt.o.dx, bt.o.dy)
                ..lineTo(at.o.dx, at.o.dy)
                ..close(),
              side));
        }
      }
    }
    walls.sort((x, y) => y.$1.compareTo(x.$1));
    final wp = Paint()..isAntiAlias = true;
    for (final w in walls) {
      canvas.drawPath(w.$2, wp..color = w.$3);
    }
    final top = Paint();
    final rim = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 0.8
      ..strokeJoin = StrokeJoin.round;
    for (final (rings, base) in slabs) {
      canvas.drawPath(_rings(rings, h), top..color = Color.alphaBlend(base.withValues(alpha: 0.62), land));
    }
    for (final (rings, base) in slabs) {
      canvas.drawPath(_rings(rings, h), rim..color = Color.lerp(base, Colors.white, 0.3)!.withValues(alpha: 0.55));
    }
  }

  static final Set<MapCity> _big = kBigCities.toSet();

  void _cities(Canvas canvas, Size size) {
    final z = cam.zoom;
    final placed = <Rect>[];
    final screen = Offset.zero & size;
    for (final c in kCities) {
      final big = _big.contains(c);
      final show = big ||
          (c.tier <= 1 && z >= 2.5 && !isCityDistrict(c.name)) ||
          (c.tier == 0 && z >= 4) ||
          (c.tier == 2 && z >= 6) ||
          (c.tier == 3 && z >= 12);
      if (!show) continue;
      final p = cam.proj(c.lat, c.lon).o;
      if (!screen.contains(p)) continue;
      final fs = big ? 11.0 : c.tier <= 1 ? 10.0 : 9.0;
      final label = cityLabel(c.name);
      final box = Rect.fromCenter(center: p + Offset(0, -fs), width: label.length * fs * 0.6 + 6, height: fs * 1.3);
      if (!big && placed.any((r) => r.overlaps(box))) continue;
      placed.add(box);
      canvas.drawCircle(p, big ? 2.6 : c.tier <= 1 ? 2 : 1.4,
          Paint()..color = big ? AppColors.textPrimary.withValues(alpha: 0.8) : AppColors.textSecondary);
      _text(canvas, label, p + Offset(0, -fs), fs,
          (big ? AppColors.textPrimary : AppColors.textSecondary).withValues(alpha: big ? 0.92 : 0.8),
          bold: big);
    }
  }

  void _home(Canvas canvas) {
    final g = cam.proj(home.lat, home.lon);
    final top = cam.proj(home.lat, home.lon, 18 * cam.hScale);
    final c = AppColors.primary;
    canvas.drawCircle(g.o, 6 + 10 * pulse,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.6
          ..color = c.withValues(alpha: 1 - pulse));
    canvas.drawLine(g.o, top.o, Paint()
      ..color = c
      ..strokeWidth = 2);
    canvas.drawCircle(top.o, 4.5, Paint()..color = c);
    canvas.drawCircle(top.o, 2, Paint()..color = Colors.white);
  }

  double _height(MapTarget t, TargetMotion m) => targetHeight3d(t, m, cam.hScale);

  void _target(Canvas canvas, MapTarget t, TargetMotion m) {
    final color = kThreatColors[t.cat] ?? kThreatColors['other']!;
    final z = _height(t, m);
    final ground = cam.proj(m.lat, m.lon);
    final air = cam.proj(m.lat, m.lon, z);
    if (!_view.contains(air.o) && !m.isFlight && !m.launch) return;

    if (m.launch) {
      final pts = [
        for (var a = 0; a <= 64; a++)
          () {
            final q = moveKm(m.lat, m.lon, a * 360 / 64, m.reachKm);
            return cam.proj(q.lat, q.lon).o;
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
        final h = (kab ? 55 * (1 - p) : _apogee(d) * 4 * p * (1 - p)) * cam.hScale;
        route.add(cam.proj(lat, lon, h).o);
        shadow.add(cam.proj(lat, lon).o);
      }
      final flownIdx = (m.progress! * steps).round();
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
      final aim = cam.proj(to[0], to[1]).o;
      canvas.drawCircle(
          aim,
          m.landed ? 10 + 26 * pulse : 6 + 4 * math.sin(pulse * 2 * math.pi).abs(),
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.6
            ..color = (m.landed ? const Color(0xFFFF453A) : color).withValues(alpha: m.landed ? 1 - pulse : 0.9));
      if (!m.landed && m.flightMin != null) {
        final left = (m.flightMin! * (1 - m.progress!)).clamp(0, 99);
        _text(canvas, left < 1 ? '<1 хв' : '${left.round()} хв', air.o + const Offset(20, -14), 11, color, bold: true);
      }
    } else if (!m.launch && t.heading != null && t.headingKnown && !t.down) {
      final lead = math.min(60.0, speedKmPerMin(t) * 10);
      final q = moveKm(m.lat, m.lon, m.heading, lead);
      _dashed(canvas, air.o, cam.proj(q.lat, q.lon, z).o, Paint()
        ..strokeWidth = 1.3
        ..color = color.withValues(alpha: 0.65));
    }

    // Silence: where it may be by now (it doesn't fly straight) — a faint ground ring.
    if (m.uncertKm > 0) {
      final pts = [
        for (var a = 0; a <= 48; a++)
          () {
            final q = moveKm(m.lat, m.lon, a * 360 / 48, m.uncertKm);
            return cam.proj(q.lat, q.lon).o;
          }(),
      ];
      final ring = Path()..addPolygon(pts, true);
      canvas.drawPath(ring, Paint()..color = color.withValues(alpha: 0.05));
      canvas.drawPath(
          ring,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1
            ..color = color.withValues(alpha: 0.35));
    }
    if (t.route.length >= 2 && (t.forecast || t.id == selectedId)) {
      final ahead = routeAhead(t.route, m.lat, m.lon);
      for (var i = 1; i < ahead.length; i++) {
        _dashed(canvas, cam.proj(ahead[i - 1][0], ahead[i - 1][1], z).o, cam.proj(ahead[i][0], ahead[i][1], z).o,
            Paint()
              ..strokeWidth = 1.2
              ..color = color.withValues(alpha: t.forecast ? 0.35 : 0.55));
      }
    }
    final faded = m.quietMin >= 6 || t.down || t.forecast;
    if (faded) canvas.saveLayer(null, Paint()..color = Colors.white.withValues(alpha: 0.5));

    canvas.drawOval(Rect.fromCenter(center: ground.o, width: 14, height: 14 * (1 - 0.6 * cam.st)),
        Paint()..color = Colors.black.withValues(alpha: 0.35));
    _dashed(canvas, ground.o, air.o, Paint()
      ..strokeWidth = 1
      ..color = color.withValues(alpha: 0.45));
    final r = (12.0 * (_Cam.d / air.depth)).clamp(9.0, 16.0);
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
    paintThreatGlyph(canvas, air.o, r * 1.7, t.cat, color, m.heading - cam.yaw);
    if (faded) canvas.restore();
    final alt = altitudeShort(t);
    if (t.forecast) {
      _text(canvas, 'прогноз', air.o + Offset(0, r + 11), 9.5, color, bold: true);
    } else if (t.down) {
      _text(canvas, alt.isEmpty ? 'падає' : alt, air.o + Offset(0, r + 12), 10.5, const Color(0xFFFF6B5E), bold: true);
    } else if (alt.isNotEmpty && cam.zoom >= 2) {
      _text(canvas, alt, air.o + Offset(r + 5, 6), 10, AppColors.textPrimary, bold: true);
    }
    if (selectedId != null && t.id == selectedId) {
      canvas.drawCircle(air.o, r + 6 + 7 * pulse,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 2.2
            ..color = AppColors.highlight.withValues(alpha: 1 - 0.7 * pulse));
      canvas.drawCircle(air.o, r + 3,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.5
            ..color = AppColors.highlight);
    }
    if (t.amount > 1) {
      _text(canvas, '×${t.amount}', air.o + Offset(0, -r - 9), 12, AppColors.textPrimary, bold: true);
    }
    if (cam.zoom >= 3 && t.place.isNotEmpty) {
      _text(canvas, t.place, air.o + Offset(0, r + 10), 10, color, bold: true);
    }
  }

  void _polyline(Canvas canvas, List<Offset> pts, Paint paint) {
    if (pts.length < 2) return;
    canvas.drawPath(Path()..addPolygon(pts, false), paint);
  }

  void _dashed(Canvas canvas, Offset a, Offset b, Paint paint) {
    const dash = 4.0, gap = 4.0;
    final total = (b - a).distance;
    if (total == 0 || total > 4000) return;
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
    final p = b.build()..layout(const ui.ParagraphConstraints(width: 240));
    canvas.drawParagraph(p, at - Offset(120, p.height / 2));
  }

  @override
  bool shouldRepaint(_Map3DPainter old) => true;
}
