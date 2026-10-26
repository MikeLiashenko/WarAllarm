import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../theme/app_colors.dart';
import '../utils/svg_path.dart';

/// Frosted panel that floats over the map: real backdrop blur, translucent
/// fill, hairline light border. The premium "layer" of the design.
class Glass extends StatelessWidget {
  const Glass({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
    this.radius = 16,
    this.color,
    this.borderColor,
    this.onTap,
    this.shadow = true,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final Color? color;
  final Color? borderColor;
  final VoidCallback? onTap;
  final bool shadow;

  @override
  Widget build(BuildContext context) {
    final r = BorderRadius.circular(radius);
    Widget body = Container(
      padding: padding,
      decoration: BoxDecoration(
        color: color ?? AppColors.glass,
        borderRadius: r,
        border: Border.all(color: borderColor ?? AppColors.glassBorder),
      ),
      child: child,
    );
    if (onTap != null) {
      body = Material(
        color: Colors.transparent,
        child: InkWell(borderRadius: r, onTap: onTap, child: body),
      );
    }
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: r,
        boxShadow: shadow
            ? const [
                BoxShadow(
                    color: Color(0x40000000),
                    blurRadius: 24,
                    spreadRadius: -6,
                    offset: Offset(0, 10)),
              ]
            : null,
      ),
      child: ClipRRect(
        borderRadius: r,
        child: BackdropFilter(
          filter: ui.ImageFilter.blur(sigmaX: 18, sigmaY: 18),
          child: body,
        ),
      ),
    );
  }
}

/// Round glass icon button (map controls, menu).
class GlassIconButton extends StatelessWidget {
  const GlassIconButton({
    super.key,
    required this.icon,
    required this.onTap,
    this.tooltip,
    this.size = 44,
  });

  final IconData icon;
  final VoidCallback onTap;
  final String? tooltip;
  final double size;

  @override
  Widget build(BuildContext context) {
    final b = Glass(
      padding: EdgeInsets.zero,
      radius: size / 2,
      onTap: onTap,
      child: SizedBox(
        width: size,
        height: size,
        child: Icon(icon, size: size * 0.45, color: AppColors.textPrimary),
      ),
    );
    return tooltip == null ? b : Tooltip(message: tooltip!, child: b);
  }
}

/// The map's own target silhouette (shahed / missile / …) as a small icon, so
/// counters and lists read exactly like the map.
class ThreatGlyph extends StatelessWidget {
  const ThreatGlyph(this.cat, {super.key, this.size = 18, this.color, this.heading = 0});

  final String cat;
  final double size;
  final Color? color;
  final double heading;

  static final Map<String, List<(Path, FigStyle)>> _cache = {};

  static List<(Path, FigStyle)> _parts(String cat) => _cache.putIfAbsent(
      cat,
      () => [
            for (final p in figParts(cat)) (parseSvgPath(p.d), p.style),
          ]);

  @override
  Widget build(BuildContext context) {
    final c = cat;
    final known = const ['shahed', 'shahed_jet', 'banderol', 'missile', 'balist', 'kab', 'avia', 'fpv'].contains(c)
        ? c
        : 'other';
    return SizedBox(
      width: size,
      height: size,
      child: CustomPaint(
        painter: _GlyphPainter(_parts(known), known,
            color ?? kThreatColors[known] ?? kThreatColors['other']!, heading),
      ),
    );
  }
}

class _GlyphPainter extends CustomPainter {
  _GlyphPainter(this.parts, this.cat, this.color, this.heading);
  final List<(Path, FigStyle)> parts;
  final String cat;
  final Color color;
  final double heading;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.save();
    canvas.translate(size.width / 2, size.height / 2);
    canvas.rotate(heading * 3.141592653589793 / 180);
    // Fig coords span roughly ±10 units.
    canvas.scale(size.width / 15 * figScale(cat).clamp(0.8, 1.1));
    for (final (path, style) in parts) {
      canvas.drawPath(
          path,
          Paint()
            ..color = switch (style) {
              FigStyle.dark => const Color(0x800C101A),
              FigStyle.light => const Color(0xB8FFFFFF),
              FigStyle.body => color,
            });
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(_GlyphPainter old) =>
      old.color != color || old.cat != cat || old.heading != heading;
}

/// A target as a glowing icon, no disc: a soft halo in [glow], a dark outline for
/// contrast on any background, the silhouette with a light-nose gradient, details.
/// Same look as the 2D map icons.
void paintThreatIcon(Canvas canvas, Offset c, double size, String cat, Color color, double headingDeg,
    {Color? glow, double glowStrength = 0.6}) {
  final known = const ['shahed', 'shahed_jet', 'banderol', 'missile', 'balist', 'kab', 'avia', 'fpv'].contains(cat) ? cat : 'other';
  final parts = ThreatGlyph._parts(known);
  final k = size / 15 * figScale(known).clamp(0.8, 1.1);
  canvas.save();
  canvas.translate(c.dx, c.dy);
  canvas.rotate(headingDeg * 3.141592653589793 / 180);
  canvas.scale(k);
  final halo = Paint()
    ..color = (glow ?? color).withValues(alpha: 0.75 * glowStrength)
    ..maskFilter = MaskFilter.blur(BlurStyle.normal, 7 / k);
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
  for (final (path, style) in parts) {
    if (style == FigStyle.body) canvas.drawPath(path, halo);
  }
  for (final (path, style) in parts) {
    if (style == FigStyle.body) canvas.drawPath(path, outline);
  }
  for (final (path, style) in parts) {
    canvas.drawPath(
        path,
        style == FigStyle.body
            ? fill
            : (Paint()..color = style == FigStyle.dark ? const Color(0x800C101A) : const Color(0xB8FFFFFF)));
  }
  canvas.restore();
}

/// Paint a target silhouette centred at [c], [size] px across, rotated to a
/// compass [headingDeg] — the same artwork as [ThreatGlyph] (3D map badges).
void paintThreatGlyph(Canvas canvas, Offset c, double size, String cat,
    Color color, double headingDeg) {
  final known = const ['shahed', 'shahed_jet', 'banderol', 'missile', 'balist', 'kab', 'avia', 'fpv'].contains(cat)
      ? cat
      : 'other';
  canvas.save();
  canvas.translate(c.dx - size / 2, c.dy - size / 2);
  _GlyphPainter(ThreatGlyph._parts(known), known, color, headingDeg)
      .paint(canvas, Size(size, size));
  canvas.restore();
}
