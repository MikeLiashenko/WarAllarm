import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_theme.dart';

/// Lightweight charts for the analytics screen (custom painters — no chart
/// package, so they look the same on every platform and follow the theme).

// ─── Stacked bars with tap-to-inspect ─────────────────────────────────────────

class BarSeries {
  const BarSeries(this.label, this.color, this.values);
  final String label;
  final Color color;
  final List<int> values;
}

/// Vertical bars (series stacked bottom→top). Tap / drag a bar to see its
/// numbers; [labelOf] names the bucket ("12.11", "03:00"…).
class StackedBars extends StatefulWidget {
  const StackedBars({
    super.key,
    required this.series,
    required this.labelOf,
    this.height = 170,
    this.axisLabels = const [],
    this.highlight,
  });

  final List<BarSeries> series;
  final String Function(int i) labelOf;
  final double height;

  /// (index, text) labels under the axis.
  final List<(int, String)> axisLabels;

  /// Index drawn brighter (e.g. "now").
  final int? highlight;

  @override
  State<StackedBars> createState() => _StackedBarsState();
}

class _StackedBarsState extends State<StackedBars> {
  int? _sel;

  int get _n => widget.series.isEmpty ? 0 : widget.series.first.values.length;

  int _max() {
    var m = 1;
    for (var i = 0; i < _n; i++) {
      var s = 0;
      for (final se in widget.series) {
        s += se.values[i];
      }
      m = math.max(m, s);
    }
    return m;
  }

  void _pick(Offset p, double w) {
    if (_n == 0) return;
    final i = (p.dx / w * _n).floor().clamp(0, _n - 1);
    if (i != _sel) setState(() => _sel = i);
  }

  @override
  Widget build(BuildContext context) {
    final max = _max();
    // Fixed overall height (LayoutBuilder has no intrinsic size, and the cards
    // sit in equal-height rows).
    return SizedBox(
      height: 34 + 6 + widget.height + 6 + (widget.axisLabels.isNotEmpty ? 14 : 0),
      child: _body(max),
    );
  }

  Widget _body(int max) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SizedBox(
          height: 34,
          child: AnimatedSwitcher(
            duration: const Duration(milliseconds: 150),
            child: _sel == null
                ? Align(
                    key: const ValueKey('legend'),
                    alignment: Alignment.centerLeft,
                    child: Wrap(spacing: 14, children: [
                      for (final s in widget.series) LegendDot(s.color, s.label),
                    ]),
                  )
                : _tooltip(_sel!),
          ),
        ),
        const SizedBox(height: 6),
        LayoutBuilder(builder: (context, c) {
          final w = c.maxWidth;
          return GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTapDown: (d) => _pick(d.localPosition, w),
            onHorizontalDragUpdate: (d) => _pick(d.localPosition, w),
            onLongPressEnd: (_) => setState(() => _sel = null),
            child: SizedBox(
              height: widget.height,
              width: w,
              child: CustomPaint(
                painter: _BarsPainter(
                  series: widget.series,
                  max: max,
                  sel: _sel,
                  highlight: widget.highlight,
                  grid: AppColors.divider,
                  dim: AppColors.textMuted.withValues(alpha: 0.12),
                  labelColor: AppColors.textMuted,
                ),
              ),
            ),
          );
        }),
        const SizedBox(height: 6),
        if (widget.axisLabels.isNotEmpty && _n > 0)
          SizedBox(
            height: 14,
            child: LayoutBuilder(builder: (context, c) {
              final slot = c.maxWidth / _n;
              return Stack(
                clipBehavior: Clip.none,
                children: [
                  for (final (i, t) in widget.axisLabels)
                    Positioned(
                      left: (slot * (i + 0.5) - 24).clamp(0, c.maxWidth - 48),
                      width: 48,
                      child: Text(t,
                          textAlign: i == 0
                              ? TextAlign.left
                              : (i == _n - 1 ? TextAlign.right : TextAlign.center),
                          style: TextStyle(
                              fontSize: 10.5,
                              color: AppColors.textMuted,
                              fontFeatures: AppText.tabular)),
                    ),
                ],
              );
            }),
          ),
      ],
    );
  }

  Widget _tooltip(int i) {
    return Container(
      key: ValueKey(i),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: AppColors.surface2,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(widget.labelOf(i),
              style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w800,
                  color: AppColors.textPrimary,
                  fontFeatures: AppText.tabular)),
          for (final s in widget.series) ...[
            const SizedBox(width: 12),
            Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(color: s.color, shape: BoxShape.circle)),
            const SizedBox(width: 5),
            Text('${s.values[i]} ${s.label.toLowerCase()}',
                style: TextStyle(
                    fontSize: 12.5,
                    color: AppColors.textSecondary,
                    fontFeatures: AppText.tabular)),
          ],
          const SizedBox(width: 8),
          GestureDetector(
            onTap: () => setState(() => _sel = null),
            child: Icon(Icons.close_rounded, size: 15, color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }
}

class _BarsPainter extends CustomPainter {
  _BarsPainter({
    required this.series,
    required this.max,
    required this.sel,
    required this.highlight,
    required this.grid,
    required this.dim,
    required this.labelColor,
  });

  final List<BarSeries> series;
  final int max;
  final int? sel, highlight;
  final Color grid, dim, labelColor;

  @override
  void paint(Canvas canvas, Size size) {
    final n = series.isEmpty ? 0 : series.first.values.length;
    if (n == 0) return;
    // Grid: 3 dashed lines + max label.
    final gp = Paint()
      ..color = grid
      ..strokeWidth = 1;
    for (var k = 1; k <= 3; k++) {
      final y = size.height - size.height * k / 3;
      for (double x = 0; x < size.width; x += 6) {
        canvas.drawLine(Offset(x, y), Offset(math.min(x + 3, size.width), y), gp);
      }
    }
    final tp = TextPainter(
      text: TextSpan(text: '$max', style: TextStyle(fontSize: 9.5, color: labelColor)),
      textDirection: TextDirection.ltr,
    )..layout();
    tp.paint(canvas, Offset(size.width - tp.width, 2));

    final slot = size.width / n;
    final bw = math.max(1.5, math.min(slot * 0.68, 22.0));
    final r = Radius.circular(math.min(bw / 2, 5));
    for (var i = 0; i < n; i++) {
      final x = slot * i + (slot - bw) / 2;
      final faded = sel != null && sel != i;
      var total = 0;
      for (final s in series) {
        total += s.values[i];
      }
      if (total == 0) {
        canvas.drawRRect(
            RRect.fromRectAndRadius(Rect.fromLTWH(x, size.height - 3, bw, 3), r),
            Paint()..color = dim);
        continue;
      }
      var y = size.height;
      for (var si = 0; si < series.length; si++) {
        final v = series[si].values[i];
        if (v == 0) continue;
        final h = math.max(2.0, v / max * (size.height - 14));
        final top = y - h;
        final isTop = series.skip(si + 1).every((s) => s.values[i] == 0);
        final rect = RRect.fromRectAndCorners(Rect.fromLTWH(x, top, bw, h),
            topLeft: isTop ? r : Radius.zero, topRight: isTop ? r : Radius.zero);
        final c = series[si].color;
        canvas.drawRRect(
          rect,
          Paint()
            ..shader = LinearGradient(
              begin: Alignment.topCenter,
              end: Alignment.bottomCenter,
              colors: [
                c.withValues(alpha: faded ? 0.3 : 1),
                c.withValues(alpha: faded ? 0.18 : (i == highlight ? 0.95 : 0.62)),
              ],
            ).createShader(rect.outerRect),
        );
        y = top - (series.length > 1 ? 1.5 : 0);
      }
    }
    if (sel != null) {
      final x = slot * sel! + slot / 2;
      canvas.drawLine(Offset(x, 0), Offset(x, size.height),
          Paint()
            ..color = labelColor.withValues(alpha: 0.35)
            ..strokeWidth = 1);
    }
  }

  @override
  bool shouldRepaint(_BarsPainter o) =>
      o.sel != sel || o.max != max || o.series != series || o.grid != grid;
}

// ─── Sparkline ────────────────────────────────────────────────────────────────

class Sparkline extends StatelessWidget {
  const Sparkline(this.values, {super.key, required this.color, this.height = 32});
  final List<int> values;
  final Color color;
  final double height;

  @override
  Widget build(BuildContext context) => SizedBox(
        height: height,
        width: double.infinity,
        child: CustomPaint(painter: _SparkPainter(values, color)),
      );
}

class _SparkPainter extends CustomPainter {
  _SparkPainter(this.v, this.c);
  final List<int> v;
  final Color c;

  @override
  void paint(Canvas canvas, Size s) {
    if (v.length < 2) return;
    final m = math.max(1, v.reduce(math.max));
    Offset pt(int i) => Offset(s.width * i / (v.length - 1), s.height - 2 - (s.height - 4) * v[i] / m);
    final path = Path()..moveTo(pt(0).dx, pt(0).dy);
    for (var i = 1; i < v.length; i++) {
      final a = pt(i - 1), b = pt(i);
      final mx = (a.dx + b.dx) / 2;
      path.cubicTo(mx, a.dy, mx, b.dy, b.dx, b.dy);
    }
    final fill = Path.from(path)
      ..lineTo(s.width, s.height)
      ..lineTo(0, s.height)
      ..close();
    canvas.drawPath(
        fill,
        Paint()
          ..shader = LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [c.withValues(alpha: 0.28), c.withValues(alpha: 0)],
          ).createShader(Offset.zero & s));
    canvas.drawPath(
        path,
        Paint()
          ..color = c
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.8
          ..strokeCap = StrokeCap.round);
    canvas.drawCircle(pt(v.length - 1), 2.6, Paint()..color = c);
  }

  @override
  bool shouldRepaint(_SparkPainter o) => o.v != v || o.c != c;
}

// ─── Donut ────────────────────────────────────────────────────────────────────

class DonutChart extends StatelessWidget {
  const DonutChart({
    super.key,
    required this.parts,
    this.size = 150,
    this.thickness = 18,
    this.center,
  });

  final List<(Color, int)> parts;
  final double size, thickness;
  final Widget? center;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: size,
        height: size,
        child: CustomPaint(
          painter: _DonutPainter(parts, thickness, AppColors.textMuted.withValues(alpha: 0.14)),
          child: Center(child: center),
        ),
      );
}

class _DonutPainter extends CustomPainter {
  _DonutPainter(this.parts, this.t, this.track);
  final List<(Color, int)> parts;
  final double t;
  final Color track;

  @override
  void paint(Canvas canvas, Size s) {
    final rect = Rect.fromCircle(center: s.center(Offset.zero), radius: s.width / 2 - t / 2);
    canvas.drawArc(rect, 0, math.pi * 2, false,
        Paint()
          ..color = track
          ..style = PaintingStyle.stroke
          ..strokeWidth = t);
    final total = parts.fold<int>(0, (a, b) => a + b.$2);
    if (total == 0) return;
    const gap = 0.045;
    var a = -math.pi / 2;
    for (final (c, v) in parts) {
      if (v == 0) continue;
      final sweep = v / total * math.pi * 2;
      final g = parts.where((p) => p.$2 > 0).length > 1 ? gap : 0.0;
      canvas.drawArc(rect, a + g / 2, math.max(0.01, sweep - g), false,
          Paint()
            ..color = c
            ..style = PaintingStyle.stroke
            ..strokeWidth = t
            ..strokeCap = StrokeCap.butt);
      a += sweep;
    }
  }

  @override
  bool shouldRepaint(_DonutPainter o) => o.parts != parts;
}

// ─── Heatmap (weekday × hour) ─────────────────────────────────────────────────

class HeatmapGrid extends StatefulWidget {
  const HeatmapGrid({
    super.key,
    required this.data,
    required this.rowLabels,
    required this.color,
  });

  /// [row][col] counts (7 × 24).
  final List<List<int>> data;
  final List<String> rowLabels;
  final Color color;

  @override
  State<HeatmapGrid> createState() => _HeatmapGridState();
}

class _HeatmapGridState extends State<HeatmapGrid> {
  (int, int)? _sel;

  @override
  Widget build(BuildContext context) {
    final rows = widget.data.length, cols = widget.data.isEmpty ? 0 : widget.data[0].length;
    var max = 1;
    for (final r in widget.data) {
      for (final v in r) {
        max = math.max(max, v);
      }
    }
    const labelW = 26.0;
    const h = 24.0;
    return SizedBox(
      height: rows * h + 6 + 14 + 10 + 16,
      child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        LayoutBuilder(builder: (context, c) {
          final cell = (c.maxWidth - labelW) / cols;
          return Column(
            children: [
              for (var r = 0; r < rows; r++)
                SizedBox(
                  height: h,
                  child: Row(
                    children: [
                      SizedBox(
                        width: labelW,
                        child: Text(widget.rowLabels[r],
                            style: TextStyle(
                                fontSize: 10.5,
                                fontWeight: FontWeight.w600,
                                color: r >= 5 ? AppColors.textSecondary : AppColors.textMuted)),
                      ),
                      for (var col = 0; col < cols; col++)
                        GestureDetector(
                          onTap: () => setState(() => _sel = _sel == (r, col) ? null : (r, col)),
                          child: Container(
                            width: cell,
                            height: h,
                            padding: const EdgeInsets.all(1.2),
                            child: DecoratedBox(
                              decoration: BoxDecoration(
                                borderRadius: BorderRadius.circular(math.min(4, cell / 4)),
                                border: _sel == (r, col)
                                    ? Border.all(color: AppColors.textPrimary, width: 1.4)
                                    : null,
                                color: widget.data[r][col] == 0
                                    ? AppColors.textMuted.withValues(alpha: 0.07)
                                    : widget.color.withValues(
                                        alpha: 0.18 + 0.82 * math.pow(widget.data[r][col] / max, 0.8)),
                              ),
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
            ],
          );
        }),
        const SizedBox(height: 6),
        Row(
          children: [
            const SizedBox(width: labelW),
            for (final t in ['00', '06', '12', '18', '23'])
              Expanded(
                child: Text(t,
                    textAlign: t == '23' ? TextAlign.right : TextAlign.left,
                    style: TextStyle(fontSize: 10.5, color: AppColors.textMuted)),
              ),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            Expanded(
              child: _sel == null
                  ? Text('Торкнись клітинки — скільки тривог у цю годину',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontSize: 11.5, color: AppColors.textMuted))
                  : Text(
                      '${widget.rowLabels[_sel!.$1]}, ${_sel!.$2.toString().padLeft(2, '0')}:00–'
                      '${_sel!.$2.toString().padLeft(2, '0')}:59 — ${widget.data[_sel!.$1][_sel!.$2]} тривог',
                      style: TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w700,
                          color: AppColors.textPrimary)),
            ),
            Text('менше ', style: TextStyle(fontSize: 10.5, color: AppColors.textMuted)),
            for (final a in [0.18, 0.4, 0.62, 0.84, 1.0])
              Container(
                width: 11,
                height: 11,
                margin: const EdgeInsets.only(left: 2),
                decoration: BoxDecoration(
                  color: widget.color.withValues(alpha: a),
                  borderRadius: BorderRadius.circular(3),
                ),
              ),
            Text(' більше', style: TextStyle(fontSize: 10.5, color: AppColors.textMuted)),
          ],
        ),
      ],
      ),
    );
  }
}

// ─── 24-hour clock ────────────────────────────────────────────────────────────

/// Radial bars around a clock face: alarms by hour; night half shaded.
class DayClock extends StatelessWidget {
  const DayClock({
    super.key,
    required this.hours,
    required this.color,
    this.size = 180,
    this.center,
  });

  final List<int> hours;
  final Color color;
  final double size;
  final Widget? center;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: size,
        height: size,
        child: CustomPaint(
          painter: _ClockPainter(
            hours,
            color,
            AppColors.night,
            AppColors.textMuted,
            AppColors.divider,
          ),
          child: Center(child: center),
        ),
      );
}

class _ClockPainter extends CustomPainter {
  _ClockPainter(this.h, this.c, this.night, this.label, this.grid);
  final List<int> h;
  final Color c, night, label, grid;

  @override
  void paint(Canvas canvas, Size s) {
    final ctr = s.center(Offset.zero);
    final outer = s.width / 2 - 14;
    final inner = outer * 0.46;
    final m = math.max(1, h.reduce(math.max));
    double ang(num hour) => -math.pi / 2 + hour / 24 * math.pi * 2;

    // Night sector 22:00–06:00.
    canvas.drawArc(Rect.fromCircle(center: ctr, radius: outer), ang(22), math.pi * 2 * 8 / 24, true,
        Paint()..color = night.withValues(alpha: 0.08));
    canvas.drawCircle(ctr, inner - 2, Paint()..color = grid.withValues(alpha: 0.6));
    canvas.drawCircle(
        ctr,
        outer,
        Paint()
          ..color = grid
          ..style = PaintingStyle.stroke);

    for (var i = 0; i < 24; i++) {
      final a = ang(i + 0.5);
      final len = (outer - inner) * (h[i] / m);
      final dir = Offset(math.cos(a), math.sin(a));
      canvas.drawLine(
        ctr + dir * inner,
        ctr + dir * (inner + math.max(2, len)),
        Paint()
          ..color = h[i] == 0 ? label.withValues(alpha: 0.2) : c.withValues(alpha: 0.35 + 0.65 * h[i] / m)
          ..strokeWidth = math.max(3, outer * 0.085)
          ..strokeCap = StrokeCap.round,
      );
    }
    for (final (hr, t) in [(0, '00'), (6, '06'), (12, '12'), (18, '18')]) {
      final a = ang(hr);
      final tp = TextPainter(
        text: TextSpan(text: t, style: TextStyle(fontSize: 10, color: label, fontWeight: FontWeight.w600)),
        textDirection: TextDirection.ltr,
      )..layout();
      final p = ctr + Offset(math.cos(a), math.sin(a)) * (outer + 8);
      tp.paint(canvas, p - Offset(tp.width / 2, tp.height / 2));
    }
  }

  @override
  bool shouldRepaint(_ClockPainter o) => o.h != h || o.c != c;
}

// ─── Small pieces ─────────────────────────────────────────────────────────────

class LegendDot extends StatelessWidget {
  const LegendDot(this.color, this.label, {super.key, this.value});
  final Color color;
  final String label;
  final String? value;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
              width: 9,
              height: 9,
              decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(3))),
          const SizedBox(width: 6),
          Text(label, style: TextStyle(fontSize: 12, color: AppColors.textSecondary)),
          if (value != null) ...[
            const SizedBox(width: 5),
            Text(value!,
                style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w800,
                    color: AppColors.textPrimary,
                    fontFeatures: AppText.tabular)),
          ],
        ],
      );
}

/// Horizontal ranked bar: label · bar (one or two segments) · value.
class RankBar extends StatelessWidget {
  const RankBar({
    super.key,
    required this.label,
    required this.value,
    required this.max,
    required this.color,
    this.second = 0,
    this.secondColor,
    this.leading,
    this.trailing,
  });

  final String label;
  final int value, max, second;
  final Color color;
  final Color? secondColor;
  final Widget? leading;
  final String? trailing;

  @override
  Widget build(BuildContext context) {
    final total = value + second;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              if (leading != null) ...[leading!, const SizedBox(width: 8)],
              Expanded(
                child: Text(label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        color: AppColors.textPrimary)),
              ),
              Text(trailing ?? '$total',
                  style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w800,
                      color: AppColors.textPrimary,
                      fontFeatures: AppText.tabular)),
            ],
          ),
          const SizedBox(height: 6),
          Builder(builder: (context) {
            final m = math.max(math.max(1, max), value + second);
            // flex units out of 1000 so tiny values still get a sliver.
            final fa = value == 0 ? 0 : math.max(12, value * 1000 ~/ m);
            final fb = second == 0 ? 0 : math.max(12, second * 1000 ~/ m);
            final rest = math.max(0, 1000 - fa - fb);
            return Container(
              height: 8,
              clipBehavior: Clip.antiAlias,
              decoration: BoxDecoration(
                color: AppColors.textMuted.withValues(alpha: 0.1),
                borderRadius: BorderRadius.circular(4),
              ),
              child: Row(
                children: [
                  if (value > 0)
                    Expanded(
                      flex: fa,
                      child: Container(
                      decoration: BoxDecoration(
                        gradient: LinearGradient(colors: [color.withValues(alpha: 0.7), color]),
                        borderRadius: BorderRadius.horizontal(
                          left: const Radius.circular(4),
                          right: Radius.circular(second > 0 ? 0 : 4),
                        ),
                      ),
                    ),
                    ),
                  if (second > 0)
                    Expanded(
                      flex: fb,
                      child: Container(
                        decoration: BoxDecoration(
                          color: secondColor ?? AppColors.threat,
                          borderRadius: const BorderRadius.horizontal(right: Radius.circular(4)),
                        ),
                      ),
                    ),
                  if (rest > 0) Spacer(flex: rest),
                ],
              ),
            );
          }),
        ],
      ),
    );
  }
}
