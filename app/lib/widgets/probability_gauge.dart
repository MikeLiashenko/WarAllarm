import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../domain/probability.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';

/// Big "chance of a strike" card for a place: an arc gauge with the percentage,
/// a plain verdict, the official oblast status, the top contributing threats and
/// a per-kind breakdown. Driven by [ThreatProbability].
class ProbabilityGauge extends StatelessWidget {
  const ProbabilityGauge({super.key, required this.prob});

  final ThreatProbability prob;

  Color get _color => prob.cls == 'alarm'
      ? AppColors.threat
      : prob.cls == 'warn'
          ? AppColors.warn
          : AppColors.safe;

  @override
  Widget build(BuildContext context) {
    final color = _color;
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(AppRadii.lg),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              SizedBox(
                width: 118,
                height: 92,
                child: CustomPaint(
                  painter: _GaugePainter(prob.percent / 100, color,
                      AppColors.surface2),
                  child: Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        const SizedBox(height: 14),
                        Text('${prob.percent}%',
                            style: TextStyle(
                                fontSize: 28,
                                height: 1,
                                fontWeight: FontWeight.w900,
                                letterSpacing: -1,
                                fontFeatures: AppText.tabular,
                                color: color)),
                        Text('удар',
                            style: TextStyle(
                                fontSize: 10.5, color: AppColors.textMuted)),
                      ],
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('${prob.place.icon} ${prob.place.name}',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                            fontSize: 14.5,
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary)),
                    const SizedBox(height: 4),
                    Text(prob.verdict,
                        style: TextStyle(
                            fontSize: 18,
                            fontWeight: FontWeight.w800,
                            color: color)),
                    if (prob.officialLevel != null) ...[
                      const SizedBox(height: 6),
                      _officialBadge(prob.officialLevel!),
                    ],
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(prob.advice,
              style: TextStyle(
                  fontSize: 13, height: 1.45, color: AppColors.textSecondary)),
          if (prob.byKind.isNotEmpty) ...[
            const SizedBox(height: 14),
            _kindBars(),
          ],
          if (prob.factors.isNotEmpty) ...[
            const SizedBox(height: 12),
            Text('ГОЛОВНІ ЧИННИКИ',
                style: TextStyle(
                    fontSize: 10.5,
                    fontWeight: FontWeight.w800,
                    letterSpacing: 0.6,
                    color: AppColors.textMuted)),
            const SizedBox(height: 6),
            ...prob.factors.take(3).map(_factorRow),
          ],
        ],
      ),
    );
  }

  Widget _officialBadge(String level) {
    final red = level == 'red';
    final c = red ? AppColors.threat : AppColors.warn;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(
        color: c.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(20),
      ),
      child: Text(
        red ? '🔴 офіційна тривога в області' : '🟡 часткова тривога в області',
        style: TextStyle(
            fontSize: 11, fontWeight: FontWeight.w700, color: c),
      ),
    );
  }

  Widget _kindBars() {
    final entries = prob.byKind.entries.toList()
      ..sort((a, b) => b.value.compareTo(a.value));
    return Column(
      children: [
        for (final e in entries.take(4))
          Padding(
            padding: const EdgeInsets.only(bottom: 6),
            child: Row(
              children: [
                SizedBox(
                  width: 76,
                  child: Text(_kindName(e.key),
                      style: TextStyle(
                          fontSize: 11.5, color: AppColors.textSecondary)),
                ),
                Expanded(
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(5),
                    child: LinearProgressIndicator(
                      value: e.value / 100,
                      minHeight: 8,
                      backgroundColor: AppColors.surface2,
                      valueColor: AlwaysStoppedAnimation(
                          kThreatColors[e.key] ?? AppColors.primary),
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                SizedBox(
                  width: 34,
                  child: Text('${e.value}%',
                      textAlign: TextAlign.right,
                      style: TextStyle(
                          fontSize: 11.5,
                          fontWeight: FontWeight.w700,
                          color: AppColors.textPrimary)),
                ),
              ],
            ),
          ),
      ],
    );
  }

  Widget _factorRow(ProbFactor f) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Row(
        children: [
          Expanded(
            child: Text(
              '${f.target.label}${f.target.amount > 1 ? ' ×${f.target.amount}' : ''} · ${f.distKm.round()} км',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 12.5, color: AppColors.textSecondary),
            ),
          ),
          Text('${f.percent}%',
              style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w700,
                  color: AppColors.textPrimary)),
        ],
      ),
    );
  }

  String _kindName(String cat) => switch (cat) {
        'shahed' => 'Шахед',
        'missile' => 'Ракета',
        'banderol' => 'Бандероль',
        'balist' => 'Балістика',
        'kab' => 'КАБ',
        'avia' => 'Авіація',
        'fpv' => 'ФПВ',
        _ => 'Інше',
      };
}

/// Draws a 220° arc gauge: grey track + coloured value sweep.
class _GaugePainter extends CustomPainter {
  _GaugePainter(this.value, this.color, this.track);
  final double value; // 0..1
  final Color color;
  final Color track;

  static const _start = math.pi * 0.85; // ~153°
  static const _sweep = math.pi * 1.30; // 234°

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Rect.fromLTWH(8, 12, size.width - 16, size.width - 16);
    final trackPaint = Paint()
      ..color = track
      ..style = PaintingStyle.stroke
      ..strokeWidth = 11
      ..strokeCap = StrokeCap.round;
    final valPaint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 11
      ..strokeCap = StrokeCap.round;
    canvas.drawArc(rect, _start, _sweep, false, trackPaint);
    canvas.drawArc(
        rect, _start, _sweep * value.clamp(0.0, 1.0), false, valPaint);
  }

  @override
  bool shouldRepaint(_GaugePainter old) =>
      old.value != value || old.color != color;
}
