import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme/app_colors.dart';

/// Prominent central status circle. Calm = steady blue; threat = animated red
/// pulse. The only place red is allowed outside of alert list highlights.
class ThreatCircle extends StatefulWidget {
  const ThreatCircle({
    super.key,
    required this.active,
    required this.label,
    required this.sublabel,
    this.size = 240,
  });

  final bool active;
  final String label;
  final String sublabel;
  final double size;

  @override
  State<ThreatCircle> createState() => _ThreatCircleState();
}

class _ThreatCircleState extends State<ThreatCircle>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1600),
    )..repeat();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final base = widget.active ? AppColors.threat : AppColors.primary;
    final dark = widget.active ? AppColors.threatDark : AppColors.primaryDark;

    return SizedBox(
      width: widget.size,
      height: widget.size,
      child: AnimatedBuilder(
        animation: _controller,
        builder: (context, _) {
          final t = _controller.value;
          return CustomPaint(
            painter: _RingPainter(
              progress: t,
              active: widget.active,
              color: base,
            ),
            child: Center(
              child: Container(
                width: widget.size * 0.62,
                height: widget.size * 0.62,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: LinearGradient(
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                    colors: [base, dark],
                  ),
                  boxShadow: [
                    BoxShadow(
                      color: base.withValues(alpha: 0.45),
                      blurRadius: 40,
                      spreadRadius: widget.active ? 6 + 8 * t : 4,
                    ),
                  ],
                ),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(
                      widget.active
                          ? Icons.warning_amber_rounded
                          : Icons.shield_rounded,
                      color: Colors.white,
                      size: widget.size * 0.18,
                    ),
                    const SizedBox(height: 6),
                    Text(
                      widget.label,
                      style: TextStyle(
                        color: Colors.white,
                        fontSize: widget.size * 0.085,
                        fontWeight: FontWeight.w800,
                        letterSpacing: -0.5,
                      ),
                    ),
                    Text(
                      widget.sublabel,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.85),
                        fontSize: widget.size * 0.05,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class _RingPainter extends CustomPainter {
  _RingPainter({
    required this.progress,
    required this.active,
    required this.color,
  });

  final double progress;
  final bool active;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final maxRadius = size.width / 2;

    // Expanding pulse rings when a threat is active.
    if (active) {
      for (var i = 0; i < 3; i++) {
        final p = (progress + i / 3) % 1.0;
        final radius = size.width * 0.34 + (maxRadius - size.width * 0.34) * p;
        final paint = Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2.5
          ..color = color.withValues(alpha: (1 - p) * 0.5);
        canvas.drawCircle(center, radius, paint);
      }
    }

    // Static decorative ring.
    final ringPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 10
      ..strokeCap = StrokeCap.round
      ..color = color.withValues(alpha: 0.15);
    canvas.drawCircle(center, size.width * 0.40, ringPaint);

    // Rotating arc accent.
    final arcPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 10
      ..strokeCap = StrokeCap.round
      ..shader = SweepGradient(
        colors: [color.withValues(alpha: 0), color],
        startAngle: 0,
        endAngle: math.pi * 2,
      ).createShader(Rect.fromCircle(center: center, radius: size.width * 0.40));
    final sweep = active ? math.pi * 1.1 : math.pi * 0.75;
    canvas.drawArc(
      Rect.fromCircle(center: center, radius: size.width * 0.40),
      progress * 2 * math.pi,
      sweep,
      false,
      arcPaint,
    );
  }

  @override
  bool shouldRepaint(_RingPainter old) =>
      old.progress != progress || old.active != active;
}
