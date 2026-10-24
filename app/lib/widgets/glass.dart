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
    final known = const ['shahed', 'missile', 'balist', 'kab', 'avia'].contains(c)
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
