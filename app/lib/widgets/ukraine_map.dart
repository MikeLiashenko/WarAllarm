import 'dart:convert';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../data/ukraine_oblasts.dart';
import '../data/ukraine_raions.dart';
import '../domain/motion.dart';
import '../domain/situation.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';
import '../utils/svg_path.dart';

// Geographic bounds of the equirectangular projection — MUST match _Fit.project.
const double _lon0 = 22.14, _lonSpan = 18.09, _lat1 = 52.38, _latSpan = 7.99;

// Official-alert fills: a deep burgundy and a muted gold read well over imagery.
const Color _alarmRed = Color(0xFFC0303F);
// Warm amber: over the dark-blue land a pale yellow at low opacity turned muddy olive.
const Color _alarmYellow = Color(0xFFF2B705);

/// Fit the geographic bounds into [size] preserving the lon/lat aspect, so the
/// projection matches the oblast polygons exactly (the country is drawn from
/// them — no separate silhouette that could drift out of alignment).
_Fit _geoFit(Size size, [EdgeInsets insets = EdgeInsets.zero]) {
  const pad = 12.0;
  final aW = size.width - 2 * pad - insets.horizontal,
      aH = size.height - 2 * pad - insets.vertical;
  // Correct for longitude compression at Ukraine's latitude (a degree of lon is
  // ~cos(48.4°)≈0.66 of a degree of lat) — otherwise the country looks squashed.
  final midLat = (_lat1 + (_lat1 - _latSpan)) / 2;
  final aspect = (_lonSpan / _latSpan) * math.cos(midLat * math.pi / 180);
  double pw, ph;
  if (aW / aH > aspect) {
    ph = aH;
    pw = ph * aspect;
  } else {
    pw = aW;
    ph = pw / aspect;
  }
  return _Fit(
      s: 1,
      pw: pw,
      ph: ph,
      oX: insets.left + (size.width - insets.horizontal - pw) / 2,
      oY: insets.top + (size.height - insets.vertical - ph) / 2);
}

/// Notifies at most every ~33 ms: target animation at ≤30 fps instead of every vsync.
class _FrameClock extends ChangeNotifier {
  int _last = 0;
  void tick() {
    final n = DateTime.now().millisecondsSinceEpoch;
    if (n - _last >= 33) {
      _last = n;
      notifyListeners();
    }
  }
}

/// City labels drawn by the base layer — redrawn on top where a target icon covers them.
class _LabelSink {
  List<(String, Offset, double, Color, Rect)> items = const [];
}

/// Oblast / raion outlines projected ONCE per size into base (pre-zoom) pixels, then
/// drawn under the pan/zoom transform (no reprojection of ~30k points per frame).
class _BaseGeo {
  _BaseGeo(this.size, this.insets, List<_Oblast> oblasts, List<_Oblast> raions) {
    final fit = _geoFit(size, insets);
    Path poly(_Oblast ob) {
      final p = Path()..fillType = PathFillType.evenOdd;
      for (final ring in ob.rings) {
        for (var i = 0; i < ring.length; i++) {
          final s = fit.project(ring[i].dx, ring[i].dy);
          i == 0 ? p.moveTo(s.dx, s.dy) : p.lineTo(s.dx, s.dy);
        }
        p.close();
      }
      return p;
    }

    for (final ob in oblasts) {
      final p = poly(ob);
      oblastPaths.add((ob.name, p));
      land.addPath(p, Offset.zero);
    }
    for (final r in raions) {
      final p = poly(r);
      raionPaths.add((r.name, p));
      raionBorders.addPath(p, Offset.zero);
    }
    bounds = Rect.fromPoints(fit.project(_lon0, _lat1), fit.project(_lon0 + _lonSpan, _lat1 - _latSpan));
  }

  final Size size;
  final EdgeInsets insets;
  final oblastPaths = <(String, Path)>[];
  final raionPaths = <(String, Path)>[];
  final land = Path();
  final raionBorders = Path();
  late final Rect bounds;
}

/// Laid-out label paragraphs, reused across frames (layout + shadow setup was per frame).
final Map<String, ui.Paragraph> _paraCache = {};

void _drawText(Canvas canvas, String s, Offset at, double size, Color color,
    {bool center = false, bool stroke = false, bool bold = false}) {
  final key = '$s|$size|${color.toARGB32()}|$center|$stroke|$bold|${AppColors.isDark}';
  var para = _paraCache[key];
  if (para == null) {
    if (_paraCache.length > 800) _paraCache.clear();
    final builder = ui.ParagraphBuilder(ui.ParagraphStyle(
      textAlign: center ? TextAlign.center : TextAlign.left,
      fontSize: size,
      fontWeight: bold ? FontWeight.w700 : FontWeight.w600,
    ))
      ..pushStyle(ui.TextStyle(color: color))
      ..addText(s);
    para = _paraCache[key] = builder.build()..layout(const ui.ParagraphConstraints(width: 200));
  }
  final dx = center ? at.dx - 100 : at.dx;
  final dy = center ? at.dy - para.height : at.dy - para.height / 2;
  if (stroke) {
    // Halo = the same text in the land colour, nudged around (a blurred shadow per label
    // was a GPU blur pass each — over a hundred per frame on the tablet).
    final hk = '$s|$size|halo|$center|$bold|${AppColors.isDark}';
    var halo = _paraCache[hk];
    if (halo == null) {
      final hb = ui.ParagraphBuilder(ui.ParagraphStyle(
        textAlign: center ? TextAlign.center : TextAlign.left,
        fontSize: size,
        fontWeight: bold ? FontWeight.w700 : FontWeight.w600,
      ))
        ..pushStyle(ui.TextStyle(color: AppColors.mapLand.withValues(alpha: 0.85)))
        ..addText(s);
      halo = _paraCache[hk] = hb.build()..layout(const ui.ParagraphConstraints(width: 200));
    }
    for (final o in const [Offset(-1, -1), Offset(1, -1), Offset(-1, 1), Offset(1, 1), Offset(0, 1.4), Offset(0, -1.4)]) {
      canvas.drawParagraph(halo, Offset(dx, dy) + o);
    }
  }
  canvas.drawParagraph(para, Offset(dx, dy));
}

/// The static map: land, official alarm fills, raion / oblast borders, cities and labels.
/// Repaints only when the view, the alarms or the theme change — not with every target frame.
class _BasePainter extends CustomPainter {
  _BasePainter({
    required this.geo,
    required this.scale,
    required this.tx,
    required this.ty,
    required this.oblastWhole,
    required this.raionLevels,
    required this.insets,
    required this.light,
    required this.dark,
    required this.sink,
  });

  final _BaseGeo geo;
  final double scale, tx, ty;
  final Map<String, String> oblastWhole, raionLevels;
  final EdgeInsets insets;
  final bool light; // while panning / zooming: skip the costly border glow
  final bool dark;
  final _LabelSink sink;

  static final Set<MapCity> _bigSet = kBigCities.toSet();

  @override
  void paint(Canvas canvas, Size size) {
    final fit = _geoFit(size, insets);
    Offset toScreen(Offset b) => Offset(tx + scale * b.dx, ty + scale * b.dy);
    final px = 1 / scale; // one screen pixel in base units

    canvas.save();
    canvas.translate(tx, ty);
    canvas.scale(scale);
    // Land: a soft vertical gradient so the country reads as a raised surface
    // on the dark sea, plus a faint outer glow along the border.
    if (!light) {
      canvas.drawPath(
          geo.land,
          Paint()
            ..color = AppColors.primary.withValues(alpha: AppColors.isDark ? 0.10 : 0.08)
            ..maskFilter = MaskFilter.blur(BlurStyle.normal, 18 * px));
    }
    canvas.drawPath(
        geo.land,
        Paint()
          ..shader = LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [
              Color.lerp(AppColors.mapLand, Colors.white, AppColors.isDark ? 0.05 : 0)!,
              AppColors.mapLand,
            ],
          ).createShader(geo.bounds));
    // Official shading — fill a WHOLE oblast only when the whole oblast is under
    // alarm (like the official map). Per-raion alarms are drawn below.
    for (final (name, path) in geo.oblastPaths) {
      final lvl = oblastWhole[name];
      if (lvl == null) continue;
      final red = lvl == 'red';
      canvas.drawPath(path, Paint()..color = (red ? _alarmRed : _alarmYellow).withValues(alpha: red ? 0.5 : 0.6));
    }
    // --- Raion split: fill alarmed raions individually + faint raion borders,
    //     so partially-alarmed oblasts show WHICH raions (official-style). ---
    final raiLvl = <String, String>{for (final e in raionLevels.entries) _MapPainter._napo(e.key): e.value};
    if (raiLvl.isNotEmpty) {
      for (final (name, path) in geo.raionPaths) {
        final lvl = raiLvl[_MapPainter._napo(name)];
        if (lvl == null) continue;
        final red = lvl == 'red';
        canvas.drawPath(path, Paint()..color = (red ? _alarmRed : _alarmYellow).withValues(alpha: red ? 0.5 : 0.6));
      }
    }
    canvas.drawPath(
        geo.raionBorders,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 0.5 * px
          ..strokeJoin = StrokeJoin.round
          ..color = const Color(0xFF8296B4).withValues(alpha: 0.28));
    // Oblast borders (also the country outline — the outer edges).
    canvas.drawPath(
        geo.land,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = px
          ..strokeJoin = StrokeJoin.round
          ..color = AppColors.primary.withValues(alpha: 0.5));
    // Colored edge on wholly-alarmed oblasts, on top of the blue border.
    for (final (name, path) in geo.oblastPaths) {
      final lvl = oblastWhole[name];
      if (lvl == null) continue;
      final red = lvl == 'red';
      canvas.drawPath(
          path,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.3 * px
            ..strokeJoin = StrokeJoin.round
            ..color = (red ? _alarmRed : _alarmYellow).withValues(alpha: 0.9));
    }
    canvas.restore();

    // --- Cities / towns / villages (tiered by zoom, thinned + de-cluttered) ---
    // kCities is sorted big→small, so important places claim dots/labels first.
    final placed = <Rect>[]; // label boxes already drawn (collision avoidance)
    // Drawn city labels — redrawn on top where a target icon covers them («…цьк»).
    final cityLabels = <(String, Offset, double, Color, Rect)>[];
    final dotCell = <int, int>{}; // per-grid-cell village dot cap (even density)
    final dotFill = Paint()..color = AppColors.primary;
    final dotVillage = Paint()..color = AppColors.textMuted;
    final dotRim = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = AppColors.mapLand;
    for (final c in kCities) {
      if (isLatinName(c.name)) continue; // «Desna», «… Microdistrict» — англ. дублі
      // Country zoom: real big cities only (tier 0 also holds city districts,
      // and villages share big-city names) — see kBigCities.
      final big = c.tier <= 1 && _bigSet.contains(c);
      final show = big ||
          (c.tier <= 1 && !isCityDistrict(c.name) && scale >= 1.7) ||
          (c.tier == 0 && scale >= 1.7) ||
          (c.tier == 2 && scale >= 2.8) ||
          (c.tier == 3 && scale >= 4.2);
      if (!show) continue;
      final p = toScreen(fit.project(c.lon, c.lat));
      if (p.dx < -30 || p.dx > size.width + 30 || p.dy < -20 || p.dy > size.height + 20) continue;
      final village = c.tier == 3;

      // Even out village dot density: cap per ~46px screen cell.
      if (village) {
        final cell = (p.dx ~/ 46) * 100000 + (p.dy ~/ 46);
        final n = dotCell[cell] ?? 0;
        if (n >= 2) continue;
        dotCell[cell] = n + 1;
      }

      final r = c.tier == 0
          ? 2.4
          : c.tier == 1
              ? 1.9
              : c.tier == 2
                  ? 1.4
                  : 1.0;
      canvas.drawCircle(p, r, village ? dotVillage : dotFill);
      canvas.drawCircle(p, r, dotRim);

      if (village && scale < 5.2) continue; // village labels only when close
      final fs = c.tier == 0
          ? 10.5
          : c.tier == 1
              ? 9.0
              : c.tier == 2
                  ? 7.5
                  : 6.5;
      // Skip a label if it would overlap one already drawn.
      final w = c.name.length * fs * 0.6 + 4;
      final h = fs * 1.2;
      // Near the screen edge (Луганськ on a phone) slide the label inwards
      // instead of letting it be cut to «Луганс».
      final cx = size.width > w + 4 ? p.dx.clamp(w / 2 + 2, size.width - w / 2 - 2).toDouble() : p.dx;
      final box = Rect.fromCenter(center: Offset(cx, p.dy - (r + 3) - h / 2), width: w, height: h);
      var clash = false;
      for (final o in placed) {
        if (box.overlaps(o)) {
          clash = true;
          break;
        }
      }
      if (clash) continue;
      placed.add(box.inflate(1));
      final labelColor = village ? AppColors.textSecondary : AppColors.textPrimary;
      _drawText(canvas, cityLabel(c.name), Offset(cx, p.dy - (r + 3)), fs, labelColor, center: true, stroke: true);
      cityLabels.add((cityLabel(c.name), Offset(cx, p.dy - (r + 3)), fs, labelColor, box));
    }
    sink.items = cityLabels;
  }

  @override
  bool shouldRepaint(_BasePainter old) =>
      old.geo != geo ||
      old.scale != scale ||
      old.tx != tx ||
      old.ty != ty ||
      old.light != light ||
      old.dark != dark ||
      !mapEquals(old.oblastWhole, oblastWhole) ||
      !mapEquals(old.raionLevels, raionLevels);
}

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
    this.audibilityKm = 0,
    this.showAudibility = false,
    this.oblastLevels = const {},
    this.raionLevels = const {},
    this.oblastWhole = const {},
    this.showControls = true,
    this.showLegend = true,
    this.insets = EdgeInsets.zero,
    this.dimTargets = false,
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

  /// The user dragged / zoomed the map themselves (e.g. to stop following).
  final VoidCallback? onUserMove;

  final bool showControls;
  final bool showLegend;

  /// Screen area covered by floating UI — the country is fitted into the rest.
  final EdgeInsets insets;

  /// Draw targets faded (stale snapshot: last known positions, not live).
  final bool dimTargets;

  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final String homeName;
  final double audibilityKm;
  final bool showAudibility;

  /// Official air-raid status: oblast polygon name -> 'red' | 'yellow'.
  final Map<String, String> oblastLevels;

  /// Raion-level split (like the official map): "X район" -> 'red' | 'yellow'.
  final Map<String, String> raionLevels;

  /// Oblasts under alarm as a WHOLE (filled entirely): "n" -> 'red' | 'yellow'.
  final Map<String, String> oblastWhole;

  @override
  State<UkraineMap> createState() => UkraineMapState();
}

class UkraineMapState extends State<UkraineMap>
    with SingleTickerProviderStateMixin {
  late final Map<String, List<_FigPathPart>> _figs;
  late final List<_Oblast> _oblasts;
  late final List<_Oblast> _raions;
  late final AnimationController _anim;
  // The tablet (120 Hz) repainted the whole map 120×/s and lagged until it crashed: targets
  // now animate at ≤30 fps, and the static map (land, alarms, cities) lives in its own
  // layer that repaints only when the view or the alarms change.
  final _clock = _FrameClock();
  final _labels = _LabelSink();
  // Impeller (Android) has no raster cache: even an unchanged layer re-tessellated ~30k
  // border points and re-blurred every label shadow each frame. So the static map is drawn
  // ONCE into an image; frames just blit it (moved / scaled while panning) and it is
  // re-rendered at most every 0.3 s during a gesture and right after it.
  ui.Image? _baseImg;
  ({double s, double tx, double ty})? _baseView;
  Object? _baseKey;
  bool _baseBusy = false;
  int _baseAt = 0;
  _BaseGeo? _geo;
  bool _interacting = false;

  double _scale = 1;
  double _tx = 0, _ty = 0;
  Size _size = Size.zero;
  bool _focused = false;

  // Gesture bookkeeping.
  double _startScale = 1;
  Offset _startT = Offset.zero;
  Offset _focalBase = Offset.zero;

  // --- Target picking & following ---
  String? _followStarted;

  /// Screen position of a target right now (same projection + motion as the painter).
  Offset _screenOf(MapTarget t) {
    final mo = motionOf(t, DateTime.now().millisecondsSinceEpoch, frozen: widget.dimTargets);
    final b = _geoFit(_size, widget.insets).project(mo.lon, mo.lat);
    return Offset(_tx + _scale * b.dx, _ty + _scale * b.dy);
  }

  /// The target under [local] (nearest within ~28 px), or null.
  MapTarget? targetAt(Offset local) {
    MapTarget? best;
    var bd = 28.0;
    for (final t in widget.targets) {
      final d = (_screenOf(t) - local).distance;
      if (d < bd) {
        bd = d;
        best = t;
      }
    }
    return best;
  }

  /// Keep the followed target in the middle of the visible map (every frame).
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
      if (_scale < 3.5) _scale = 3.5; // come closer once when following starts
    }
    final mo = motionOf(t, DateTime.now().millisecondsSinceEpoch, frozen: widget.dimTargets);
    final b = _geoFit(_size, widget.insets).project(mo.lon, mo.lat);
    final c = Offset(widget.insets.left + (_size.width - widget.insets.horizontal) / 2,
        widget.insets.top + (_size.height - widget.insets.vertical) / 2);
    _tx = c.dx - _scale * b.dx;
    _ty = c.dy - _scale * b.dy;
  }

  // --- Public controls (the screen draws its own glass buttons) ---
  void zoomIn() => _zoomAt(_size.center(Offset.zero), 1.35);
  void zoomOut() => _zoomAt(_size.center(Offset.zero), 1 / 1.35);
  void resetView() => setState(() => _autoFocus(_size));

  /// Centre a point in the visible map, zooming in to at least [scale].
  void focusOn(double lat, double lon, {double scale = 3.5}) => setState(() {
        if (_scale < scale) _scale = scale;
        final b = _geoFit(_size, widget.insets).project(lon, lat);
        final c = Offset(widget.insets.left + (_size.width - widget.insets.horizontal) / 2,
            widget.insets.top + (_size.height - widget.insets.vertical) / 2);
        _tx = c.dx - _scale * b.dx;
        _ty = c.dy - _scale * b.dy;
      });

  @override
  void initState() {
    super.initState();
    _figs = _buildFigs();
    _oblasts = _parsePolys(kOblastsJson);
    _raions = _parsePolys(kRaionsJson);
    _anim = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1600),
    )
      ..addListener(_clock.tick)
      ..repeat();
  }

  List<_Oblast> _parsePolys(String json) {
    final data = jsonDecode(json) as List;
    final out = <_Oblast>[];
    for (final o in data) {
      final rings = <List<Offset>>[];
      for (final ring in (o['r'] as List)) {
        rings.add([
          for (final p in (ring as List))
            Offset((p[0] as num).toDouble(), (p[1] as num).toDouble()),
        ]);
      }
      out.add(_Oblast((o['n'] ?? '').toString(), rings));
    }
    return out;
  }

  Map<String, List<_FigPathPart>> _buildFigs() {
    final cats = ['shahed', 'shahed_jet', 'banderol', 'missile', 'balist', 'kab', 'avia', 'fpv', 'other'];
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
    _clock.dispose();
    _baseImg?.dispose();
    super.dispose();
  }

  String _levelsKey(Map<String, String> m) =>
      (m.entries.map((e) => '${e.key}=${e.value}').toList()..sort()).join(',');

  /// Re-render the static map image when the view, alarms or theme changed.
  void _renderBase(_BaseGeo geo, double dpr) {
    if (_size.isEmpty || _baseBusy) return;
    final key = (_scale, _tx, _ty, _size, widget.insets, AppColors.isDark, _interacting,
        _levelsKey(widget.oblastWhole), _levelsKey(widget.raionLevels));
    if (key == _baseKey) return;
    final now = DateTime.now().millisecondsSinceEpoch;
    if (_interacting && _baseImg != null && now - _baseAt < 300) return;
    _baseBusy = true;
    _baseAt = now;
    final view = (s: _scale, tx: _tx, ty: _ty);
    final ratio = math.min(dpr, 2.0);
    final rec = ui.PictureRecorder();
    final c = Canvas(rec)..scale(ratio);
    _BasePainter(
      geo: geo,
      scale: view.s,
      tx: view.tx,
      ty: view.ty,
      oblastWhole: widget.oblastWhole,
      raionLevels: widget.raionLevels,
      insets: widget.insets,
      light: _interacting,
      dark: AppColors.isDark,
      sink: _labels,
    ).paint(c, _size);
    final pic = rec.endRecording();
    pic.toImage((_size.width * ratio).ceil(), (_size.height * ratio).ceil()).then((img) {
      pic.dispose();
      _baseBusy = false;
      if (!mounted) {
        img.dispose();
        return;
      }
      final old = _baseImg;
      setState(() {
        _baseImg = img;
        _baseView = view;
        _baseKey = key;
      });
      if (old != null) WidgetsBinding.instance.addPostFrameCallback((_) => old.dispose());
    }, onError: (_) {
      pic.dispose();
      _baseBusy = false;
    });
  }

  // --- Projection helpers (recomputed from current size) ----------------------

  void _autoFocus(Size size) {
    // Default view = the WHOLE country, centred and evenly proportioned (like the
    // clean bot map). Zoom in with pinch / wheel / the + button to see towns and
    // villages. This keeps the default map recognisable and uncluttered.
    _scale = 1;
    _tx = 0;
    _ty = 0;
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
            Listener(
              onPointerSignal: (s) {
                if (s is PointerScrollEvent) {
                  final f = s.scrollDelta.dy < 0 ? 1.15 : 1 / 1.15;
                  _zoomAt(s.localPosition, f);
                }
              },
              child: GestureDetector(
              onTapUp: (d) => widget.onTargetTap?.call(targetAt(d.localPosition)),
              onScaleStart: (d) {
                _interacting = true;
                _startScale = _scale;
                _startT = Offset(_tx, _ty);
                _focalBase = (d.localFocalPoint - _startT) / _startScale;
              },
              onScaleUpdate: (d) {
                if ((d.focalPointDelta.distance > 2 || d.scale != 1) && widget.followId != null) {
                  widget.onUserMove?.call();
                }
                setState(() {
                  _scale = (_startScale * d.scale).clamp(1.0, 8.0);
                  _tx = d.localFocalPoint.dx - _scale * _focalBase.dx;
                  _ty = d.localFocalPoint.dy - _scale * _focalBase.dy;
                });
              },
              onScaleEnd: (_) => setState(() => _interacting = false),
              child: AnimatedBuilder(
                animation: _clock,
                builder: (context, _) {
                  _followTick();
                  final geo = _geo != null && _geo!.size == _size && _geo!.insets == widget.insets
                      ? _geo!
                      : (_geo = _BaseGeo(_size, widget.insets, _oblasts, _raions));
                  _renderBase(geo, MediaQuery.devicePixelRatioOf(context));
                  final bv = _baseView;
                  return CustomPaint(
                  size: Size.infinite,
                  painter: _MapPainter(
                    figs: _figs,
                    targets: widget.targets,
                    home: widget.home,
                    scale: _scale,
                    tx: _tx,
                    ty: _ty,
                    pulse: _anim.value,
                    nowMs: DateTime.now().millisecondsSinceEpoch,
                    textDir: Directionality.of(context),
                    oblasts: _oblasts,
                    raions: _raions,
                    audibilityKm:
                        widget.showAudibility ? widget.audibilityKm : 0,
                    oblastLevels: widget.oblastLevels,
                    raionLevels: widget.raionLevels,
                    oblastWhole: widget.oblastWhole,
                    insets: widget.insets,
                    dimTargets: widget.dimTargets,
                    selectedId: widget.selectedId,
                    labels: _labels,
                    base: _baseImg,
                    baseView: bv,
                    labelsValid: bv != null && bv.s == _scale && bv.tx == _tx && bv.ty == _ty,
                  ),
                  );
                },
              ),
            ),
            ),
            if (widget.showControls)
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
            if (widget.showLegend)
              const Positioned(left: 12, bottom: 12, child: _Legend()),
          ],
        );
      },
    );
  }

  Widget _zoomButton(IconData icon, VoidCallback onTap) {
    return Material(
      color: AppColors.surface2,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(10),
        side: BorderSide(color: AppColors.border),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(9),
        onTap: onTap,
        child: SizedBox(
          width: 34,
          height: 34,
          child: Icon(icon, color: AppColors.primaryDark, size: 18),
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

class _Oblast {
  final String name;
  final List<List<Offset>> rings; // [outer, holes...] in (lon,lat)
  _Oblast(this.name, this.rings);
}

class _Fit {
  final double s, pw, ph, oX, oY;
  _Fit({required this.s, required this.pw, required this.ph, required this.oX, required this.oY});

  /// lon/lat → base (pre-zoom) pixel space.
  Offset project(double lon, double lat) => Offset(
        oX + ((lon - _lon0) / _lonSpan) * pw,
        oY + ((_lat1 - lat) / _latSpan) * ph,
      );
}

class _MapPainter extends CustomPainter {
  _MapPainter({
    required this.figs,
    required this.targets,
    required this.home,
    required this.scale,
    required this.tx,
    required this.ty,
    required this.pulse,
    required this.nowMs,
    required this.textDir,
    required this.oblasts,
    required this.raions,
    required this.audibilityKm,
    required this.oblastLevels,
    required this.raionLevels,
    required this.oblastWhole,
    this.insets = EdgeInsets.zero,
    this.dimTargets = false,
    this.selectedId,
    required this.labels,
    this.base,
    this.baseView,
    this.labelsValid = false,
  });

  final _LabelSink labels;
  final ui.Image? base;
  final ({double s, double tx, double ty})? baseView;
  final bool labelsValid; // the image shows exactly this view → its label boxes match
  final String? selectedId;
  final EdgeInsets insets;
  final bool dimTargets;

  final Map<String, List<_FigPathPart>> figs;
  final List<MapTarget> targets;
  final ({double lat, double lon}) home;
  final double scale, tx, ty, pulse;
  final int nowMs;
  final TextDirection textDir;
  final List<_Oblast> oblasts;
  final List<_Oblast> raions;
  final double audibilityKm;
  final Map<String, String> oblastLevels;
  final Map<String, String> raionLevels;
  final Map<String, String> oblastWhole;


  Offset _toScreen(Offset base) => Offset(tx + scale * base.dx, ty + scale * base.dy);

  /// Apostrophe normalization (feed uses U+0027, geometry uses U+2019) so raion
  /// names match between the alert feed and the polygons.
  static String _napo(String s) => s
      .replaceAll('’', "'")
      .replaceAll('ʼ', "'")
      .replaceAll('`', "'");

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
      if (isLatinName(c.name)) continue;
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
    final ppu = (size.width / 420).clamp(2.0, 3.4);

    // The static map (land, alarms, borders, cities): a cached image from _BasePainter,
    // moved / scaled to the current view (new view = old view × k + shift).
    final img = base, bv = baseView;
    if (img != null && bv != null) {
      final k = scale / bv.s;
      canvas.save();
      canvas.translate(tx - k * bv.tx, ty - k * bv.ty);
      canvas.scale(k);
      canvas.drawImageRect(
          img,
          Rect.fromLTWH(0, 0, img.width.toDouble(), img.height.toDouble()),
          Offset.zero & size,
          Paint()..filterQuality = FilterQuality.low);
      canvas.restore();
    }
    // --- Hot-zone glow under target clusters (radial gradient: a blur per target per frame was heavy) ---
    if (!dimTargets) {
      for (final t in targets) {
        // Де мітка зараз: «на Харків» ще не над Харковом.
        final hm = motionOf(t, nowMs, frozen: dimTargets);
        final p = _toScreen(fit.project(hm.lon, hm.lat));
        final r = 34 + 5.0 * t.amount;
        canvas.drawCircle(
            p,
            r,
            Paint()
              ..shader = RadialGradient(colors: [
                const Color(0xFFFF3B30).withValues(alpha: 0.16),
                const Color(0xFFFF3B30).withValues(alpha: 0),
              ]).createShader(Rect.fromCircle(center: p, radius: r)));
      }
    }

    // --- Audibility radius around home ("what we can hear") ---
    if (audibilityKm > 0) {
      final hpc = _toScreen(fit.project(home.lon, home.lat));
      final edge =
          _toScreen(fit.project(home.lon, home.lat + audibilityKm / 111));
      final rPx = (hpc - edge).distance;
      canvas.drawCircle(
          hpc,
          rPx,
          Paint()
            ..style = PaintingStyle.fill
            ..color = const Color(0xFF3B9EFF).withValues(alpha: 0.08));
      canvas.drawCircle(
          hpc,
          rPx,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1.4
            ..color = const Color(0xFF3B9EFF).withValues(alpha: 0.55));
      _text(canvas, '~${audibilityKm.round()} км чутно',
          hpc + Offset(0, -rPx - 5), 9.5, AppColors.primaryDark,
          center: true, stroke: true);
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

    // --- Targets: compact badges (dark disc + coloured ring + silhouette) ---
    // A stale snapshot is drawn faded and frozen (no dead-reckoning).
    if (dimTargets) {
      canvas.saveLayer(Offset.zero & size,
          Paint()..color = Colors.white.withValues(alpha: 0.4));
    }
    final badgeR = (ppu * 4.6).clamp(10.0, 15.0);
    final iconBoxes = <Rect>[];
    for (final t in targets) {
      final key = iconKey(t.cat, t.label); // reactive shahed has its own silhouette
      final cat = figs.containsKey(key) ? key : figs.containsKey(t.cat) ? t.cat : 'other';
      final color = threatColor(t.cat, t.label);
      // Where it is now: flight From→To (ballistic/КАБ), launch ring, or
      // dead-reckoning along the heading — all at the weapon's real speed.
      final mo = motionOf(t, nowMs, frozen: dimTargets);
      // Course line only for a REAL course (seen moving), never the «inland» guess.
      final known = t.headingKnown && t.heading != null && !t.down && !mo.isFlight && !mo.launch;
      final spd = speedKmPerMin(t);
      final double lon = mo.lon, lat = mo.lat;
      final scr = _toScreen(fit.project(lon, lat));

      if (mo.launch) {
        // Launch, target unknown: ring of how far it could have flown by now.
        final edge = _toScreen(fit.project(lon, lat + mo.reachKm / 111));
        final rPx = (scr - edge).distance;
        if (rPx > 2) {
          canvas.drawCircle(scr, rPx, Paint()..color = color.withValues(alpha: 0.06));
          _dashedCircle(canvas, scr, rPx,
              Paint()
                ..style = PaintingStyle.stroke
                ..strokeWidth = 1.4
                ..color = color.withValues(alpha: 0.7));
        }
        _text(canvas, 'пуск: ${t.fromName}', scr + Offset(0, badgeR + 14), 10.5,
            color, center: true, stroke: true, bold: true);
      }

      // ✈ Aviation zone: KAB range circle (purple) / aviation (yellow) on the ground.
      if (t.areaKm > 0) {
        final kab = t.label.contains('КАБ');
        final zc = kab ? kThreatColors['kab']! : color;
        final ctr = _toScreen(fit.project(t.lon, t.lat));
        final rPx = (ctr - _toScreen(fit.project(t.lon, t.lat + t.areaKm / 111))).distance;
        canvas.drawCircle(
            ctr,
            rPx,
            Paint()
              ..shader = RadialGradient(colors: [zc.withValues(alpha: 0.18), zc.withValues(alpha: 0.04)])
                  .createShader(Rect.fromCircle(center: ctr, radius: rPx)));
        _dashedCircle(
            canvas,
            ctr,
            rPx,
            Paint()
              ..style = PaintingStyle.stroke
              ..strokeWidth = 1.4
              ..color = zc.withValues(alpha: 0.8));
        _text(canvas, kab ? 'загроза КАБ' : 'авіація', ctr + Offset(0, rPx + 10), 10.5, zc,
            center: true, stroke: true, bold: true);
      }

      if (mo.isFlight) {
        final a = _toScreen(fit.project(t.from![1], t.from![0]));
        final b = _toScreen(fit.project(t.to![1], t.to![0]));
        // Whole route (dashed), the part already flown (solid), the aim point.
        _dashedLine(canvas, a, b,
            Paint()
              ..color = color.withValues(alpha: 0.45)
              ..strokeWidth = 1.2
              ..strokeCap = StrokeCap.round);
        canvas.drawLine(
            a,
            scr,
            Paint()
              ..color = color.withValues(alpha: 0.85)
              ..strokeWidth = 2
              ..strokeCap = StrokeCap.round);
        canvas.drawCircle(a, 3, Paint()..color = color.withValues(alpha: 0.7));
        final aim = 7 + 5 * (math.sin(pulse * 2 * math.pi).abs());
        canvas.drawCircle(
            b,
            mo.landed ? 14 + 22 * pulse : aim,
            Paint()
              ..style = PaintingStyle.stroke
              ..strokeWidth = 1.6
              ..color = (mo.landed ? const Color(0xFFFF453A) : color)
                  .withValues(alpha: mo.landed ? 1 - pulse : 0.9));
        canvas.drawLine(b - const Offset(5, 0), b + const Offset(5, 0), Paint()..color = color..strokeWidth = 1.2);
        canvas.drawLine(b - const Offset(0, 5), b + const Offset(0, 5), Paint()..color = color..strokeWidth = 1.2);
        if (!mo.landed && mo.flightMin != null) {
          final left = (mo.flightMin! * (1 - mo.progress!)).clamp(0, 99);
          _text(canvas, left < 1 ? '<1 хв' : '${left.round()} хв', scr + Offset(badgeR + 4, -badgeR),
              11, color, stroke: true, bold: true);
        }
      }

      // Silence: where it may be by now (it doesn't fly straight) — a faint ring.
      if (mo.uncertKm > 0) {
        final edge = _toScreen(fit.project(lon, lat + mo.uncertKm / 111));
        final rPx = (scr - edge).distance;
        if (rPx > badgeR + 4) {
          canvas.drawCircle(scr, rPx, Paint()..color = color.withValues(alpha: 0.05));
          _dashedCircle(canvas, scr, rPx,
              Paint()
                ..style = PaintingStyle.stroke
                ..strokeWidth = 1
                ..color = color.withValues(alpha: 0.35));
        }
      }

      // Where it is expected to go: launch groups always, the picked target too.
      if (t.route.length >= 2 && (t.forecast || t.toPlace || t.id == selectedId) && !dimTargets) {
        final ahead = routeAhead(t.route, lat, lon);
        for (var i = 1; i < ahead.length; i++) {
          _dashedLine(
              canvas,
              _toScreen(fit.project(ahead[i - 1][1], ahead[i - 1][0])),
              _toScreen(fit.project(ahead[i][1], ahead[i][0])),
              Paint()
                ..color = color.withValues(alpha: t.forecast ? 0.35 : 0.55)
                ..strokeWidth = 1.2
                ..strokeCap = StrokeCap.round);
        }
      }

      // Trail.
      if (t.trail.length > 1) {
        final path = Path();
        for (var i = 0; i < t.trail.length; i++) {
          final s = _toScreen(fit.project(t.trail[i][1], t.trail[i][0]));
          i == 0 ? path.moveTo(s.dx, s.dy) : path.lineTo(s.dx, s.dy);
        }
        canvas.drawPath(
            path,
            Paint()
              ..style = PaintingStyle.stroke
              ..strokeWidth = 1.4
              ..strokeCap = StrokeCap.round
              ..color = color.withValues(alpha: 0.4));
      }

      // Course prediction: dashed line to where it's heading (+ city when zoomed).
      // Never past the all-clear border / out of Ukraine.
      final lead = known && spd > 0 && !dimTargets
          ? allowedLeadKm(lat, lon, t.heading!, math.min(55.0, spd * 10))
          : 0.0;
      if (lead >= 3) {
        final pd = _moveKm(lon, lat, t.heading!, lead);
        final ps = _toScreen(fit.project(pd.dx, pd.dy));
        _dashedLine(canvas, scr, ps,
            Paint()
              ..color = color.withValues(alpha: 0.6)
              ..strokeWidth = 1.3
              ..strokeCap = StrokeCap.round);
        canvas.drawCircle(ps, 2.6, Paint()..color = color.withValues(alpha: 0.8));
        if (scale >= 1.6) {
          final nc = _nearestCity(pd.dx, pd.dy);
          if (nc != null) {
            _text(canvas, '→ ${nc.name}?', ps + const Offset(7, -8), 10.5,
                color, stroke: true, bold: true);
          }
        }
      }

      // Badge — faded once nothing new was heard for a while.
      final faded = mo.quietMin >= 6 || t.down || t.forecast;
      if (faded) canvas.saveLayer(Rect.fromCircle(center: scr, radius: badgeR + 8), Paint()..color = Colors.white.withValues(alpha: 0.5));
      // No disc / ring — just the silhouette with a glow (Mike: «просто красиві іконки»).
      final picked = selectedId != null && t.id == selectedId;
      final near = (kRank[t.stage] ?? 0) >= 3 && !dimTargets;
      _drawIcon(canvas, scr, mo.heading, cat, color, badgeR / 7.8,
          glow: picked ? AppColors.highlight : near ? const Color(0xFFFF453A) : color,
          glowStrength: picked ? 1.0 : near ? 0.55 + 0.45 * pulse : 0.55);
      if (faded) canvas.restore();
      iconBoxes.add(Rect.fromCircle(center: scr, radius: badgeR * 1.4));
      if (t.forecast) {
        _text(canvas, 'прогноз', scr + Offset(0, badgeR + 11), 9.5, color.withValues(alpha: 0.85),
            center: true, stroke: true, bold: true);
      }
      if (t.down) {
        _text(canvas, t.altNote.isEmpty ? 'падає' : t.altNote, scr + Offset(0, badgeR + 12), 10.5,
            const Color(0xFFFF6B5E), center: true, stroke: true, bold: true);
      }

      // ×N cluster label above the badge.
      if (t.amount > 1) {
        _text(canvas, '×${t.amount}', scr + Offset(0, -badgeR - 2), 12.5,
            AppColors.textPrimary, center: true, stroke: true, bold: true);
      }
      // Place label when zoomed in.
      if (scale >= 2.2 && t.place.isNotEmpty) {
        final alt = altitudeShort(t);
        _text(canvas, alt.isEmpty ? t.place : '${t.place} · $alt', scr + Offset(badgeR + 4, 0), 10.5,
            AppColors.textPrimary, stroke: true, bold: true);
      }
    }
    if (dimTargets) canvas.restore();
    for (final (name, at, fs, color, box) in labelsValid ? labels.items : const <(String, Offset, double, Color, Rect)>[]) {
      if (iconBoxes.any((b) => b.overlaps(box))) {
        _text(canvas, name, at, fs, color, center: true, stroke: true);
      }
    }
  }

  _Fit _fit(Size size) => _geoFit(size, insets);

  /// A target as a glowing icon: soft halo in [glow], a dark outline for contrast on
  /// any map colour, then the silhouette with a light-nose gradient and its details.
  void _drawIcon(Canvas canvas, Offset at, double headingDeg, String cat, Color color, double ppu,
      {required Color glow, double glowStrength = 0.55}) {
    final parts = figs[cat]!;
    final k = figScale(cat) * ppu;
    canvas.save();
    canvas.translate(at.dx, at.dy);
    canvas.rotate(headingDeg * math.pi / 180);
    canvas.scale(k);
    final halo = Paint()
      ..shader = RadialGradient(colors: [
        glow.withValues(alpha: 0.55 * glowStrength),
        glow.withValues(alpha: 0),
      ]).createShader(Rect.fromCircle(center: Offset.zero, radius: 15));
    final outline = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2.2 / k
      ..strokeJoin = StrokeJoin.round
      ..color = const Color(0xE60A0F19);
    final fill = Paint()
      ..shader = LinearGradient(
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
        colors: [Color.lerp(color, Colors.white, 0.45)!, color, Color.lerp(color, Colors.black, 0.15)!],
        stops: const [0, 0.45, 1],
      ).createShader(const Rect.fromLTRB(-11, -12, 11, 11));
    canvas.drawCircle(Offset.zero, 15, halo);
    for (final p in parts.where((p) => p.body)) {
      canvas.drawPath(p.path, outline);
    }
    for (final p in parts) {
      canvas.drawPath(p.path, p.body ? fill : (Paint()..color = p.overrideColor ?? color));
    }
    canvas.restore();
  }

  void _dashedCircle(Canvas canvas, Offset c, double r, Paint paint) {
    const n = 64;
    for (var i = 0; i < n; i += 2) {
      canvas.drawArc(Rect.fromCircle(center: c, radius: r), i * 2 * math.pi / n,
          2 * math.pi / n, false, paint);
    }
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
          {bool center = false, bool stroke = false, bool bold = false}) =>
      _drawText(canvas, s, at, size, color, center: center, stroke: stroke, bold: bold);

  @override
  bool shouldRepaint(_MapPainter old) => true;
}

class _Legend extends StatelessWidget {
  const _Legend();

  @override
  Widget build(BuildContext context) {
    const items = [
      ('Шахед', Color(0xFFFFE14D)),
      ('Крилата', Color(0xFFFF7A3C)),
      ('Балістика', Color(0xFFFF3B30)),
      ('КАБ', Color(0xFFC77DFF)),
      ('Авіація', Color(0xFF7DD3FC)),
    ];
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: AppColors.surface.withValues(alpha: 0.92),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: AppColors.border),
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
                    style: TextStyle(
                        color: AppColors.textSecondary,
                        fontSize: 11,
                        fontWeight: FontWeight.w600)),
              ],
            ),
        ],
      ),
    );
  }
}
